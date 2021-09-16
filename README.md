[![Build & Test](https://github.com/marcingolenia/rabbitor/actions/workflows/dotnet.yml/badge.svg)](https://github.com/marcingolenia/rabbitor/actions/workflows/dotnet.yml)
![F#](https://img.shields.io/badge/Made%20with-F%23-blue)

# Rabbitor 🐇

<p align="center">
<img src="https://github.com/marcingolenia/rabbitor/raw/main/logo.png" width="150px"/>
</p>
The F# friendly (I hope so) RabbitMQ Client. The goal of this library is to enable quick asynchronous communication with a low-configuration approach.
The library focuses (at the moment) on RabbitMQ. If You need more You should check awesome https://github.com/pchalamet/fbus.

### Contents
- [Features](#Features)
- [Coming next](#Coming-next)
- [Recipes](#Recipes)
   1. Quickstart: async communication between services
   2. Increase throughput through parallel consumers
   3. Stream consumption (useful when adding new service or whenever You need to synchronize)
   4. Extending the consumer capabilities using decorators
- [Explanations](#Explanations)
   1. Serialization/Deserialization of messages
   2. Queues and Exchanges topology convention 
   3. Connection to RabbitMQ

# Features 🤹
- Publish-Subscribe messaging
- Automatic creation of queues & exchanges
- Rabbit Streams Support
- Parallel Consumers
- Single-threaded consumers for in-order processing
- Easy decorators for rich message consumptions
# Coming next 🔮
Next features will depend on the author's whims unless related issues on GH will get some votes.
- Dead-Letter exchange support for failures
- Streams retention
- More examples 
- In-memory transport
- Possibility to pass in custom conventions for queues/exchanges topology
- Kafka equivalent 

# Recipes 📑
Recipes will help You get started and solve some common problems. There is also a minimal example in the source code with Giraffe if You can't wait.

## 1. Quickstart: async communication between services
Note: Nothing stops You from using Rabbitor for asynchronous communication within a single service (for instance for inter-bounded contexts communication). The configuration will be almost the same. 

*Make sure that You have access to running RabbitMQ instance, for local development You can just take `docker-compose.yml` from this repository and run `docker-compose up -d`*

Let's assume we have services A and B. You need to establish asynchronous communication between these two services. You have decided to use Rabbitor together with RabbitMQ. Let's start with the definition of the messages that can be emitted from service A.

```fsharp
namespace Service.A.Contracts

open System

type ManKilled = { Name: string }
type ManResurrected = { Name: string; When: DateTime }

type CrimeNotifications =
    | ManKilled of ManKilled
    | ManResurrected of ManResurrected
```

The same types (including namespace) should be placed in Service B. You can copy the file or publish a shared nuget package (I prefer copy). See [2. Queues and Exchanges topology convention](#Ex) to learn why it is important.

Start the bus near Your application entry-point and init the publisher.

```fsharp
let bus = Bus.connect ["localhost"]
          |> Bus.initPublisher<CrimeNotifications>
```

If service A is only publishing messages, You are ready. How to manage the bus dependency is Your concern. You can register it as a single instance if You use dependency injection or just pass it to composition root to include it as a dependency. There is also a simple example in the repository, where the bus is passed down to the Giraffe HttpHandler. Publishing messages is as easy as:
```fsharp
let notification = CrimeNotifications.ManKilled { Name = "Stasiek" }
notification |> Bus.publish bus
```
note that You can use partial application to associate bus instance with `Bus.publish` function to reduce usage complexity.

Time to configure consumer in service B. We need to define a handler that will process received message and again configure the Bus instance:

```fsharp
let handler =
    (fun event ->
        async {
            printfn $"Received: %A{event}"
            return Ok()
        })
```
a handler is any function with signature `'a -> Async<Result<unit,'b>>`. Let's configure bus:
```fsharp
let bus = Bus.connect ["localhost"]
          |> Bus.subscribe<CrimeNotifications> handler
```
That is all! Bus handler will process received notification from now on.

*You can also play with related tests from the source code: Tests -> PubSub.fs*

## 2. Increase throughput through parallel consumers
By default the consumer (single subscription) works on single thread related with channel. This guarantees ordered processing of messages. If You don't need order guarantee (I hope You don't) You can enable parallel consumer processing. 
Let's assume You did everything in step 1, now to turn parallel processing let's update the consumer bus setup:

```fsharp
let bus = Bus.connect ["localhost"]
            |> Bus.parallelSubscribe<CrimeNotifications> 5 handler
```
Alternatively You can subscribe multiple times;
```fsharp
let bus = Bus.connect ["localhost"]
            |> Bus.subscribe<CrimeNotifications> handler
            |> Bus.subscribe<CrimeNotifications> handler
            |> Bus.subscribe<CrimeNotifications> handler
            |> Bus.subscribe<CrimeNotifications> handler
            |> Bus.subscribe<CrimeNotifications> handler
```
This gives You the flexibility to consume different Events in number of ways:

```fsharp
let bus = Bus.connect ["localhost"]
            |> Bus.parallelSubscribe<WhateverNotifications> 10 handler1
            |> Bus.subscribe<CrimeNotifications> handler2
            |> Bus.parallelSubscribe<OtherNotifications> 5 handler3
```
It will be beneficial to observe the application behaviour and adjust the parallelism threshold.

*You can also play with related tests from the source code: Tests -> ParallelSubscriptions.fs*

## 3. Stream consumption
Streams consumption may be handy when adding new service and You need to synchronise the state between those services. They can also be great when facing failures - You can subscribe to stream and reply the events (assuming that Your receiver is idempotent). 

Rabbitor makes it easy to consume the stream, it is similar to subscriptions and looks like this:
```fsharp
Bus.consumeStream<CrimeNotifications> handler offset bus |> ignore
```
handler will process the stream messages one by one in the background on a thread related to a dedicated channel. Rabbitor knows how many messages were in the stream upon requesting the stream, so it will consume all of them and skip messages which were appended to the stream in the meantime. 

*You can also play with related tests from the source code: Tests -> Streams.fs*

## 4. Extending the consumer capabilities using decorators
Since the handler is any function with type `'a -> Async<Result<unit,'b>>` they can be easily composed. Rabbitor comes with one decorator for retries on exception or errors cases. It is not applied by default but it can be easily added upon subscribing as follows:

```fsharp
let bus = Bus.connect ["localhost"]
            |> Bus.subscribe<CrimeNotifications> (handler |> decorate [ ConsumerDecorators.retry 4 ])
```
building a decorator is as easy as this:
```fsharp
let measureTimeDecorator next event =
    async {
        let stopwatch = Stopwatch.StartNew()
        let! result = next event // call next decorator / handler
        printfn $"Processed within {stopwatch.Elapsed}"
        return result
    }
```
now it is enough to add it to the decorators list and pass it to decorate function (or compose it by yourself). You can pass as many decorators as You want, the execution order conforms to the order in the array, so:
```fsharp
...
|>  Bus.subscribe<F.Whatever7> (handler |> decorate [ decorator1; decorator2])
```
means that the execution order is as follows;
1. decorator1
2. decorator2 
3. handler
4. decorator2
5. decorator1

The first use cases for decorator You may need (besides retry) are audit, metrics.

*You can also play with related tests from the source code: Tests -> CustomDecorators.fs*

# Explanations 👓
Few words about Rabbitor internals.
## 1. Serialization/Deserialization of messages
Rabbitor by default uses Newtonsoft.Json for serialization/deserialization. You can easily plugin Your own by using following Bus functions;
```fsharp
use bus =
    Bus.connect [ "localhost" ]
    |> Bus.initPublisher<CrimeNotifications>
    |> Bus.parallelSubscribeWithDeserializer<CrimeNotifications> myDeserialize 1 handler

// ... And later for publishing:

Bus.serializeAndPublish bus mySerialize event
```
where
1. `mySerialize` is a function `'a -> string `
2. `myDeserialize` is a function `string -> 'a`

Again, consider using partial application to simplify usage later on.

*You can also play with related tests from the source code: Tests -> CustomJsonSerializer.fs*

## 2. Queues and Exchanges topology convention
To make the topology not Your concern, Rabbitor uses following convention to setup queues and exchanges for You:
1. Exchanges are created upon `Bus.initPublisher<'a>` using the full type name of `<'a>`.
2. Stream Queues are created upon `Bus.initStreamedPublisher<'a>` using the full type name of `<'a>`.
3. Plain queues are created upon `Bus.subscribe<'a>` or parallel equivalent using the executing assembly name and full type name of `<'a>`, which allow multiple subscriptions per topic. 

At the moment it is not possible to override the convention. 

*I am hinking about removing the exchange creation, so the initPublisher function. When no queue exists, messages won't be published, that is why it the exchange can be created upon queue creation.*

## 3. Connection to RabbitMQ
Rabbitor uses official RabbitMQ .net client underneath and tries to not get into Your way to much. You can pass custom connection factory if You want using `Bus.customConnect` function 
`(unit -> ConnectionFactory) -> string list -> Bus`
where [ConnectionFactory is .net RabbitMQ library type](https://www.rabbitmq.com/dotnet-api-guide.html#connecting). Here You can setup password, use configure certificates, override connection recovery settings. Rabbitor uses defaults. 

Rabbitor uses separate connections for publishing and consuming because:
> Separate the connections for publishers and consumers to achieve high throughput. RabbitMQ can apply back pressure on the TCP connection when the publisher is sending too many messages for the server to handle -> [[source]](https://www.cloudamqp.com/blog/part1-rabbitmq-best-practice.html)

# End note
PRs are welcome 🤗