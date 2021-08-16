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
        
    static member InitializePublisher<'a> (bus: Bus) =
        let name = typeof<'a>.FullName
        bus.PublishChannel.ExchangeDeclare(name, ExchangeType.Fanout)
        bus

    static member Subscribe<'a> handler bus =
        let handlerWrapped =
            AsyncEventHandler<BasicDeliverEventArgs>
                (fun (sender: obj) (delivery: BasicDeliverEventArgs) ->
                    let event =
                        JsonConvert.DeserializeObject<'a>(Encoding.UTF8.GetString(delivery.Body.ToArray()))
                    handler event |> Async.StartAsTask :> Task)
        let channels =
            [ 1 .. 1 ]// Think about parallelization later
            |> List.map
                (fun _ ->
                    let channel = bus.Connection.CreateModel()
                    channel.QueueDeclare(typeof<'a>.FullName, durable = true, exclusive = false, autoDelete = false) |> ignore
                    channel.QueueDeclare($"{typeof<'a>.FullName}-stream",
                                         autoDelete = false,
                                         exclusive = false,
                                         durable = true,
                                         arguments = dict [ ("x-queue-type", "stream" :> obj) ]
                                         ) |> ignore
                    channel.QueueBind(typeof<'a>.FullName, typeof<'a>.FullName, "")
                    channel.QueueBind($"{typeof<'a>.FullName}-stream", typeof<'a>.FullName, "")
                    channel.BasicQos(0u, PrefetchCount, true)
                    let consumer = AsyncEventingBasicConsumer channel
                    consumer.add_Received handlerWrapped
                    channel.BasicConsume(
                        typeof<'a>.FullName,
                        true,
                        "tag",
                        null, //([ ("x-stream-offset", 3 :> obj) ] |> dict),
                        consumer
                    )
                    |> ignore
                    channel)
        { bus with ConsumeChannels = channels }
        
    static member ReadStream<'a> handler offset (bus: Bus) =
        let handlerWrapped (channel: IModel) =
            AsyncEventHandler<BasicDeliverEventArgs>
                (fun (sender: obj) (delivery: BasicDeliverEventArgs) ->
                    let event = 
                        JsonConvert.DeserializeObject<'a>(Encoding.UTF8.GetString(delivery.Body.ToArray()))
                    async { 
                        let! _ = handler event // check for error
                        channel.BasicAck(delivery.DeliveryTag, false)
                    } |> Async.StartAsTask :> Task)
                
        let channel = bus.Connection.CreateModel()
        channel.BasicQos(0u, 1us, false)
        let consumer = AsyncEventingBasicConsumer channel
        consumer.add_Received (handlerWrapped channel)
        channel.BasicConsume(
                        $"{typeof<'a>.FullName}-stream",
                        false,
                        "tag",
                        ([ ("x-stream-offset", offset) ] |> dict),
                        consumer
                    ) |> ignore
        bus
        
    static member Publish (bus: Bus) event =
        let bytes = event |> JsonConvert.SerializeObject
                          |> Encoding.UTF8.GetBytes
        bus.PublishChannel.BasicPublish(
                exchange = event.GetType().DeclaringType.FullName,
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
    let mutable handlingCount = 0
    let handler = (fun event ->
                    // This runs one by one. Fuck, check if rebus parallize and steal!
                    printfn $"Executing %A{event} START"
                    printfn $"Executing %A{event} END"
                    async {
                        handlingCount <- handlingCount + 1
                        return Ok ()
                    })
    use bus = Bus.Connect("localhost")
              |> Bus.InitializePublisher<Events>
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
        
    handlingCount |> should equal 10


[<Fact>]
let ``RabbitMq Streams`` () =
    let mutable handlingCount = 0
    let handler = (fun event ->
                    printfn $"Executing %A{event} START"
                    printfn $"Executing %A{event} END"
                    async {
                        handlingCount <- handlingCount + 1
                        return Ok ()
                    })
    use bus = Bus.Connect("localhost")
              |> Bus.ReadStream<Events> handler 0
    
    Task.Delay(30000)
    |> Async.AwaitTask
    |> Async.RunSynchronously
        
    handlingCount |> should be (greaterThan 1)
