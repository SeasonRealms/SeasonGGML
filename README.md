# SeasonGGML

`SeasonGGML` provides shared GGML native dependencies and backend enumeration helpers for
`SeasonLLM`, `SeasonImage`, `SeasonTTS`, and other SeasonEngine projects.

https://github.com/SeasonRealms/SeasonGGML

## Supported Platforms

| Platform | RID | Linkage | Backends |
| --- | --- | --- | --- |
| Windows (x64) | `win-x64` | Dynamic (multi-module) | CPU multi-variant, CUDA, Vulkan |
| Mac Catalyst | `maccatalyst-arm64` | Static (single dylib) | CPU (M1 baseline), Metal, BLAS/Accelerate |

- **Mac Catalyst is Apple Silicon only.** No `x86_64` slice is produced, so on an Intel
  Mac — or under Rosetta — `GGML.IsSupported` reports `false` and the API throws
  `PlatformNotSupportedException`.
- Windows keeps the dynamic multi-backend layout (`ggml.dll`, `ggml-base.dll`,
  `ggml-cpu-*.dll`, `ggml-cuda.dll`, `ggml-vulkan.dll`); it is unchanged.

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

On Windows the query entry point is `ggml.dll` while the CPU feature implementation lives
in the `ggml-cpu.dll` plugin. On Mac Catalyst both live in the single statically linked
`libggml.dylib`.

## Mac Catalyst: Static Linkage

### Why static

An earlier Mac Catalyst plan loaded backends as `.so` modules via `dlopen`
(`GGML_BACKEND_DL=ON`). For a Catalyst bundle that meant roughly a dozen nested Mach-O
files — every one of which must be signed with the app's Team ID, including `dlopen`ed
modules that hardened runtime's Library Validation treats with suspicion.

The current build uses `GGML_BACKEND_DL=OFF` + `BUILD_SHARED_LIBS=OFF`: every backend is
archived as a static library and merged into a single `libggml.dylib` with `-force_load`.
The result:

- exactly **one Mach-O** for the whole GGML runtime — codesign has a single, well-understood
  artifact, covered by the bundle's nested-code signature;
- **zero `dlopen`** — CPU / Metal / BLAS are registered by ggml's backend registry
  constructor on first use;
- **no `.so` modules** — nothing is placed in `Contents/Resources`, so there is no
  nested-code audit surprise.

### Backends included

| Backend | Notes |
| --- | --- |
| CPU | Compiled for `armv8.2-a+dotprod`, the M1 baseline. Runs on M1 and every later Apple Silicon chip. |
| Metal | `GGML_METAL_EMBED_LIBRARY=ON`: the shader source is embedded in the dylib, so no `default.metallib` has to be deployed. |
| BLAS | Accelerate-backed; a build-time choice via the `maccatalyst_blas` workflow input (default ON). |
| CUDA / Vulkan | Not available on macOS / Mac Catalyst and explicitly disabled. |

### Tradeoffs of static linkage

1. **Single CPU baseline instead of runtime multi-variant selection.** Upstream
   `GGML_CPU_ALL_VARIANTS` (apple_m1 / apple_m2_m3 / apple_m4) requires
   `GGML_BACKEND_DL` and aborts the build without it, so it is mutually exclusive with
   static linking. The runtime is fixed at the M1 baseline: M2/M3/M4 chips run it but do
   not get their newer `MATMUL_INT8`/SME CPU paths. Metal is the recommended backend on
   those chips, which hides most of this cost.
2. **BLAS is a build-time switch, not a runtime one.** Turning it off requires a rebuild
   with `maccatalyst_blas=OFF`.
3. **Intel Macs are unsupported by design.** No `maccatalyst-x64` artifact exists and
   `GGML.IsSupported` is `false` for x86_64 / Rosetta processes.
4. **One larger dylib.** All backends plus the embedded Metal shader source live in a
   single file; deployment simplicity is the trade for size.

### Upstream notes (SeasonRealms/ggml)

- No source patches are required; the fork builds as-is with the flags listed in
  *Building the native runtimes*.
- `GGML_CPU_ALL_VARIANTS` must not be enabled without `GGML_BACKEND_DL` — the static build
  uses `GGML_CPU_ARM_ARCH=armv8.2-a+dotprod` instead.
- `ggml_get_system_arch()` compares `CMAKE_OSX_ARCHITECTURES` with `STREQUAL`, so the
  build must be single-architecture (`arm64`); a universal `arm64;x86_64` value would fall
  back to the host processor and pick the wrong variant set.
- `CMAKE_SYSTEM_NAME` stays unset (keeps `APPLE=ON` so the Apple ARM branch is taken) and
  `CMAKE_OSX_DEPLOYMENT_TARGET` stays empty; Catalyst is expressed purely as the clang
  target triple `arm64-apple-ios<ver>-macabi`.
- All targets are compiled with `-DGGML_MAX_NAME=128`.

### Downstream notes (consumers)

- **Target framework**: consuming apps must target `net10.0-maccatalyst`. SeasonGGML ships
  that TFM with `SupportedOSPlatformVersion=15.0` (Catalyst ≈ macOS 12+); an app with a
  higher minimum such as `17.0` is fine.
- **Runtime identifier**: only `maccatalyst-arm64` exists. Building the app for
  `maccatalyst-x64` finds no native assets. In the SeasonEngine workspace,
  `Apps\Engine\Engine.csproj` still lists `maccatalyst-x64` in `RuntimeIdentifiers` — do
  not build that RID until an x64 artifact is added.
- **P/Invoke names**: both `"ggml"` and `"ggml-base"` resolve to the single
  `libggml.dylib` through SeasonGGML's `DllImportResolver`. Do not add a second
  `ggml-base.dylib`; none is needed.
- **Bundle layout**: MSBuild `Content` lands in `Contents/Resources` while assemblies land
  in `Contents/MonoBundle`; SeasonGGML probes both directories automatically.
- **Backend loading**: no `ggml_backend_load_all*` call is needed on Mac Catalyst — the
  registry registers CPU/Metal/BLAS on first use and SeasonGGML skips module probing there.
  Do not ship extra `libggml-*.so` files next to the dylib; they would not be loaded.
- **Signing**: sign the app bundle as usual. The single dylib is covered by the bundle's
  nested-code signature and needs no per-module ad-hoc signing workarounds.
- **Windows consumers are unaffected**, but still require every GGML DLL (`ggml.dll`,
  `ggml-base.dll`, `ggml-cpu-*.dll`, `ggml-cuda.dll`, `ggml-vulkan.dll`) to load from the
  same directory.
- **SeasonAI / Engine (workspace)**: `SeasonAI.csproj` currently targets `net10.0` only
  and consumes SeasonGGML via a `PackageReference`, so it must add `net10.0-maccatalyst`
  before it can use the Catalyst runtime. `Apps\Engine\Engine.csproj` still has the
  `SeasonAI` `ProjectReference` commented out.

## Building the native runtimes

Native binaries are produced by `.github/workflows/build-ggml.yml`
(`workflow_dispatch` only). Exactly one job runs per dispatch, selected by `build_target`:

| Input | Default | Meaning |
| --- | --- | --- |
| `build_target` | `maccatalyst` | Which job runs: `maccatalyst` or `windows` |
| `configuration` | `Release` | CMake build type |
| `ggml_ref` | `master` | `SeasonRealms/ggml` tag / branch / commit |
| `maccatalyst_min_ios` | `15.0` | Catalyst deployment target used in the `*-macabi` triple |
| `maccatalyst_blas` | `ON` | Build the Accelerate-backed BLAS backend |
| `cuda_version` / `cuda_architectures` / `cudnn_redist_url` | — | Windows CUDA / cuDNN inputs |
| `build_parallel` | `4` | Parallel build jobs |

The Mac Catalyst job runs on `macos-15` (arm64) and:

1. configures CMake with `BUILD_SHARED_LIBS=OFF`, `GGML_BACKEND_DL=OFF`,
   `GGML_CPU_ARM_ARCH=armv8.2-a+dotprod`, `GGML_METAL=ON`, `GGML_METAL_EMBED_LIBRARY=ON`,
   `GGML_BLAS` per the input, and `GGML_OPENMP`/`GGML_CUDA`/`GGML_VULKAN=OFF`;
2. builds the static archives (`libggml-base.a`, `libggml.a`, `libggml-cpu.a`,
   `libggml-metal.a`, `libggml-blas.a`);
3. merges them into one `libggml.dylib` with
   `clang++ -dynamiclib -Wl,-force_load,...` against Foundation / Metal / MetalKit /
   Accelerate;
4. validates the result: exported symbols (`ggml_backend_dev_count`,
   `ggml_backend_reg_count`), `lipo -info` (arm64 only, no Intel slice), and `vtool`
   (MACCATALYST platform tag);
5. uploads `out/runtimes/maccatalyst-arm64/native/multi-backend/` containing
   `libggml.dylib`, the public headers, `ggml.txt` (LICENSE) and a generated `README.txt`.

Drop the artifact into `runtimes/maccatalyst-arm64/native/` to ship it through the NuGet
package's `runtimes/<rid>/native` layout.

**Transfer it as a binary.** Mach-O headers contain bytes that are invalid UTF-8; routing
the dylib through a text-mode channel (text editors, some sync tools / file shares) rewrites
them into UTF-8 replacement characters and silently corrupts the file, which `dyld` then
refuses to load. Download the workflow artifact zip and unpack it, then verify:

- compare `shasum -a 256` against the `libggml.dylib sha256:` line in the generated
  `README.txt`;
- the first four bytes must be `CF FA ED FE` (64-bit little-endian Mach-O magic).
