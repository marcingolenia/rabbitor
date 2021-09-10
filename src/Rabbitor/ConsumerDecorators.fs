module Rabbitor.ConsumerDecorators

open System
open System.Diagnostics
open System.Runtime.ExceptionServices

type Exception with
    member this.Reraise() =
        (ExceptionDispatchInfo.Capture this).Throw()
        Unchecked.defaultof<_>

let rec retry times next event =
    async {
        try
            let! result = next event
            match result with
            | Error _ when times > 0 -> return! retry (times - 1) next event
            | _ -> return result
        with
        | _ when times > 0 -> return! retry (times - 1) next event
        | ex -> return ex.Reraise()
    }

// Creating handler decorator is as easy as this:
let measureTime next event =
    async {
        let stopwatch = Stopwatch.StartNew()
        let! result = next event
        printfn $"Processed within {stopwatch.Elapsed}"
        return result
    }

let defaultDecors () = [ retry 3; measureTime ]

let handleWithDecors handler decors = (decors |> List.reduceBack (>>)) handler

let handleDefault handler =
    defaultDecors () |> handleWithDecors handler
