module Tests.ParallelSubscriptions

open System.Collections.Concurrent
open System.Threading.Tasks
open Rabbitor
open Xunit
open FsUnit.Xunit
open Tests.Contracts

[<Fact>]
let ``Parallelized subscriptions handle events simultaneously`` () =
    // Arrange
    async {
        let promise = TaskCompletionSource()
        let actualEvents = ConcurrentBag<F.Whatever5>()
        let expectedEvents =
            [ 1 .. 100 ] |> List.map (fun i -> ({ Name = $"%i{i}" }: F.Whatever5))
        let handler =
            (fun event ->
                async {
                    actualEvents.Add event
                    if actualEvents.Count = expectedEvents.Length then
                        promise.SetResult()
                    return Ok()
                })
        use bus =
            Bus.connect [ "localhost" ]
            |> Bus.initPublisher<F.Whatever5>
            |> Bus.parallelSubscribe<F.Whatever5> 5 handler
        // Act
        Bus.publishMany bus expectedEvents
        // Assert
        do! promise.Task |> Async.AwaitTask
        let actualEvents = actualEvents.ToArray() |> Array.toList
        actualEvents |> should not' (equal expectedEvents)
        actualEvents
        |> List.sortBy (fun evt -> int evt.Name)
        |> should equal expectedEvents
    }
