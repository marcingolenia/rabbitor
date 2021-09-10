namespace Tests.Contracts.D

open System

type FrogMarried = { Name: string }
type FrogDivorced = { Name: string; When: DateTime }
type FrogKissed = { Name: string; When: DateTime }


type Events =
    | FrogMarried of FrogMarried
    | FrogDivorced of FrogDivorced
    | FrogKissed of FrogKissed