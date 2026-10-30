```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26100.9445/24H2/2024Update/HudsonValley)
Intel Core Ultra 7 265K 3.90GHz, 1 CPU, 20 logical and 20 physical cores
.NET SDK 10.0.401
  [Host]      : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  PakBaseline : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=PakBaseline  PowerPlanMode=00000000-0000-0000-0000-000000000000  Runtime=.NET 10.0
IterationCount=6  LaunchCount=1  WarmupCount=3

```
| Method         | PayloadMiB | Mean      | Error    | StdDev    | Gen0      | Gen1      | Gen2      | Allocated |
|--------------- |----------- |----------:|---------:|----------:|----------:|----------:|----------:|----------:|
| **ExportBuffered** | **16**         |  **83.30 ms** | **17.91 ms** |  **6.388 ms** | **3285.7143** | **3285.7143** | **1142.8571** |  **95.14 MB** |
| ExportToFile   | 16         |  78.81 ms | 15.12 ms |  5.394 ms | 2428.5714 | 2428.5714 | 1000.0000 |  79.12 MB |
| **ExportBuffered** | **64**         | **238.45 ms** | **30.77 ms** | **10.974 ms** | **2333.3333** | **2333.3333** |  **666.6667** | **383.16 MB** |
| ExportToFile   | 64         | 251.60 ms | 10.77 ms |  3.840 ms | 3500.0000 | 3500.0000 | 1000.0000 | 319.14 MB |
