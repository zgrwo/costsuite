```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.8655)
Intel Core Ultra 9 185H, 1 CPU, 22 logical and 16 physical cores
.NET SDK 8.0.422
  [Host]     : .NET 8.0.28 (8.0.2826.26413), X64 RyuJIT AVX2
  Job-KHBMUD : .NET 8.0.28 (8.0.2826.26413), X64 RyuJIT AVX2
  ShortRun   : .NET 8.0.28 (8.0.2826.26413), X64 RyuJIT AVX2


```
| Method   | Job        | IterationCount | LaunchCount | WarmupCount | Count | Mean      | Error      | StdDev   | Gen0    | Gen1   | Allocated |
|--------- |----------- |--------------- |------------ |------------ |------ |----------:|-----------:|---------:|--------:|-------:|----------:|
| **Evaluate** | **Job-KHBMUD** | **5**              | **Default**     | **2**           | **100**   |  **16.37 μs** |   **1.688 μs** | **0.261 μs** |  **1.6479** | **0.0610** |  **20.41 KB** |
| Evaluate | ShortRun   | 3              | 1           | 3           | 100   |  16.99 μs |   8.284 μs | 0.454 μs |  1.6479 | 0.0610 |  20.41 KB |
| **Evaluate** | **Job-KHBMUD** | **5**              | **Default**     | **2**           | **1000**  | **213.65 μs** |  **12.051 μs** | **3.130 μs** | **19.2871** | **6.5918** | **237.21 KB** |
| Evaluate | ShortRun   | 3              | 1           | 3           | 1000  | 213.08 μs | 129.316 μs | 7.088 μs | 19.2871 | 6.5918 | 237.21 KB |
