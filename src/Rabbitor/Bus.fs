﻿namespace Rabbitor

open System
open System.Reflection
open System.Text
open System.Threading
open System.Threading.Tasks
open Newtonsoft.Json
open RabbitMQ.Client
open RabbitMQ.Client.Events

type Bus =
    { Connection: IConnection
      PublishChannel: IModel
      ConsumeChannels: IModel list }

    interface IDisposable with
        member this.Dispose() = this.Connection.Dispose()

module Bus =

    [<Literal>]
    let StreamIdleCloseInterval = 3000
    
    let customConnect (connectionFactory: unit -> ConnectionFactory) (hosts: string list) =
        let factory = connectionFactory ()
        factory.DispatchConsumersAsync <- true
        let connection =
            factory.CreateConnection(ResizeArray<string> hosts)
        let publishingChannel = connection.CreateModel()
        { Connection = connection
          PublishChannel = publishingChannel
          ConsumeChannels = [] }

    let connect = customConnect ConnectionFactory

    let initPublisher<'a> bus =
        bus.PublishChannel.ExchangeDeclare(typeof<'a>.FullName, ExchangeType.Fanout)
        bus

    let initStreamedPublisher<'a> bus =
        let exchangeName = typeof<'a>.FullName
        let streamedQueueName = $"{typeof<'a>.FullName}-stream"
        bus.PublishChannel.ExchangeDeclare(exchangeName, ExchangeType.Fanout)
        bus.PublishChannel.QueueDeclare(
            streamedQueueName,
            autoDelete = false,
            exclusive = false,
            durable = true,
            arguments = dict [ ("x-queue-type", "stream" :> obj) ]
        )
        |> ignore
        bus.PublishChannel.QueueBind(streamedQueueName, exchangeName, "")
        bus

    let parallelSubscribeWithDeserializer<'a>
        (deserializer: string -> 'a)
        threadsCount
        (handler: 'a -> Async<Result<unit, obj>>)
        bus
        =
        let queueName =
            $"{Assembly.GetExecutingAssembly().GetName().Name}_{typeof<'a>.FullName}"
        let handlerWrapped =
            AsyncEventHandler<BasicDeliverEventArgs>(fun (sender: obj) (delivery: BasicDeliverEventArgs) ->
                let event =
                    deserializer (Encoding.UTF8.GetString(delivery.Body.ToArray()))
                handler event |> Async.StartAsTask :> Task
            )
        let channels =
            [ 1 .. threadsCount ]
            |> List.map (fun _ ->
                let channel = bus.Connection.CreateModel()
                channel.QueueDeclare(queueName, durable = true, exclusive = false, autoDelete = false)
                |> ignore
                channel.QueueBind(queueName, typeof<'a>.FullName, "")
                channel.BasicQos(0u, 1us, true)
                let consumer = AsyncEventingBasicConsumer channel
                consumer.add_Received handlerWrapped
                channel.BasicConsume(queueName, autoAck = true, consumerTag = "", arguments = null, consumer = consumer)
                |> ignore
                channel
            )
        { bus with
              ConsumeChannels = bus.ConsumeChannels @ channels }

    let parallelSubscribe<'a> =
        parallelSubscribeWithDeserializer<'a> JsonConvert.DeserializeObject<'a>

    let subscribe<'a> = parallelSubscribe<'a> 1

    let consumeStreamWithDeserializer<'a>
        (deserializer: string -> 'a)
        (handler: 'a -> Async<Result<unit, obj>>)
        (offset: int)
        bus
        =
        let handlerWrapped (channel: IModel) (totalCount: uint32) =
            AsyncEventHandler<BasicDeliverEventArgs>(fun (sender: obj) (delivery: BasicDeliverEventArgs) ->
                let event =
                    deserializer (Encoding.UTF8.GetString(delivery.Body.ToArray()))
                async {
                    match! handler event with
                    | Ok _ -> channel.BasicAck(delivery.DeliveryTag, multiple = false)
                    | Error _ -> channel.BasicNack(delivery.DeliveryTag, multiple = false, requeue = false)
                    let deliveredOffset = delivery.BasicProperties.Headers.["x-stream-offset"] :?> int64 |> uint32
                    if deliveredOffset + 1ul = totalCount then channel.Dispose()
                }
                |> Async.StartAsTask
                :> Task
            )
        let channel = bus.Connection.CreateModel()
        channel.BasicQos(0u, 1us, false)
        let consumer = AsyncEventingBasicConsumer channel
        // Consider switching to messages count - more robust and simple line 123
        let streamedQueueName = $"{typeof<'a>.FullName}-stream"
        let totalMessagesCount = channel.MessageCount streamedQueueName
        match totalMessagesCount with
        | 0u ->
            channel.Dispose()
        | _ ->
            consumer.add_Received (
                handlerWrapped
                    channel
                    totalMessagesCount
                )
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
        let exchange = event.GetType().DeclaringType.FullName
        let a = $"{exchange}+{event.GetType().Name}"
        bus.PublishChannel.BasicPublish(
            exchange = a,
            routingKey = "",
            basicProperties = null,
            body = ReadOnlyMemory bytes
        )

    let publish bus =
        serializeAndPublish JsonConvert.SerializeObject bus
