```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26100.9445/24H2/2024Update/HudsonValley)
Intel Core Ultra 7 265K 3.90GHz, 1 CPU, 20 logical and 20 physical cores
.NET SDK 10.0.401
  [Host]         : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  EditorBaseline : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=EditorBaseline  PowerPlanMode=00000000-0000-0000-0000-000000000000  Runtime=.NET 10.0
IterationCount=6  LaunchCount=1  WarmupCount=3

```
| Method                 | InvocationCount | UnrollFactor | EntryCount | Mean        | Error       | StdDev      | Gen0     | Gen1     | Gen2     | Allocated |
|----------------------- |---------------- |------------- |----------- |------------:|------------:|------------:|---------:|---------:|---------:|----------:|
| **RecoveryThroughSession** | **Default**         | **16**           | **1000**       |  **8,278.0 μs** |   **509.75 μs** |   **181.78 μs** | **468.7500** | **453.1250** | **375.0000** |   **7.77 MB** |
| RecoverySchedule       | 1               | 1            | 1000       |    595.6 μs |    41.61 μs |    14.84 μs |        - |        - |        - |   1.41 MB |
| **RecoveryThroughSession** | **Default**         | **16**           | **10000**      | **45,578.8 μs** | **4,022.37 μs** | **1,434.42 μs** | **666.6667** | **583.3333** |  **83.3333** |  **63.05 MB** |
| RecoverySchedule       | 1               | 1            | 10000      |  2,720.4 μs |   309.27 μs |   110.29 μs |        - |        - |        - |  14.05 MB |
