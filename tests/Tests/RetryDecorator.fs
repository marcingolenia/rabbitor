module RetryDecorator

open System
open Tests.Contracts
open Xunit
open System.Threading.Tasks
open FsUnit.Xunit
open Rabbitor

[<Fact>]
let ``Failures are retried given number of times`` () =
    // Arrange
    async {
        let mutable eventsProcessed, promise = [], TaskCompletionSource()
        let expectedEvents =
            [ E.CoderHired { Name = $"{Guid.NewGuid()}" }
              E.CoderFired
                  { Name = $"{Guid.NewGuid()}"
                    When = DateTime.Now }
              E.CoderDied
                  { Name = $"{Guid.NewGuid()}"
                    When = DateTime.Now } ]
        let handler =
            (fun event ->
                async {
                    eventsProcessed <- eventsProcessed @ [event]
                    match event with
                    | E.CoderHired _ ->
                        if eventsProcessed.Length = 9 then promise.SetResult()
                        return Error("sth bad happened" :> obj)
                    | E.CoderDied _ ->
                        if eventsProcessed.Length = 9 then promise.SetResult()
                        return Ok()
                    | E.CoderFired _ ->
                        if eventsProcessed.Length = 9 then promise.SetResult()
                        failwith "Exception!!"
                        return Ok()
                })
        let decoratedHandler =
            ConsumerDecorators.handleWithDecors handler [ConsumerDecorators.retry 3]
        use bus =
            Bus.connect [ "localhost" ]
            |> Bus.initPublisher<E.Events>
            |> Bus.subscribe<E.Events> decoratedHandler
        // Act
        expectedEvents |> List.iter (Bus.publish bus)
        // Assert
        do! promise.Task |> Async.AwaitTask
        // 4 - retries on Error | 4 - retries on exception | 1 normal handling
        eventsProcessed.Length |> should equal 9
    }