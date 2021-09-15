module CustomDecorators

open System
open Tests.Contracts
open Xunit
open System.Threading.Tasks
open FsUnit.Xunit
open Rabbitor
open ConsumerDecorators

[<Fact>]
let ``Custom Decorators wrap handler nad are fired in proper order`` () =
    // Arrange
    let mutable decorator1CalledAt, decorator2CalledAt = DateTime.Today, DateTime.Today
    let decorator1 =
        fun next event ->
            async {
                decorator1CalledAt <- DateTime.Now
                return! next event
            }
    let decorator2 =
        fun next event ->
            async {
                decorator2CalledAt <- DateTime.Now
                return! next event
            }

    async {
        let promise = TaskCompletionSource()
        let expectedEvent: F.Whatever7 = { Name = $"{Guid.NewGuid()}" }
        let handler =
            (fun _ ->
                async {
                    promise.SetResult()
                    return Ok()
                })
            |> decorate [ decorator1; decorator2 ]
        use bus =
            Bus.connect [ "localhost" ]
            |> Bus.initPublisher<F.Whatever7>
            |> Bus.subscribe<F.Whatever7> handler
        // Act
        expectedEvent |> Bus.publish bus
        do! promise.Task |> Async.AwaitTask
        // Assert
        decorator1CalledAt.TimeOfDay |> should be (lessThan decorator2CalledAt.TimeOfDay)
    }
