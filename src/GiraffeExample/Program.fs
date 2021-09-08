open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
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
    
let handler = (fun event ->
                async {
                    printfn $"Received: %A{event} || at %s{DateTime.Now.ToShortTimeString()}"
                    do! Async.Sleep 1000
                    return Ok ()
                })    
    
let pub (bus: Bus) : HttpHandler =
    let evt = Events.CompanyCreated {| Name = Guid.NewGuid().ToString(); On = DateTime.Now |}
    evt |> Bus.publish bus
    text $"published: %A{evt}"
    
let read (bus: Bus) : HttpHandler =
    Bus.consumeStream<Events> handler 3 bus |> ignore
    text "Will consume."

let webApp (bus: Bus) =
    choose [ route "/pub-random" >=> warbler (fun _ -> pub bus)
             route "/stream-console" >=> warbler (fun _ -> read bus)
             route "/" >=> text "Hello from Consumer3!" ]

let configureApp (app: IApplicationBuilder) =
    let bus = Bus.connect ["localhost"]
              |> Bus.initStreamedPublisher<Events>
    app.UseGiraffe (webApp bus)

let configureServices (services: IServiceCollection) = services.AddGiraffe() |> ignore

[<EntryPoint>]
let main _ =
    Host
        .CreateDefaultBuilder()
        .ConfigureWebHostDefaults(fun webHostBuilder ->
            webHostBuilder
                .Configure(configureApp)
                .ConfigureServices(configureServices)
            |> ignore)
        .Build()
        .Run()
    0
