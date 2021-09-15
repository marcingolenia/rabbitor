module Streams

open System
open System.Threading
open Tests.Contracts
open Xunit
open System.Threading.Tasks
open FsUnit.Xunit
open Rabbitor
 
let ``Stream handling timeout`` = TimeSpan.FromSeconds 10.0

let rec checkMessagesUntil (bus: Bus) amount =
    async {
        if bus.NumberOfMessagesInStream<S.Events>() = amount then ()
        else
            do! Async.Sleep 2000
            do! checkMessagesUntil bus amount
    }

[<Fact>]
let ``Stream can be consumed starting with given offset`` () =
    // Arrange
    async {
        let expectedEvents =
            [ for i in 1 .. 10 do
                  S.ManKilled { Name = $"{i}" } ]
        let timeoutStreamAwait =
            new CancellationTokenSource(``Stream handling timeout``)
        let mutable actualHandledEvents, promise = [], TaskCompletionSource()
        timeoutStreamAwait.Token.Register(fun _ -> promise.SetCanceled()) |> ignore
        use bus =
            Bus.connect [ "localhost" ] |> Bus.initStreamedPublisher<S.Events>
        let messagesAmount =
            int <| bus.Publication.Channel.MessageCount($"{typeof<S.Events>.FullName}-stream")
        expectedEvents |> List.iter (Bus.publish bus)
        do! checkMessagesUntil bus (uint32 <| messagesAmount + expectedEvents.Length)
        let handler =
            (fun event ->
                async {
                    actualHandledEvents <- actualHandledEvents @ [ event ]
                    if actualHandledEvents.Length = expectedEvents.Length + messagesAmount then
                        promise.SetResult()
                    return Ok()
                })
        // Act
        Bus.consumeStream<S.Events> handler 0u bus |> ignore
        do! promise.Task |> Async.AwaitTask
        // Assert
        actualHandledEvents.[actualHandledEvents.Length - 10..]
        |> should equal expectedEvents
    }

[<Fact>]
let ``When stream is consumed with offset greater than total messages count then nothing happens - handler is not fired.``
    ()
    =
    // Arrange
    async {
        let eventsToSend =
            [ for i in 1 .. 5 do
                  S.ManKilled { Name = $"{i}" } ]
        let mutable wasHandlerTriggered = false
        let timeoutStreamAwait =
            new CancellationTokenSource(``Stream handling timeout``)
        let promise = TaskCompletionSource()
        timeoutStreamAwait.Token.Register(fun _ -> promise.SetResult()) |> ignore
        use bus =
            Bus.connect [ "localhost" ] |> Bus.initStreamedPublisher<S.Events>
        eventsToSend |> List.iter (Bus.publish bus)
        do! checkMessagesUntil bus ((bus.NumberOfMessagesInStream<S.Events>()) + uint32 eventsToSend.Length)
        let handler =
            (fun _ ->
                async {
                    wasHandlerTriggered <- true
                    return Ok()
                })
        // Act
        Bus.consumeStream<S.Events> handler 100_000u bus |> ignore
        do! promise.Task |> Async.AwaitTask
        // Assert
        wasHandlerTriggered |> should equal false
    }
