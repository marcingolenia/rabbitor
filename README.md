[![Build & Test](https://github.com/marcingolenia/rabbitor/actions/workflows/dotnet.yml/badge.svg)](https://github.com/marcingolenia/rabbitor/actions/workflows/dotnet.yml)
![F#](https://img.shields.io/badge/Made%20with-F%23-blue)

# Rabbitor

<p align="center">
<img src="https://github.com/marcingolenia/rabbitor/raw/main/logo.png" width="150px"/>
</p>
The opinionated F# friendly (I hope so) RabbitMQ Client. The goal of this library is to enable quick asynchronous communication with a low-configuration approach.
The library focuses (at the moment) on RabbitMQ. If You need more You should check awesome https://github.com/pchalamet/fbus.

### Contents
- [Features](#Features)
- [Coming next](#Coming-next)
- [Recipes](#Recipes)
   1. [Quickstart: async communication between services](#1.-Quickstart:-async-communication-between-services)
   2. [Increase throughput through parallel consumers](#2.-Increase-throughput-through-parallel-consumers)
   3. [Stream consumption (useful when adding new service or whenever You need to synchronise)](#3.-Stream-consumption)
   4. [Extending the consumer capabilities using decorators](#4.-Extending-the-consumer-capabilities-using-decorators)
- [Explanations](#Explanations)
   1. [Serialization/Deserialization of messages](#1.-Serialization/Deserialization-of-messages)
   2. [Queues and Exchanges topology convention](#2.-Queues-and-Exchanges-topology-convention) 
   3. [Connection to RabbitMQ](#3.-Connection-to-RabbitMQ)

# Features
- Publish-Subscribe messaging
- Automatic creation of queues & exchanges
- Rabbit Streams Support
- Parallel Consumers
- Single-threaded consumers for in-order processing
- Easy decorators for rich message consumptions
# Coming next
Next features will depend on the author's whims unless related issues on GH will get some votes.
- Dead-Letter exchange support for failures
- Streams retention
- More examples 
- In-memory transport
- Possibility to pass in custom conventions for queues/exchanges topology
- Kafka equivalent 

# Recipes
Recipes will help You get started and solve some common problems. 

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
note that You can use partial application to associate bus intance with `Bus.publish` function to reduce usage complexity.

Time to configure consumer in service B. We need to define a handler that will process received message and again configure the Bus instance:

```fsharp
let handler =
    (fun event ->
        async {
            printfn $" | Received: %A{event}"
            return Ok()
        })
```
a handler is any function with signature `'a -> Async<Result<unit,'b>>`. Let's configure bus:
```fsharp
let bus = Bus.connect ["localhost"]
          |> Bus.subscribe<CrimeNotifications> handler
```
That is all! Bus handler will process received notification from now on.

## 2. Increase throughput through parallel consumers
todo
## 3. Stream consumption
todo
## 4. Extending the consumer capabilities using decorators
todo

# Explanations
## 1. Serialization/Deserialization of messages
todo
## 2. Queues and Exchanges topology convention
todo
## 3. Connection to RabbitMQ
todo