// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonGGML

namespace SeasonGGML;

public enum GgmlBackendDeviceType
{
    Cpu = 0,
    Gpu = 1,
    IntegratedGpu = 2,
    Accelerator = 3,
    Meta = 4
}

public readonly record struct GgmlBackendInfo(
    string Name,
    string BackendRegistry,
    string Description,
    string? DeviceId,
    GgmlBackendDeviceType DeviceType,
    ulong MemoryFreeBytes,
    ulong MemoryTotalBytes,
    bool SupportsAsync,
    bool SupportsHostBuffer,
    bool SupportsBufferFromHostPtr,
    bool SupportsEvents);

public readonly record struct GgmlBackendFeature(
    string Name,
    string Value);

public readonly record struct SystemCpuFeatureInfo(
    Architecture Architecture,
    bool Sse3,
    bool Ssse3,
    bool Avx,
    bool AvxVnni,
    bool Avx2,
    bool F16c,
    bool Fma,
    bool Bmi2,
    bool Avx512F,
    bool Avx512Vbmi,
    bool Avx512Vnni,
    bool Avx512Bf16,
    bool AmxInt8,
    bool Neon,
    bool ArmFma,
    bool Fp16VectorArithmetic,
    bool MatmulInt8,
    bool Sve,
    int SveCount,
    bool DotProduct,
    bool Sme,
    string HighestTier);

public readonly record struct GgmlCpuExecutionInfo(
    GgmlBackendFeature[] BackendFeatures,
    SystemCpuFeatureInfo SystemFeatures,
    string CompiledTopTier,
    string SystemTopTier,
    string EffectiveTopTier,
    string Notes);

public sealed class GgmlBackendSelection : IDisposable
{
    private readonly IntPtr[] _devices;
    private GCHandle _handle;
    private bool _disposed;

    internal GgmlBackendSelection(IntPtr[] devices, string[] names)
    {
        _devices = devices;
        Names = names;
        _handle = GCHandle.Alloc(_devices, GCHandleType.Pinned);
    }

    public IntPtr Pointer => _handle.AddrOfPinnedObject();
    public IReadOnlyList<string> Names { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_handle.IsAllocated)
        {
            _handle.Free();
        }
    }
}
