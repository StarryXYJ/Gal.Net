```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26100.9445/24H2/2024Update/HudsonValley)
Intel Core Ultra 7 265K 3.90GHz, 1 CPU, 20 logical and 20 physical cores
.NET SDK 10.0.401
  [Host]         : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  EditorBaseline : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=EditorBaseline  PowerPlanMode=00000000-0000-0000-0000-000000000000  Runtime=.NET 10.0
IterationCount=6  LaunchCount=1  WarmupCount=3

```
| Method            | EntryCount | Mean       | Error     | StdDev   | Allocated |
|------------------ |----------- |-----------:|----------:|---------:|----------:|
| **DirtyClean**        | **1000**       |   **208.5 μs** |   **3.55 μs** |  **1.27 μs** |     **760 B** |
| DirtyLastEntry    | 1000       |   174.6 μs |   1.93 μs |  0.69 μs |     704 B |
| DirtyLastTextByte | 1000       |   206.8 μs |   3.89 μs |  1.39 μs |     760 B |
| **DirtyClean**        | **10000**      | **2,727.7 μs** |  **86.48 μs** | **22.46 μs** |     **760 B** |
| DirtyLastEntry    | 10000      | 1,875.6 μs | 226.38 μs | 80.73 μs |     704 B |
| DirtyLastTextByte | 10000      | 2,735.9 μs | 298.62 μs | 77.55 μs |     760 B |
