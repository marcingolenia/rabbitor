module RabbitStreams.Consumer

let registerHandler
      (handler: 'a -> Async<Unit>) = ()
      //(activator: BuiltinHandlerActivator) =
      //  activator.Handle<'a>(fun message -> handler message |> toTask)