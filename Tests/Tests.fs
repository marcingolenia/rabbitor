module Tests

open System
open System.Text
open Newtonsoft.Json
open RabbitMQ.Client
open RabbitMQ.Client.Events
open Xunit
open System.Threading.Tasks
open FsUnit.Xunit

// docker run --name rabbitor -d -p 15672:15672 -p 5672:5672 rabbitmq:3.9.1-management-alpine
// docker exec rabbitor rabbitmq-plugins list
// docker exec rabbitor rabbitmq-plugins enable rabbitmq_stream

// BUS

[<Literal>]
let PrefetchCount = 4us

type Bus =
    { Connection: IConnection
      PublishChannel: IModel
      ConsumeChannels: IModel list }
    static member Connect(host: string) =
        let factory = ConnectionFactory()
        factory.DispatchConsumersAsync <- true
        let connection = factory.CreateConnection(ResizeArray<string> [ host ])
        let publishingChannel = connection.CreateModel()
        { Connection = connection
          PublishChannel = publishingChannel
          ConsumeChannels = [] }

    interface IDisposable with
        member this.Dispose() = this.Connection.Dispose()

    static member Subscribe<'a> handler bus =
        let handlerWrapped =
            AsyncEventHandler<BasicDeliverEventArgs>
                (fun (sender: obj) (delivery: BasicDeliverEventArgs) ->
                    let event =
                        JsonConvert.DeserializeObject<'a>(Encoding.UTF8.GetString(delivery.Body.ToArray()))
                    handler event |> Async.StartAsTask :> Task)

        let channels =
            [ 1 .. int PrefetchCount / 2 ]
            |> List.map
                (fun _ ->
                    let channel = bus.Connection.CreateModel()
                    channel.BasicQos(0u, PrefetchCount, true)
                    let consumer = AsyncEventingBasicConsumer(channel)
                    consumer.add_Received handlerWrapped
                    channel.BasicConsume(
                        "my-queue",
                        true,
                        "tag",
                        null, //([ ("x-stream-offset", 3 :> obj) ] |> dict),
                        consumer
                    )
                    |> ignore
                    channel)
        { bus with ConsumeChannels = channels }
        
    static member Publish (bus: Bus) event =
        let bytes = event |> JsonConvert.SerializeObject
                          |> Encoding.UTF8.GetBytes
        bus.PublishChannel.BasicPublish(
                exchange = "my-exchange",
                routingKey = "",
                basicProperties = null,
                body = ReadOnlyMemory bytes
            )

// EVENTS
type ManKilled = { Name: string }
type ManResurrected = { Name: string; When: DateTime }

type Events =
    | ManKilled of ManKilled
    | ManResurrected of ManResurrected

// Queue per event - mainly because of streams and single-threaded queue model
// GetSream offset<Event> (int | timespan | int)

[<Fact>]
let ``RabbitMq PubSub`` () =
    let a = System.Threading.ThreadPool.GetMaxThreads()
    let mutable handlerWasCalled = false
    let mutable handlingCount = 0
    let handler = (fun event ->
                    // This runs one by one. Fuck, check if rebus parallize and steal!
                    printfn $"Executing %A{event} START"
                    printfn $"Executing %A{event} END"
                    async {
                        handlerWasCalled <- true
                        handlingCount <- handlingCount + 1
                        return Ok ()
                    })
    use bus = Bus.Connect("localhost")
              |> Bus.Subscribe<Events> handler

    [ 1 .. 1 ]
    |> List.iter
        (fun i ->
            Bus.Publish bus (ManKilled { Name = "Marcinek 1" })
            Bus.Publish bus (ManKilled { Name = "Marcinek 2" })
            Bus.Publish bus (ManKilled { Name = "Marcinek 3" })
            Bus.Publish bus (ManKilled { Name = "Marcinek 4" })
            Bus.Publish bus (ManKilled { Name = "Marcinek 5" })
            Bus.Publish bus (ManKilled { Name = "Marcinek 6" })
            Bus.Publish bus (ManKilled { Name = "Marcinek 7" })
            Bus.Publish bus (ManKilled { Name = "Marcinek 8" })
            Bus.Publish bus (ManKilled { Name = "Marcinek 9" })
            Bus.Publish bus (ManKilled { Name = "Marcinek 10" })
            
            Task.Delay(20000)
            |> Async.AwaitTask
            |> Async.RunSynchronously)

    handlerWasCalled |> should equal true
    handlingCount |> should equal 10
