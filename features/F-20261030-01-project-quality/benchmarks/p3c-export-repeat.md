```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26100.9445/24H2/2024Update/HudsonValley)
Intel Core Ultra 7 265K 3.90GHz, 1 CPU, 20 logical and 20 physical cores
.NET SDK 10.0.401
  [Host]      : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  PakBaseline : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=PakBaseline  PowerPlanMode=00000000-0000-0000-0000-000000000000  Runtime=.NET 10.0
IterationCount=6  LaunchCount=1  WarmupCount=3

```
| Method         | PayloadMiB | Mean     | Error    | StdDev   | Gen0      | Gen1      | Gen2      | Allocated |
|--------------- |----------- |---------:|---------:|---------:|----------:|----------:|----------:|----------:|
| ExportBuffered | 16         | 71.16 ms | 5.233 ms | 1.866 ms | 1857.1429 | 1857.1429 | 1142.8571 |  95.15 MB |
| ExportToFile   | 16         | 68.85 ms | 4.689 ms | 1.672 ms | 1750.0000 | 1750.0000 | 1125.0000 |  79.17 MB |
