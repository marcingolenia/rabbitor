module Tests.CustomJsonSerializer

open System
open System.Threading.Tasks
open Rabbitor
open Thoth.Json.Net
open Xunit
open Tests.Contracts
open FsUnit.Xunit

[<Fact>]
let ``custom json serializer/deserializer can be used`` () =
    async {
        // Arrange
        let promise = TaskCompletionSource()
        let indentSize = 8
        let mutable serializedJson = ""
        let mutable actualEvent: F.Whatever6 option = None
        let handler =
            fun event ->
                async {
                    actualEvent <- Some event
                    promise.SetResult()
                    return Ok()
                }
        let myDeserialize =
            fun payload ->
                serializedJson <- payload
                Decode.Auto.unsafeFromString<F.Whatever6> (
                    payload,
                    CaseStrategy.PascalCase
                )
        let mySerialize =
            fun event -> Encode.Auto.toString (indentSize, event, CaseStrategy.PascalCase)

        use bus =
            Bus.connect [ "localhost" ]
            |> Bus.initPublisher<F.Whatever6>
            |> Bus.parallelSubscribeWithDeserializer<F.Whatever6> myDeserialize 1 handler
        // Act
        let expectedEvent: F.Whatever6 = { Name = "Gulgulgul" }
        Bus.serializeAndPublish bus mySerialize expectedEvent
        do! promise.Task |> Async.AwaitTask
        // Assert
        let indentedLine =
            serializedJson.Split(
                [| Environment.NewLine |],
                StringSplitOptions.RemoveEmptyEntries
            ).[1]
        let numberOfWhitespaces =
            (0, indentedLine)
            ||> Seq.fold (fun state ->
                function
                | ' ' -> state + 1
                | _ -> state
            )
        numberOfWhitespaces |> should equal (indentSize + 1)
        actualEvent |> should equal (Some expectedEvent)
    }
