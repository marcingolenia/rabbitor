module PubSub

open System
open Tests.Contracts
open Xunit
open System.Threading.Tasks
open FsUnit.Xunit
open Rabbitor

[<Fact>]
let ``Subscribing and handling to published messages using different types of events works``
    ()
    =
    // Arrange
    async {
        let mutable actualHandledEventsOf_A, promise_A = [], TaskCompletionSource()
        let expectedEvents_A =
            [ 1 .. 4 ]
            |> List.map (fun i -> A.ManKilled { Name = $"{Guid.NewGuid()} %i{i}" })
        let mutable actualHandledEventsOf_B, promise_B = [], TaskCompletionSource()
        let expectedEvents_B =
            [ 1 .. 2 ]
            |> List.map (fun i -> B.ManAsked {| Name = $"%i{i}"; Question = "???" |})
        let handlerA =
            (fun event ->
                async {
                    actualHandledEventsOf_A <- actualHandledEventsOf_A @ [ event ]
                    if actualHandledEventsOf_A.Length = expectedEvents_A.Length then
                        promise_A.SetResult()
                    return Ok()
                })
        let handlerB =
            (fun event ->
                async {
                    actualHandledEventsOf_B <- actualHandledEventsOf_B @ [ event ]
                    if expectedEvents_B.Length = actualHandledEventsOf_B.Length then
                        promise_B.SetResult()
                    return Ok()
                })
        use bus =
            Bus.connect [ "localhost" ]
            |> Bus.initPublisher<A.Events>
            |> Bus.initPublisher<B.Events>
            |> Bus.subscribe<A.Events> handlerA
            |> Bus.subscribe<B.Events> handlerB

        // Act
        expectedEvents_B |> List.iter (Bus.publish bus)
        expectedEvents_A |> List.iter (Bus.publish bus)
        // Assert
        do! promise_A.Task |> Async.AwaitTask
        do! promise_B.Task |> Async.AwaitTask
        actualHandledEventsOf_A |> should equal expectedEvents_A
        actualHandledEventsOf_B |> should equal expectedEvents_B
    }
    
[<Fact>]
let ``Subscribing and handling to published messages batch works``
    ()
    =
    // Arrange
    async {
        let mutable actualHandledEvents, promise = [], TaskCompletionSource()
        let expectedEvents =
            [ 1 .. 4 ]
            |> List.map (fun i -> ({ Name = $"{Guid.NewGuid()} %i{i}" }: F.Whatever4))
        let handler =
            (fun event ->
                async {
                    actualHandledEvents <- actualHandledEvents @ [ event ]
                    if actualHandledEvents.Length = expectedEvents.Length then
                        promise.SetResult()
                    return Ok()
                })
        use bus =
            Bus.connect [ "localhost" ]
            |> Bus.initPublisher<F.Whatever4>
            |> Bus.subscribe<F.Whatever4> handler
        // Act
        Bus.publishMany bus expectedEvents
        // Assert
        do! promise.Task |> Async.AwaitTask
        actualHandledEvents |> should equal expectedEvents
    }

[<Fact>]
let ``Failures are ignored and remaining events are consumed.`` () =
    // Arrange
    async {
        let mutable actualHandledEvents, promise = [], TaskCompletionSource()
        let expectedEvents =
            [ D.FrogKissed
                { Name = $"{Guid.NewGuid()}"
                  When = DateTime.Now }
              D.FrogDivorced
                  { Name = $"{Guid.NewGuid()}"
                    When = DateTime.Now }
              D.FrogMarried { Name = $"{Guid.NewGuid()}" }
              D.FrogKissed
                  { Name = $"{Guid.NewGuid()}"
                    When = DateTime.Now } ]
        let handler =
            (fun event ->
                async {
                    match event with
                    | D.FrogDivorced _ -> return Error("sth bad happened" :> obj)
                    | D.FrogMarried _ ->
                        failwith "Exception!!"
                        return Ok()
                    | D.FrogKissed evt ->
                        actualHandledEvents <- actualHandledEvents @ [ evt ]
                        if (actualHandledEvents.Length = 2) then
                            promise.SetResult()
                        return Ok()
                })
        use bus =
            Bus.connect [ "localhost" ]
            |> Bus.initPublisher<D.Events>
            |> Bus.subscribe<D.Events> handler
        // Act
        expectedEvents |> List.iter (Bus.publish bus)
        // Assert
        do! promise.Task |> Async.AwaitTask
        actualHandledEvents
        |> should
            equal
            (expectedEvents
             |> List.choose
                 (function
                 | D.Events.FrogKissed evt -> Some evt
                 | _ -> None))
    }
