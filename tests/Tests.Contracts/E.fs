namespace Tests.Contracts.E

open System

type CoderHired = { Name: string }
type CoderFired = { Name: string; When: DateTime }
type CoderDied = { Name: string; When: DateTime }


type Events =
    | CoderHired of CoderHired
    | CoderFired of CoderFired
    | CoderDied of CoderDied