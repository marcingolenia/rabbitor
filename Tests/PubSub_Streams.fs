module Tests

open System
open Tests.Contracts
open Xunit
open System.Threading.Tasks
open FsUnit.Xunit

[<Fact>]
let ``Subscribing and handling to published messages using different types of events works`` () =
    // Arrange
    async {
        let mutable actualHandledEventsOf_A, promise_A = [], TaskCompletionSource()
        let expectedEvents_A = 
            [ 1 .. 1 ] |> List.map(fun i -> A.ManKilled { Name = $"{Guid.NewGuid()} %i{i}" })
        let mutable actualHandledEventsOf_B, promise_B = [], TaskCompletionSource()
        let expectedEvents_B = 
            [ 1 .. 1 ] |> List.map(fun i -> B.ManAsked {| Name = $"{Guid.NewGuid()} %i{i}"; Question = "???" |})
        let handlerA = ( fun event ->
                         async {
                             actualHandledEventsOf_A <- actualHandledEventsOf_A @ [ event ]
                             if actualHandledEventsOf_A.Length = expectedEvents_A.Length then
                                 promise_A.SetResult()
                             return Ok ()
                         })
        let handlerB = ( fun event ->
                         async {
                             actualHandledEventsOf_B <- actualHandledEventsOf_B @ [ event ]
                             if expectedEvents_B.Length = actualHandledEventsOf_B.Length then
                                 promise_B.SetResult()
                             return Ok ()
                         })
        use bus = Bus.connect(["localhost"])
                  |> Bus.initPublisher<A.Events>   
                  |> Bus.initPublisher<B.Events>
                  |> Bus.subscribe<A.Events> handlerA
                  |> Bus.subscribe<B.Events> handlerB
                  
        // Act
        expectedEvents_B |> List.iter(Bus.publish bus)
        expectedEvents_A |> List.iter(Bus.publish bus)
        // Assert
        do! promise_A.Task |> Async.AwaitTask
        do! promise_B.Task |> Async.AwaitTask
        actualHandledEventsOf_A |> should equal expectedEvents_A
        actualHandledEventsOf_B |> should equal expectedEvents_B
    }

[<Fact>]
let ``RabbitMq Streams`` () =
    // Arrange
    async {
        let expectedEvents = [for i in 1 .. 10 do S.ManKilled { Name = $"{i}" }] 
        let mutable actualHandledEvents, promise = [], TaskCompletionSource()
        let handler = (fun event ->
                        async {
                            actualHandledEvents <- actualHandledEvents @ [event]
                            if actualHandledEvents.Length = expectedEvents.Length then
                                     promise.SetResult()
                            return Ok ()
                        })
        use bus = Bus.connect(["localhost"])
                  |> Bus.initStreamedPublisher<S.Events>
        expectedEvents |> List.iter (Bus.publish bus)
        // Act
        Bus.consumeStream<S.Events> handler 0 bus |> ignore
        do! promise.Task |> Async.AwaitTask
        actualHandledEvents.[0 .. 9] |> should equal expectedEvents
    } 