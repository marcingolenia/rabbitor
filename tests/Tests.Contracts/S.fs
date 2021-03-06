namespace Tests.Contracts.S

open System

type ManKilled = { Name: string }
type ManResurrected = { Name: string; When: DateTime }

type Events =
    | ManKilled of ManKilled
    | ManResurrected of ManResurrected