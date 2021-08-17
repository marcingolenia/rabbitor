module Tests

open System
open System.Reflection
open System.Text
open Newtonsoft.Json
open RabbitMQ.Client
open RabbitMQ.Client.Events
open Xunit
open System.Threading.Tasks
open FsUnit.Xunit
open Contracts

// docker run --name rabbitor -d -p 15672:15672 -p 5672:5672 rabbitmq:3.9.1-management-alpine
// docker exec rabbitor rabbitmq-plugins list
// docker exec rabbitor rabbitmq-plugins enable rabbitmq_stream
//    let registeredEvents = FSharpType.GetUnionCases(typeof<'a>)
//                           |> Seq.map (fun case -> (case.GetFields().[0].PropertyType, case))
//                           |> Seq.toList

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
        let exchangeName = typeof<'a>.FullName
        bus.PublishChannel.ExchangeDeclare(exchangeName, ExchangeType.Fanout)
        bus.PublishChannel.QueueDeclare($"{typeof<'a>.FullName}-stream",
                                        autoDelete = false,
                                        exclusive = false,
                                        durable = true,
                                        arguments = dict [ ("x-queue-type", "stream" :> obj) ]
                                        ) |> ignore
        bus.PublishChannel.QueueBind($"{typeof<'a>.FullName}-stream", exchangeName, "")
        bus

    static member Subscribe<'a> handler bus =
        let queueName = $"{Assembly.GetExecutingAssembly().GetName().Name}_{typeof<'a>.FullName}"
        let handlerWrapped =
            AsyncEventHandler<BasicDeliverEventArgs>
                (fun (sender: obj) (delivery: BasicDeliverEventArgs) ->
                    let event =
                        JsonConvert.DeserializeObject<'a>(Encoding.UTF8.GetString(delivery.Body.ToArray()))
                    handler event |> Async.StartAsTask :> Task)
        let channels =
            [ 1 .. 1 ]// Within channel events are handled one by one. Rebus does parallelization per event type.
            |> List.map
                (fun _ ->
                    let channel = bus.Connection.CreateModel()
                    channel.QueueDeclare(queueName,
                                         durable = true,
                                         exclusive = false,
                                         autoDelete = false) |> ignore
                    channel.QueueBind(queueName, typeof<'a>.FullName, "")
                    channel.BasicQos(0u, PrefetchCount, true)
                    let consumer = AsyncEventingBasicConsumer channel
                    consumer.add_Received handlerWrapped
                    channel.BasicConsume(
                        queueName,
                        true,
                        "",
                        null,
                        consumer
                    )
                    |> ignore
                    channel)
        { bus with ConsumeChannels = bus.ConsumeChannels @ channels }
        
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

[<Fact>]
let ``RabbitMq PubSub`` () =
    let mutable handlingCount = 0
    let handler = (fun event ->
                    printfn $"Executing %A{event} START"
                    printfn $"Executing %A{event} END"
                    async {
                        handlingCount <- handlingCount + 1
                        return Ok ()
                    })
    
    use bus = Bus.Connect("localhost")
              |> Bus.InitializePublisher<A.Events> // Can publish events for different bounded contexts
              |> Bus.InitializePublisher<B.Events>
              |> Bus.Subscribe<A.Events> handler // Can handle events from different bounded contexts
              |> Bus.Subscribe<B.Events> handler

    Bus.Publish bus (B.ManAsked {| Name = "Marcin"; Question = "Skąd kk?" |})
    Bus.Publish bus (B.ManAnswered {| Name = "Marcin"; Answer = "Skąd kk?"; When = DateTime.UtcNow |})
    [ 1 .. 3 ] |> List.iter(fun i -> Bus.Publish bus (A.ManKilled { Name = $"Marcinek %i{i}" }))
    
    Task.Delay(5000)
        |> Async.AwaitTask
        |> Async.RunSynchronously
        
    handlingCount |> should equal 5


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
              |> Bus.ReadStream<A.Events> handler 0
    
    Task.Delay(5000)
    |> Async.AwaitTask
    |> Async.RunSynchronously
        
    handlingCount |> should be (greaterThan 1)
