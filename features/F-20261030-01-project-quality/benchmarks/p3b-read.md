```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26100.9445/24H2/2024Update/HudsonValley)
Intel Core Ultra 7 265K 3.90GHz, 1 CPU, 20 logical and 20 physical cores
.NET SDK 10.0.401
  [Host]      : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  PakBaseline : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=PakBaseline  PowerPlanMode=00000000-0000-0000-0000-000000000000  Runtime=.NET 10.0
IterationCount=6  LaunchCount=1  WarmupCount=3

```
| Method           | PayloadMiB | Mean       | Error     | StdDev    | Gen0      | Gen1      | Gen2      | Allocated |
|----------------- |----------- |-----------:|----------:|----------:|----------:|----------:|----------:|----------:|
| **ReadOneVerified**  | **16**         |   **9.190 ms** |  **1.751 ms** | **0.6244 ms** |  **296.8750** |  **296.8750** |  **296.8750** |  **16.09 MB** |
| ReadFourVerified | 16         |  49.743 ms |  6.996 ms | 2.4947 ms | 2000.0000 | 2000.0000 | 1500.0000 |  80.07 MB |
| **ReadOneVerified**  | **64**         |  **37.887 ms** |  **2.399 ms** | **0.8555 ms** |  **153.8462** |  **153.8462** |  **153.8462** |  **64.09 MB** |
| ReadFourVerified | 64         | 184.961 ms | 16.410 ms | 5.8520 ms | 3000.0000 | 3000.0000 | 1333.3333 | 320.11 MB |
