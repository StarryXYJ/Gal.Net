```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26100.9445/24H2/2024Update/HudsonValley)
Intel Core Ultra 7 265K 3.90GHz, 1 CPU, 20 logical and 20 physical cores
.NET SDK 10.0.401
  [Host]         : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  EditorBaseline : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=EditorBaseline  PowerPlanMode=00000000-0000-0000-0000-000000000000  Runtime=.NET 10.0
IterationCount=6  LaunchCount=1  WarmupCount=3

```
| Method                 | EntryCount | Mean        | Error       | StdDev    | Gen0     | Gen1     | Gen2     | Allocated   |
|----------------------- |----------- |------------:|------------:|----------:|---------:|---------:|---------:|------------:|
| **DirtyClean**             | **1000**       |    **226.0 μs** |     **4.13 μs** |   **1.47 μs** |   **3.9063** |        **-** |        **-** |    **63.24 KB** |
| DirtyLastEntry         | 1000       |    194.4 μs |     1.07 μs |   0.38 μs |   3.9063 |        - |        - |    63.19 KB |
| DirtyLastTextByte      | 1000       |    226.9 μs |     4.16 μs |   1.48 μs |   3.9063 |        - |        - |    63.24 KB |
| CaptureWorking         | 1000       |  2,116.7 μs |    23.91 μs |   8.53 μs | 160.1563 | 140.6250 | 132.8125 |  1446.77 KB |
| SerializeSnapshot      | 1000       |    744.2 μs |    38.07 μs |  13.58 μs | 119.1406 | 118.1641 | 118.1641 |  2109.64 KB |
| RecoveryUnchanged      | 1000       |  1,173.1 μs |    75.46 μs |  19.60 μs | 130.8594 | 130.8594 | 130.8594 |  2236.39 KB |
| RecoveryChanged        | 1000       |  5,828.3 μs |   290.30 μs |  75.39 μs | 406.2500 | 406.2500 | 406.2500 |  7405.26 KB |
| RecoveryThroughSession | 1000       |  5,853.4 μs |   373.13 μs | 133.06 μs | 437.5000 | 406.2500 | 406.2500 |  8755.48 KB |
| **DirtyClean**             | **10000**      |  **2,815.2 μs** |    **76.38 μs** |  **27.24 μs** |  **39.0625** |        **-** |        **-** |   **625.74 KB** |
| DirtyLastEntry         | 10000      |  1,991.5 μs |    38.39 μs |  13.69 μs |  39.0625 |        - |        - |   625.69 KB |
| DirtyLastTextByte      | 10000      |  2,770.9 μs |    55.15 μs |  14.32 μs |  39.0625 |        - |        - |   625.74 KB |
| CaptureWorking         | 10000      |  5,606.7 μs |   222.97 μs |  57.90 μs | 414.0625 | 351.5625 | 132.8125 | 14384.31 KB |
| SerializeSnapshot      | 10000      |  8,902.8 μs |   593.98 μs | 154.26 μs |  46.8750 |  46.8750 |  46.8750 | 21015.85 KB |
| RecoveryUnchanged      | 10000      | 13,426.6 μs | 1,113.06 μs | 289.06 μs |  93.7500 |  93.7500 |  93.7500 | 24279.69 KB |
| RecoveryChanged        | 10000      | 36,218.6 μs | 1,995.30 μs | 518.17 μs | 285.7143 | 285.7143 | 285.7143 | 60997.33 KB |
| RecoveryThroughSession | 10000      | 42,528.0 μs | 1,513.00 μs | 392.92 μs | 384.6154 | 307.6923 | 153.8462 | 69440.21 KB |
