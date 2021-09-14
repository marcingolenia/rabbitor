module Learning.ThreadIssues

open System
open System.Threading
open Tests.Contracts
open Xunit
open FsUnit.Xunit
open Rabbitor

[<Fact(Skip="~")>]
let ``Checking thread safety of IModel (channel) while single-publishing messages`` () =
    async {
        let amountToPublish = 1000u
        let bus =
            Bus.connect [ "localhost" ] |> Bus.initStreamedPublisher<F.Whatever>
        let amountBeforePublish = bus.NumberOfMessagesInStream<F.Whatever> ()
        [ 1u .. amountToPublish ]
        |> List.iter (fun i ->
            ThreadPool.QueueUserWorkItem(fun _ ->
                Console.WriteLine($"Doing... {i} on Pool: {Thread.CurrentThread.IsThreadPoolThread} with Id: {Thread.CurrentThread.ManagedThreadId}")
                Bus.publish bus ({ Name = i.ToString() }: F.Whatever)
            )
            |> ignore
        )

        do! Async.Sleep 5000
        // Assert
        let actualMessageAmount = bus.NumberOfMessagesInStream<F.Whatever> ()
        actualMessageAmount |> should equal (amountBeforePublish + amountToPublish)
    // Nothing bad happens
    }

[<Fact(Skip="~")>]
let ``Checking thread safety of IModel (channel) while multi-publishing messages`` () =
    async {
        let amountToPublish = 1000u
        let bus =
            Bus.connect [ "localhost" ] |> Bus.initStreamedPublisher<F.Whatever>
        let amountBeforePublish = bus.NumberOfMessagesInStream<F.Whatever>()
        [ 1u .. 1000u ]
        |> List.iter (fun i ->
            ThreadPool.QueueUserWorkItem(fun threadContext ->
                Console.WriteLine($"Doing... {i} on Pool: {Thread.CurrentThread.IsThreadPoolThread} with Id: {Thread.CurrentThread.ManagedThreadId}")
                Bus.publishMany
                    bus
                    [ ({ Name = i.ToString() }: F.Whatever)
                      ({ Name = i.ToString() + "_2nd" }: F.Whatever) ]
            )
            |> ignore
        )
        do! Async.Sleep 5500
        // Assert
        let actualMessageAmount = bus.NumberOfMessagesInStream<F.Whatever>()
        actualMessageAmount |> should equal (amountBeforePublish +  amountToPublish * 2u)
        // Nothing bad happens
    }
    
[<Fact(Skip="~")>]
let ``Let's check again using threads - publish thread safety`` () =
    // Arrange
    let pub bus () =
        [1 .. 1000]
        |> List.iter(fun i -> Bus.publish bus ({ Name = i.ToString() }: F.Whatever2))
    async {
        let bus = Bus.connect [ "localhost" ] |> Bus.initStreamedPublisher<F.Whatever2>
        let messagesNoBeforePublish = bus.NumberOfMessagesInStream<F.Whatever2>()
        let t1 = Thread(pub bus)
        let t2 = Thread(pub bus)
        let t3 = Thread(pub bus)
        // Act
        t1.Start()
        t2.Start()
        t3.Start()
        t1.Join()
        t2.Join()
        t3.Join()
        // Assert
        do! Async.Sleep 5000
        let messagesNoAfterPublish = bus.NumberOfMessagesInStream<F.Whatever2>()
        messagesNoAfterPublish |> should equal (messagesNoBeforePublish + 3000u)
        // Nothing bad happens
    }