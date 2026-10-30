```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26100.9445/24H2/2024Update/HudsonValley)
Intel Core Ultra 7 265K 3.90GHz, 1 CPU, 20 logical and 20 physical cores
.NET SDK 10.0.401
  [Host]         : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  EditorBaseline : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=EditorBaseline  PowerPlanMode=00000000-0000-0000-0000-000000000000  Runtime=.NET 10.0
InvocationCount=1  IterationCount=6  LaunchCount=1
UnrollFactor=1  WarmupCount=3

```
| Method           | EntryCount | Mean      | Error      | StdDev    | Allocated |
|----------------- |----------- |----------:|-----------:|----------:|----------:|
| **RecoverySchedule** | **1000**       |  **9.037 ms** |  **0.4392 ms** | **0.1566 ms** |  **13.32 MB** |
| **RecoverySchedule** | **10000**      | **41.082 ms** | **11.3672 ms** | **2.9520 ms** | **117.03 MB** |
