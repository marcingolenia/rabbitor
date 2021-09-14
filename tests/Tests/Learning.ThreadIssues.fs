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
        let amountToPublish = 1000
        let bus =
            Bus.connect [ "localhost" ] |> Bus.initStreamedPublisher<F.Whatever>
        let messagesAmount () =
            int
            <| bus.Publication.Channel.MessageCount(
                $"{typeof<F.Whatever>.FullName}-stream"
            )
        let amountBeforePublish = messagesAmount ()
        [ 1 .. 1000 ]
        |> List.iter (fun i ->
            ThreadPool.QueueUserWorkItem(fun _ ->
                Console.WriteLine($"Doing... {i} on Pool: {Thread.CurrentThread.IsThreadPoolThread} with Id: {Thread.CurrentThread.ManagedThreadId}")
                Bus.publish bus ({ Name = i.ToString() }: F.Whatever)
            )
            |> ignore
        )

        do! Async.Sleep 5000
        // Assert
        let actualMessageAmount = messagesAmount ()
        actualMessageAmount |> should equal (amountBeforePublish + amountToPublish)
    // Nothing bad happens
    }

[<Fact(Skip="~")>]
let ``Checking thread safety of IModel (channel) while multi-publishing messages`` () =
    async {
        let amountToPublish = 1000
        let bus =
            Bus.connect [ "localhost" ] |> Bus.initStreamedPublisher<F.Whatever>
        let checkNumberOfMessages () =
            int
            <| bus.Publication.Channel.MessageCount(
                $"{typeof<F.Whatever>.FullName}-stream"
            )
        let amountBeforePublish = checkNumberOfMessages ()
        [ 1 .. 1000 ]
        |> List.iter (fun i ->
            ThreadPool.QueueUserWorkItem(fun threadContext ->
                Console.WriteLine($"Doing... {i} on Pool: {Thread.CurrentThread.IsThreadPoolThread} with Id: {Thread.CurrentThread.ManagedThreadId}")
                Bus.publishMany
                    bus
                    [| ({ Name = i.ToString() }: F.Whatever)
                       ({ Name = i.ToString() + "_2nd" }: F.Whatever) |]
            )
            |> ignore
        )
        do! Async.Sleep 5500
        // Assert
        let actualMessageAmount = checkNumberOfMessages ()
        actualMessageAmount |> should equal (amountBeforePublish + amountToPublish * 2)
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
        let checkNumberOfMessages () =
            int
            <| bus.Publication.Channel.MessageCount(
                $"{typeof<F.Whatever2>.FullName}-stream")
        let messagesNoBeforePublish = checkNumberOfMessages()
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
        let messagesNoAfterPublish = checkNumberOfMessages()
        messagesNoAfterPublish |> should equal (messagesNoBeforePublish + 3000)
        // Nothing bad happens
    }