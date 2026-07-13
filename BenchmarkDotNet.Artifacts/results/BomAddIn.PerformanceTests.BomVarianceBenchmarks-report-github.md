```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.8655)
Intel Core Ultra 9 185H, 1 CPU, 22 logical and 16 physical cores
.NET SDK 8.0.422
  [Host]     : .NET 8.0.28 (8.0.2826.26413), X64 RyuJIT AVX2
  Job-SHDJFF : .NET 8.0.28 (8.0.2826.26413), X64 RyuJIT AVX2
  ShortRun   : .NET 8.0.28 (8.0.2826.26413), X64 RyuJIT AVX2


```
| Method           | Job        | IterationCount | LaunchCount | WarmupCount | Mean         | Error        | StdDev      | Gen0     | Gen1     | Gen2     | Allocated  |
|----------------- |----------- |--------------- |------------ |------------ |-------------:|-------------:|------------:|---------:|---------:|---------:|-----------:|
| CompareBom_100   | Job-SHDJFF | 5              | Default     | 2           |     9.846 μs |     1.190 μs |   0.3090 μs |   1.4038 |   0.0458 |        - |   17.27 KB |
| CompareBom_1000  | Job-SHDJFF | 5              | Default     | 2           |   118.588 μs |    16.826 μs |   4.3697 μs |  13.6719 |   3.2959 |        - |  168.54 KB |
| CompareBom_10000 | Job-SHDJFF | 5              | Default     | 2           | 1,664.984 μs |   128.818 μs |  33.4536 μs | 199.2188 | 199.2188 | 199.2188 | 1610.01 KB |
| CompareBom_100   | ShortRun   | 3              | 1           | 3           |    11.056 μs |     4.710 μs |   0.2582 μs |   1.4038 |   0.0458 |        - |   17.27 KB |
| CompareBom_1000  | ShortRun   | 3              | 1           | 3           |   122.709 μs |    91.953 μs |   5.0403 μs |  13.6719 |   3.2959 |        - |  168.54 KB |
| CompareBom_10000 | ShortRun   | 3              | 1           | 3           | 1,921.156 μs | 3,504.598 μs | 192.0989 μs | 199.2188 | 199.2188 | 199.2188 | 1610.01 KB |
