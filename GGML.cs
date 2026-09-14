// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonGGML

namespace Season.GGML;

public static class GGML
{
    private static readonly object s_loadLock = new();
    private static readonly object s_resolverLock = new();
    private static readonly Dictionary<string, IntPtr> s_nativeHandles = new(StringComparer.Ordinal);
    private static bool s_backendsLoaded;
    private static bool s_resolverRegistered;

    public static bool IsSupported =>
        OperatingSystem.IsWindows() ||
        (OperatingSystem.IsMacCatalyst() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64);

    public static GgmlBackendInfo[] GetAvailableBackends()
    {
        EnsureSupported();
        EnsureBackendsLoaded();

        var count = checked((int)GgmlNative.ggml_backend_dev_count());
        if (count <= 0)
        {
            return [];
        }

        var backends = new GgmlBackendInfo[count];
        for (var i = 0; i < count; i++)
        {
            var device = GgmlNative.ggml_backend_dev_get((nuint)i);
            if (device == IntPtr.Zero)
            {
                throw new InvalidOperationException($"ggml_backend_dev_get returned null for device index {i}.");
            }

            GgmlNative.ggml_backend_dev_get_props(device, out var props);
            var reg = GgmlNative.ggml_backend_dev_backend_reg(device);

            backends[i] = new GgmlBackendInfo(
                Name: GgmlNative.PtrToString(props.Name),
                BackendRegistry: GgmlNative.PtrToString(GgmlNative.ggml_backend_reg_name(reg)),
                Description: GgmlNative.PtrToString(props.Description),
                DeviceId: GgmlNative.PtrToNullableString(props.DeviceId),
                DeviceType: props.Type,
                MemoryFreeBytes: checked((ulong)props.MemoryFree),
                MemoryTotalBytes: checked((ulong)props.MemoryTotal),
                SupportsAsync: props.Caps.Async != 0,
                SupportsHostBuffer: props.Caps.HostBuffer != 0,
                SupportsBufferFromHostPtr: props.Caps.BufferFromHostPtr != 0,
                SupportsEvents: props.Caps.Events != 0);
        }

        return backends;
    }

    public static GgmlBackendFeature[] GetBackendFeatures(string registryName)
    {
        EnsureSupported();
        EnsureBackendsLoaded();
        ArgumentException.ThrowIfNullOrWhiteSpace(registryName);

        var registry = FindBackendRegistry(registryName)
            ?? throw new InvalidOperationException($"The GGML backend registry '{registryName}' is not available.");

        var procNamePtr = Marshal.StringToCoTaskMemUTF8("ggml_backend_get_features");
        try
        {
            var proc = GgmlNative.ggml_backend_reg_get_proc_address(registry.Handle, procNamePtr);
            if (proc == IntPtr.Zero)
            {
                return [];
            }

            var getFeatures = Marshal.GetDelegateForFunctionPointer<GgmlNative.GgmlBackendGetFeaturesDelegate>(proc);
            var featuresPtr = getFeatures(registry.Handle);
            return ReadBackendFeatures(featuresPtr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(procNamePtr);
        }
    }

    public static GgmlBackendSelection? CreateBackendSelection(string? backend, string? paramsBackend)
    {
        EnsureSupported();
        EnsureBackendsLoaded();

        var descriptors = EnumerateDescriptors(backend)
            .Concat(EnumerateDescriptors(paramsBackend))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (descriptors.Length == 0)
        {
            return null;
        }

        var available = EnumerateDeviceDescriptors();
        if (available.Count == 0)
        {
            throw new InvalidOperationException("No ggml backend devices are available.");
        }

        var resolved = new List<(IntPtr Handle, string Name)>(descriptors.Length);
        foreach (var descriptor in descriptors)
        {
            var device = ResolveDevice(descriptor, available);
            if (device.Handle == IntPtr.Zero)
            {
                var availableNames = string.Join(", ", available.Select(static d => d.Name));
                throw new InvalidOperationException(
                    $"The backend '{descriptor}' is not available. Available backends: {availableNames}.");
            }

            if (resolved.Any(existing => existing.Handle == device.Handle))
            {
                continue;
            }

            resolved.Add((device.Handle, device.Name));
        }

        var pointers = new IntPtr[resolved.Count + 1];
        var names = new string[resolved.Count];
        for (var i = 0; i < resolved.Count; i++)
        {
            pointers[i] = resolved[i].Handle;
            names[i] = resolved[i].Name;
        }

        pointers[^1] = IntPtr.Zero;
        return new GgmlBackendSelection(pointers, names);
    }

    public static SystemCpuFeatureInfo GetSystemCpuFeatures()
    {
        EnsureSupported();

        var architecture = RuntimeInformation.ProcessArchitecture;
        var info = new SystemCpuFeatureInfo(
            Architecture: architecture,
            Sse3: IsIntrinsicSupported("System.Runtime.Intrinsics.X86.Sse3"),
            Ssse3: IsIntrinsicSupported("System.Runtime.Intrinsics.X86.Ssse3"),
            Avx: IsIntrinsicSupported("System.Runtime.Intrinsics.X86.Avx"),
            AvxVnni: IsIntrinsicSupported("System.Runtime.Intrinsics.X86.AvxVnni"),
            Avx2: IsIntrinsicSupported("System.Runtime.Intrinsics.X86.Avx2"),
            F16c: IsIntrinsicSupported("System.Runtime.Intrinsics.X86.F16c"),
            Fma: IsIntrinsicSupported("System.Runtime.Intrinsics.X86.Fma"),
            Bmi2: IsIntrinsicSupported("System.Runtime.Intrinsics.X86.Bmi2"),
            Avx512F: IsIntrinsicSupported("System.Runtime.Intrinsics.X86.Avx512F"),
            Avx512Vbmi: IsIntrinsicSupported("System.Runtime.Intrinsics.X86.Avx512Vbmi"),
            Avx512Vnni: IsIntrinsicSupported("System.Runtime.Intrinsics.X86.Avx512Vnni"),
            Avx512Bf16: IsIntrinsicSupported("System.Runtime.Intrinsics.X86.Avx512BF16"),
            AmxInt8: IsIntrinsicSupported("System.Runtime.Intrinsics.X86.AmxInt8"),
            Neon: IsIntrinsicSupported("System.Runtime.Intrinsics.Arm.AdvSimd"),
            ArmFma: IsIntrinsicSupported("System.Runtime.Intrinsics.Arm.ArmBase+Arm64"),
            Fp16VectorArithmetic: IsIntrinsicSupported("System.Runtime.Intrinsics.Arm.AdvSimd+Arm64"),
            MatmulInt8: IsIntrinsicSupported("System.Runtime.Intrinsics.Arm.Dp"),
            Sve: false,
            SveCount: 0,
            DotProduct: IsIntrinsicSupported("System.Runtime.Intrinsics.Arm.Dp"),
            Sme: false,
            HighestTier: string.Empty);

        return info with { HighestTier = DetermineSystemTopTier(info) };
    }

    public static GgmlCpuExecutionInfo GetCpuExecutionInfo()
    {
        EnsureSupported();
        EnsureBackendsLoaded();

        var backendFeatures = GetBackendFeatures("CPU");
        var systemFeatures = GetSystemCpuFeatures();
        var compiledTopTier = DetermineCompiledTopTier(backendFeatures);
        var systemTopTier = systemFeatures.HighestTier;
        var effectiveTopTier = DetermineEffectiveTopTier(backendFeatures, systemFeatures);

        return new GgmlCpuExecutionInfo(
            BackendFeatures: backendFeatures,
            SystemFeatures: systemFeatures,
            CompiledTopTier: compiledTopTier,
            SystemTopTier: systemTopTier,
            EffectiveTopTier: effectiveTopTier,
            Notes: "CPU backend features are queried via the ggml runtime's backend registry. On Mac Catalyst the CPU backend is statically linked into libggml.dylib; on Windows it is provided by the ggml-cpu module. Either way the features are exposed through the CPU registry.");
    }

    internal static void EnsureSupported()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacCatalyst())
        {
            throw new PlatformNotSupportedException(
                "SeasonGGML currently ships GGML native binaries only for Windows and Mac Catalyst (Apple Silicon).");
        }

        // The Mac Catalyst artifact is a pure arm64 slice, so on an Intel Mac - or under
        // Rosetta - the dylib cannot even be opened. Report that reason instead of
        // letting the first P/Invoke surface a bare DllNotFoundException.
        if (OperatingSystem.IsMacCatalyst() && RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
        {
            throw new PlatformNotSupportedException(
                "SeasonGGML ships Mac Catalyst GGML native binaries for Apple Silicon (arm64) only; " +
                $"this process runs as {RuntimeInformation.ProcessArchitecture}.");
        }

        EnsureResolverRegistered();
    }

    private static void EnsureBackendsLoaded()
    {
        if (s_backendsLoaded)
        {
            return;
        }

        lock (s_loadLock)
        {
            if (s_backendsLoaded)
            {
                return;
            }

            // Mac Catalyst ships a fully static runtime: CPU, Metal and BLAS are compiled
            // into libggml.dylib and registered by ggml's backend registry on first use.
            // There are no loadable .so modules to discover, so directory probing is a
            // no-op and is skipped.
            if (OperatingSystem.IsMacCatalyst())
            {
                s_backendsLoaded = true;
                return;
            }

            // Probing several directories is safe: ggml keys its registry off the
            // backend's reg pointer and dlopen/LoadLibrary is reference counted, so the
            // same module cannot be registered twice.
            foreach (var directory in EnumerateNativeSearchDirectories())
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                var directoryPtr = Marshal.StringToCoTaskMemUTF8(directory);
                try
                {
                    GgmlNative.ggml_backend_load_all_from_path(directoryPtr);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(directoryPtr);
                }
            }

            // Finally let ggml apply its own defaults (executable directory + cwd).
            GgmlNative.ggml_backend_load_all();
            s_backendsLoaded = true;
        }
    }

    /// <summary>
    /// Registers the resolver that maps <c>ggml</c> / <c>ggml-base</c> onto an absolute
    /// path. A ProjectReference consumer gets no <c>.deps.json</c> entry for these files,
    /// so .NET would not probe <c>runtimes/&lt;rid&gt;/native/</c> for them on its own.
    /// </summary>
    private static void EnsureResolverRegistered()
    {
        if (s_resolverRegistered)
        {
            return;
        }

        lock (s_resolverLock)
        {
            if (s_resolverRegistered)
            {
                return;
            }

            try
            {
                NativeLibrary.SetDllImportResolver(typeof(GGML).Assembly, ResolveNativeLibrary);
            }
            catch (InvalidOperationException)
            {
                // A host already installed a resolver for this assembly; let its probing win.
            }

            s_resolverRegistered = true;
        }
    }

    private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName is not (GgmlNative.LibraryName or GgmlNative.BaseLibraryName))
        {
            return IntPtr.Zero;
        }

        lock (s_resolverLock)
        {
            if (s_nativeHandles.TryGetValue(libraryName, out var cached) && cached != IntPtr.Zero)
            {
                return cached;
            }

            var fileName = GetNativeFileName(libraryName);
            foreach (var directory in EnumerateNativeSearchDirectories())
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate) &&
                    NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
                {
                    s_nativeHandles[libraryName] = handle;
                    return handle;
                }
            }
        }

        // Fall through to the default .NET probing, which is what Windows relies on today.
        return IntPtr.Zero;
    }

    private static string GetNativeFileName(string libraryName) =>
        OperatingSystem.IsWindows()
            ? $"{libraryName}.dll"
            // Mac Catalyst ships a single statically-linked runtime: ggml, ggml-base and
            // every backend are merged into one libggml.dylib, so both P/Invoke names
            // ("ggml" and "ggml-base") resolve to the same file.
            : "libggml.dylib";

    private static IEnumerable<string> EnumerateNativeSearchDirectories()
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (string.IsNullOrEmpty(baseDirectory))
        {
            yield break;
        }

        baseDirectory = Path.TrimEndingDirectorySeparator(baseDirectory);

        // 1. Next to the managed assemblies. On Windows that is the output root; on Mac
        //    Catalyst it is <App>.app/Contents/MonoBundle.
        yield return baseDirectory;

        // 2. The RID layout preserved by the NuGet package.
        var rid = GetNativeRuntimeIdentifier();
        if (rid is not null)
        {
            yield return Path.Combine(baseDirectory, "runtimes", rid, "native");
        }

        // 3. Mac Catalyst packaging puts MSBuild Content under Contents/Resources rather
        //    than next to the assemblies, so the bundle sibling has to be probed too.
        if (OperatingSystem.IsMacCatalyst() && Path.GetDirectoryName(baseDirectory) is { } contents)
        {
            yield return Path.Combine(contents, "Resources");
        }
    }

    private static string? GetNativeRuntimeIdentifier()
    {
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null,
        };

        if (arch is null)
        {
            return null;
        }

        // IsMacCatalyst has to be tested before anything else: OperatingSystem.IsIOS()
        // also reports true on Mac Catalyst.
        if (OperatingSystem.IsMacCatalyst())
        {
            return $"maccatalyst-{arch}";
        }

        return OperatingSystem.IsWindows() ? $"win-{arch}" : null;
    }

    private static (IntPtr Handle, string Name)? FindBackendRegistry(string registryName)
    {
        var count = checked((int)GgmlNative.ggml_backend_reg_count());
        for (var i = 0; i < count; i++)
        {
            var reg = GgmlNative.ggml_backend_reg_get((nuint)i);
            if (reg == IntPtr.Zero)
            {
                continue;
            }

            var name = GgmlNative.PtrToString(GgmlNative.ggml_backend_reg_name(reg));
            if (string.Equals(name, registryName, StringComparison.OrdinalIgnoreCase))
            {
                return (reg, name);
            }
        }

        return null;
    }

    private static GgmlBackendFeature[] ReadBackendFeatures(IntPtr featuresPtr)
    {
        if (featuresPtr == IntPtr.Zero)
        {
            return [];
        }

        var features = new List<GgmlBackendFeature>();
        var size = Marshal.SizeOf<GgmlNative.NativeGgmlBackendFeature>();
        for (var offset = 0; ; offset += size)
        {
            var current = IntPtr.Add(featuresPtr, offset);
            var nativeFeature = Marshal.PtrToStructure<GgmlNative.NativeGgmlBackendFeature>(current);
            if (nativeFeature.Name == IntPtr.Zero)
            {
                break;
            }

            features.Add(new GgmlBackendFeature(
                Name: GgmlNative.PtrToString(nativeFeature.Name),
                Value: GgmlNative.PtrToString(nativeFeature.Value)));
        }

        return features.ToArray();
    }

    private static bool IsIntrinsicSupported(string typeName)
    {
        var type = Type.GetType(typeName, throwOnError: false);
        if (type is null)
        {
            return false;
        }

        var property = type.GetProperty("IsSupported", BindingFlags.Public | BindingFlags.Static);
        return property?.PropertyType == typeof(bool) &&
               property.GetValue(null) is bool supported &&
               supported;
    }

    private static string DetermineCompiledTopTier(IReadOnlyList<GgmlBackendFeature> backendFeatures)
    {
        var names = backendFeatures
            .Where(static feature => string.Equals(feature.Value, "1", StringComparison.Ordinal))
            .Select(static feature => feature.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return DetermineX86Tier(names)
            ?? DetermineArmTier(names)
            ?? DetermineOtherTier(names)
            ?? "Scalar";
    }

    private static string DetermineSystemTopTier(SystemCpuFeatureInfo info)
    {
        if (info.Architecture is Architecture.X64 or Architecture.X86)
        {
            if (info.Avx512F) return "AVX512";
            if (info.Avx2) return "AVX2";
            if (info.Avx) return "AVX";
            if (info.Ssse3) return "SSSE3";
            if (info.Sse3) return "SSE3";
        }

        if (info.Architecture is Architecture.Arm64 or Architecture.Arm)
        {
            if (info.Sme) return "SME";
            if (info.Sve) return "SVE";
            if (info.DotProduct || info.MatmulInt8) return "DOTPROD";
            if (info.Neon) return "NEON";
        }

        return "Scalar";
    }

    private static string DetermineEffectiveTopTier(
        IReadOnlyList<GgmlBackendFeature> backendFeatures,
        SystemCpuFeatureInfo systemFeatures)
    {
        var names = backendFeatures
            .Where(static feature => string.Equals(feature.Value, "1", StringComparison.Ordinal))
            .Select(static feature => feature.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (systemFeatures.Architecture is Architecture.X64 or Architecture.X86)
        {
            if (names.Contains("AVX512") && systemFeatures.Avx512F) return "AVX512";
            if (names.Contains("AVX2") && systemFeatures.Avx2) return "AVX2";
            if (names.Contains("AVX") && systemFeatures.Avx) return "AVX";
            if (names.Contains("SSSE3") && systemFeatures.Ssse3) return "SSSE3";
            if (names.Contains("SSE3") && systemFeatures.Sse3) return "SSE3";
        }

        if (systemFeatures.Architecture is Architecture.Arm64 or Architecture.Arm)
        {
            if (names.Contains("SME") && systemFeatures.Sme) return "SME";
            if (names.Contains("SVE") && systemFeatures.Sve) return "SVE";
            if (names.Contains("DOTPROD") && systemFeatures.DotProduct) return "DOTPROD";
            if (names.Contains("NEON") && systemFeatures.Neon) return "NEON";
        }

        return "Scalar";
    }

    private static string? DetermineX86Tier(ISet<string> names)
    {
        if (names.Contains("AVX512")) return "AVX512";
        if (names.Contains("AVX2")) return "AVX2";
        if (names.Contains("AVX")) return "AVX";
        if (names.Contains("SSSE3")) return "SSSE3";
        if (names.Contains("SSE3")) return "SSE3";
        return null;
    }

    private static string? DetermineArmTier(ISet<string> names)
    {
        if (names.Contains("SME")) return "SME";
        if (names.Contains("SVE")) return "SVE";
        if (names.Contains("DOTPROD")) return "DOTPROD";
        if (names.Contains("NEON")) return "NEON";
        return null;
    }

    private static string? DetermineOtherTier(ISet<string> names)
    {
        if (names.Contains("RISCV_V")) return "RISCV_V";
        if (names.Contains("VSX")) return "VSX";
        if (names.Contains("VXE")) return "VXE";
        if (names.Contains("WASM_SIMD")) return "WASM_SIMD";
        return null;
    }

    private static IReadOnlyList<(IntPtr Handle, string Name, GgmlBackendDeviceType Type)> EnumerateDeviceDescriptors()
    {
        var count = checked((int)GgmlNative.ggml_backend_dev_count());
        var devices = new List<(IntPtr Handle, string Name, GgmlBackendDeviceType Type)>(count);
        for (var i = 0; i < count; i++)
        {
            var handle = GgmlNative.ggml_backend_dev_get((nuint)i);
            if (handle == IntPtr.Zero)
            {
                continue;
            }

            GgmlNative.ggml_backend_dev_get_props(handle, out var props);
            var name = GgmlNative.PtrToString(props.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            devices.Add((handle, name, props.Type));
        }

        return devices;
    }

    private static IEnumerable<string> EnumerateDescriptors(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        foreach (var item in value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(item))
            {
                yield return item;
            }
        }
    }

    private static (IntPtr Handle, string Name, GgmlBackendDeviceType Type) ResolveDevice(
        string descriptor,
        IReadOnlyList<(IntPtr Handle, string Name, GgmlBackendDeviceType Type)> available)
    {
        foreach (var device in available)
        {
            if (string.Equals(device.Name, descriptor, StringComparison.OrdinalIgnoreCase))
            {
                return device;
            }
        }

        if (string.Equals(descriptor, "cpu", StringComparison.OrdinalIgnoreCase))
        {
            return available.FirstOrDefault(static d => d.Type == GgmlBackendDeviceType.Cpu);
        }

        var prefix = GetDevicePrefix(descriptor, out var index);
        if (prefix is null)
        {
            return default;
        }

        var typedDevices = available
            .Where(device => device.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(device => ExtractTrailingNumber(device.Name))
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (typedDevices.Length == 0)
        {
            return default;
        }

        if (index is null)
        {
            return typedDevices[0];
        }

        foreach (var device in typedDevices)
        {
            if (ExtractTrailingNumber(device.Name) == index.Value)
            {
                return device;
            }
        }

        return default;
    }

    private static string? GetDevicePrefix(string descriptor, out int? index)
    {
        var trimmed = descriptor.Trim();
        index = null;

        var digitStart = trimmed.Length;
        for (var i = trimmed.Length - 1; i >= 0; i--)
        {
            if (!char.IsDigit(trimmed[i]))
            {
                digitStart = i + 1;
                break;
            }
        }

        if (digitStart < trimmed.Length &&
            int.TryParse(trimmed.AsSpan(digitStart), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedIndex))
        {
            index = parsedIndex;
            trimmed = trimmed[..digitStart];
        }

        return trimmed.Length == 0 ? null : trimmed;
    }

    private static int ExtractTrailingNumber(string name)
    {
        var digitStart = name.Length;
        for (var i = name.Length - 1; i >= 0; i--)
        {
            if (!char.IsDigit(name[i]))
            {
                digitStart = i + 1;
                break;
            }
        }

        return digitStart < name.Length &&
            int.TryParse(name.AsSpan(digitStart), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }
}
