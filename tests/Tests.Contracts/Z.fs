namespace Service.A.Contracts

open System

type ManKilled = { Name: string }
type ManResurrected = { Name: string; When: DateTime }

type CrimeNotifications =
    | ManKilled of ManKilled
    | ManResurrected of ManResurrected