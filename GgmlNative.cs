// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonGGML

namespace Season.GGML;

internal static class GgmlNative
{
#if IOS
    // iOS ships a single statically-linked runtime (libggml.a merges ggml, ggml-base
    // and every backend), so both P/Invoke names bind against the executable itself
    // via "__Internal" - there is no dylib to load at runtime.
    internal const string LibraryName = "__Internal";
    internal const string BaseLibraryName = "__Internal";
#else
    internal const string LibraryName = "ggml";
    internal const string BaseLibraryName = "ggml-base";
#endif

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeGgmlBackendDevCaps
    {
        public byte Async;
        public byte HostBuffer;
        public byte BufferFromHostPtr;
        public byte Events;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeGgmlBackendDevProps
    {
        public IntPtr Name;
        public IntPtr Description;
        public nuint MemoryFree;
        public nuint MemoryTotal;
        public GgmlBackendDeviceType Type;
        public IntPtr DeviceId;
        public NativeGgmlBackendDevCaps Caps;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeGgmlBackendFeature
    {
        public IntPtr Name;
        public IntPtr Value;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr GgmlBackendGetFeaturesDelegate(IntPtr reg);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void ggml_backend_load_all();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void ggml_backend_load_all_from_path(IntPtr dirPath);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nuint ggml_backend_dev_count();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr ggml_backend_dev_get(nuint index);

    [DllImport(BaseLibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void ggml_backend_dev_get_props(IntPtr device, out NativeGgmlBackendDevProps props);

    [DllImport(BaseLibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr ggml_backend_dev_backend_reg(IntPtr device);

    [DllImport(BaseLibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr ggml_backend_reg_name(IntPtr reg);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nuint ggml_backend_reg_count();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr ggml_backend_reg_get(nuint index);

    [DllImport(BaseLibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern IntPtr ggml_backend_reg_get_proc_address(IntPtr reg, IntPtr name);

    internal static string PtrToString(IntPtr ptr) =>
        ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(ptr) ?? string.Empty;

    internal static string? PtrToNullableString(IntPtr ptr) =>
        ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);
}
