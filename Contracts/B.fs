namespace Contracts.B

open System

type Events =
    | ManAsked of {| Name: string; Question: string |}
    | ManAnswered of {| Name: string; Answer: string; When: DateTime |}