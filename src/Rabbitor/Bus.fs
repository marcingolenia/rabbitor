namespace Rabbitor

open System
open System.Reflection
open System.Text
open System.Threading.Tasks
open Newtonsoft.Json
open RabbitMQ.Client
open RabbitMQ.Client.Events

type PublicationBus =
    { Connection: IConnection
      Channel: IModel }

type ConsumptionBus =
    { Connection: IConnection
      Channels: IModel list }

type Bus =
    { Publication: PublicationBus
      Consumption: ConsumptionBus }
    interface IDisposable with
        member this.Dispose() =
            this.Publication.Connection.Dispose()
            this.Consumption.Connection.Dispose()

module Bus =
    let customConnect
        (connectionFactory: unit -> ConnectionFactory)
        (hosts: string list)
        =
        let factory = connectionFactory ()
        factory.DispatchConsumersAsync <- true
        let pubConnection =
            factory.CreateConnection(ResizeArray<string> hosts)
        { Publication =
              { Connection = pubConnection
                Channel = pubConnection.CreateModel() }
          Consumption =
              { Connection = factory.CreateConnection(ResizeArray<string> hosts)
                Channels = [] } }

    let connect = customConnect ConnectionFactory

    let initPublisher<'a> bus =
        bus.Publication.Channel.ExchangeDeclare(typeof<'a>.FullName, ExchangeType.Fanout)
        bus

    let initStreamedPublisher<'a> bus =
        let exchangeName = typeof<'a>.FullName
        let streamedQueueName = $"{typeof<'a>.FullName}-stream"
        bus.Publication.Channel.ExchangeDeclare(exchangeName, ExchangeType.Fanout)
        bus.Publication.Channel.QueueDeclare(
            streamedQueueName,
            autoDelete = false,
            exclusive = false,
            durable = true,
            arguments = dict [ ("x-queue-type", "stream" :> obj) ]
        )
        |> ignore
        bus.Publication.Channel.QueueBind(streamedQueueName, exchangeName, "")
        bus

    let parallelSubscribeWithDeserializer<'a>
        (deserializer: string -> 'a)
        threadsCount
        (handler: 'a -> Async<Result<unit, obj>>)
        bus
        =
        let queueName =
            $"{Assembly.GetExecutingAssembly().GetName().Name}_{typeof<'a>.FullName}"
        let handlerWrapped (channel: IModel) =
            AsyncEventHandler<BasicDeliverEventArgs>(fun (sender: obj) (delivery: BasicDeliverEventArgs) ->
                let event =
                    deserializer (Encoding.UTF8.GetString(delivery.Body.ToArray()))
                async {
                    try
                        match! handler event with
                        | Ok _ -> channel.BasicAck(delivery.DeliveryTag, multiple = false)
                        | Error _ ->
                            channel.BasicReject(delivery.DeliveryTag, requeue = false)
                    with
                    | _ -> channel.BasicReject(delivery.DeliveryTag, requeue = false)
                }
                |> Async.StartAsTask
                :> Task
            )
        let channels =
            [ 1 .. threadsCount ]
            |> List.map (fun _ ->
                let channel = bus.Consumption.Connection.CreateModel()
                channel.QueueDeclare(
                    queueName,
                    durable = true,
                    exclusive = false,
                    autoDelete = false
                )
                |> ignore
                channel.QueueBind(queueName, typeof<'a>.FullName, "")
                channel.BasicQos(0u, 1us, false)
                let consumer = AsyncEventingBasicConsumer channel
                consumer.add_Received (handlerWrapped channel)
                channel.BasicConsume(
                    queueName,
                    autoAck = false,
                    consumerTag = "",
                    arguments = null,
                    consumer = consumer
                )
                |> ignore
                channel
            )
        { bus with Consumption = { bus.Consumption with Channels = bus.Consumption.Channels @ channels } }

    let parallelSubscribe<'a> =
        parallelSubscribeWithDeserializer<'a> JsonConvert.DeserializeObject<'a>

    let subscribe<'a> = parallelSubscribe<'a> 1
    
    let numberOfMessagesInStream<'a> bus =
        bus.Publication.Channel.MessageCount $"{typeof<'a>.FullName}-stream"

    let consumeStreamWithDeserializer<'a>
        (deserializer: string -> 'a)
        (handler: 'a -> Async<Result<unit, obj>>)
        (offset: uint32)
        bus
        =
        let handlerWrapped (channel: IModel) (totalCount: uint32) =
            AsyncEventHandler<BasicDeliverEventArgs>(fun (sender: obj) (delivery: BasicDeliverEventArgs) ->
                let event =
                    deserializer (Encoding.UTF8.GetString(delivery.Body.ToArray()))
                async {
                    match! handler event with
                    | Ok _ -> channel.BasicAck(delivery.DeliveryTag, multiple = false)
                    | Error _ ->
                        channel.BasicNack(
                            delivery.DeliveryTag,
                            multiple = false,
                            requeue = false
                        )
                    let deliveredOffset =
                        delivery.BasicProperties.Headers.["x-stream-offset"] :?> int64
                        |> uint32
                    if deliveredOffset + 1u = totalCount then
                        channel.Close()
                        channel.Dispose()
                }
                |> Async.StartAsTask
                :> Task
            )
        let channel = bus.Consumption.Connection.CreateModel()
        channel.BasicQos(0u, 1us, false)
        let streamedQueueName = $"{typeof<'a>.FullName}-stream"
        let totalMessagesCount = channel.MessageCount streamedQueueName
        match totalMessagesCount with
        | 0u
        | _ when offset >= totalMessagesCount -> channel.Dispose()
        | _ ->
            let consumer = AsyncEventingBasicConsumer channel
            consumer.add_Received (handlerWrapped channel totalMessagesCount)
            channel.BasicConsume(
                streamedQueueName,
                autoAck = false,
                consumerTag = "",
                arguments = ([ ("x-stream-offset", offset :> obj) ] |> dict),
                consumer = consumer
            )
            |> ignore
        bus

    let consumeStream<'a> =
        consumeStreamWithDeserializer<'a> JsonConvert.DeserializeObject<'a>

    let serializeAndPublish (serialize: 'a -> string) bus event =
        let bytes =
            event |> serialize |> Encoding.UTF8.GetBytes
        bus.Publication.Channel.BasicPublish(
            exchange = typeof<'a>.FullName,
            routingKey = "",
            basicProperties = null,
            body = ReadOnlyMemory bytes
        )
    
    let serializeAndPublishMany (serialize: 'a -> string) bus events =
        let batch = bus.Publication.Channel.CreateBasicPublishBatch()
        events |> Array.iter(fun evt ->
            batch.Add(
                    exchange = typeof<'a>.FullName,
                    routingKey = "",
                    mandatory = true,
                    properties = null,
                    body = ReadOnlyMemory (serialize evt |> Encoding.UTF8.GetBytes))
            )
        batch.Publish()

    let publish bus =
        serializeAndPublish JsonConvert.SerializeObject bus 

    let publishMany bus =
        serializeAndPublishMany JsonConvert.SerializeObject bus 