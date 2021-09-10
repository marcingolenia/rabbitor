module Tests

open System
open System.Threading
open Tests.Contracts
open Xunit
open System.Threading.Tasks
open FsUnit.Xunit
open Rabbitor

[<Literal>]
let ``Seconds to wait for stream write`` = 4_000

let ``Stream handling timeout`` = TimeSpan.FromSeconds 10.0

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
            int <| bus.PublishChannel.MessageCount($"{typeof<S.Events>.FullName}-stream")
        expectedEvents |> List.iter (Bus.publish bus)
        do! Async.Sleep(``Seconds to wait for stream write``)
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
        do! Async.Sleep(``Seconds to wait for stream write``)
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
