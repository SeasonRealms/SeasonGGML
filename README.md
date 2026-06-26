# SeasonGGML

`SeasonGGML` provides shared GGML native dependencies and backend enumeration helpers for
`SeasonLLM`, `SeasonImage`, `SeasonTTS`, and other SeasonEngine projects.

## Backend Enumeration

```csharp
using SeasonGGML;

var backends = GGML.GetAvailableBackends();

foreach (var backend in backends)
{
    Console.WriteLine(
        $"{backend.Name} | reg={backend.BackendRegistry} | type={backend.DeviceType} | " +
        $"free={backend.MemoryFreeBytes} | total={backend.MemoryTotalBytes}");
}
```

Each `GgmlBackendInfo` currently includes:

- `Name`
- `BackendRegistry`
- `Description`
- `DeviceId`
- `DeviceType`
- `MemoryFreeBytes`
- `MemoryTotalBytes`
- `SupportsAsync`
- `SupportsHostBuffer`
- `SupportsBufferFromHostPtr`
- `SupportsEvents`

## CPU Introspection

```csharp
var backendFeatures = GGML.GetBackendFeatures("CPU");
var systemFeatures = GGML.GetSystemCpuFeatures();
var executionInfo = GGML.GetCpuExecutionInfo();
```

- `GetBackendFeatures("CPU")`: returns the feature list exposed by the GGML CPU backend registry.
- `GetSystemCpuFeatures()`: returns the current process hardware capability view from .NET intrinsics.
- `GetCpuExecutionInfo()`: combines both sides and gives a best-effort effective top tier such as `AVX2` or `AVX512`.

For a dynamically loaded setup, the query entry point is `ggml.dll`, but the CPU feature implementation itself is provided by the CPU backend plugin such as `ggml-cpu.dll`.
