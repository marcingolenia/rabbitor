open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open Giraffe
open Rabbitor
open System

type CompanyCreated = {
    Name: string
    On: DateTime
}
type Events =
    | CompanyCreated of {| Name: string; On: DateTime |}
    
let handler prefix = (fun event ->
                async {
                    printfn $"%s{prefix} | Received: %A{event} || at %s{DateTime.Now.ToShortTimeString()}"
                    do! Async.Sleep 500
                    return Ok ()
                })
    
let pub (bus: Bus) : HttpHandler =
    let evt = Events.CompanyCreated {| Name = Guid.NewGuid().ToString(); On = DateTime.Now |}
    evt |> Bus.publish bus
    text $"published: %A{evt}"
    
let read offset (bus: Bus) : HttpHandler =
    Bus.consumeStream<Events> (handler "Stream Handling") offset bus |> ignore
    text "Will consume."

let webApp (bus: Bus) =
    choose [ route "/pub-random" >=> warbler (fun _ -> pub bus)
             routef "/stream-console/%i" (fun (offset: int) ->  read (uint32 offset) bus)
             route "/" >=> text "Hello from Consumer3!" ]

let configureApp (app: IApplicationBuilder) =
    let bus = Bus.connect ["localhost"]
              |> Bus.initStreamedPublisher<Events>
              |> Bus.subscribe<Events> (handler "Subscription Handling")
    app.UseGiraffe (webApp bus)

let configureServices (services: IServiceCollection) = services.AddGiraffe() |> ignore

[<EntryPoint>]
let main _ =
    Host
        .CreateDefaultBuilder()
        .ConfigureLogging(fun logging -> logging.AddConsole() |> ignore)
        .ConfigureWebHostDefaults(fun webHostBuilder ->
            webHostBuilder
                .Configure(configureApp)
                .ConfigureServices(configureServices)
            |> ignore)
        .Build()
        .Run()
    0
