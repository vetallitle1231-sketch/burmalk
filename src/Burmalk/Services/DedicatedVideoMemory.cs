using System;
using System.Runtime.InteropServices;

namespace Burmalk.Services;

// DXGI uses SIZE_T for dedicated memory; Win32_VideoController.AdapterRAM is only UInt32.
internal static class DedicatedVideoMemory
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Desc
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Vendor, Device, Subsystem, Revision;
        public UIntPtr Dedicated, System, Shared;
        public long Luid;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapter(IntPtr self, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDesc(IntPtr self, out Desc desc);
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
    private static T Method<T>(IntPtr obj, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size));
    public static double ReadGiB(string name)
    {
        IntPtr factory = IntPtr.Zero;
        try
        {
            var id = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
            if (CreateDXGIFactory1(ref id, out factory) < 0) return double.NaN;
            for (uint i = 0; i < 32; i++)
            {
                if (Method<EnumAdapter>(factory, 7)(factory, i, out var adapter) < 0) break;
                try
                {
                    if (Method<GetDesc>(adapter, 8)(adapter, out var d) >= 0 && string.Equals(d.Description.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase))
                        return d.Dedicated.ToUInt64() > 0 ? d.Dedicated.ToUInt64() / 1073741824d : double.NaN;
                }
                finally { Marshal.Release(adapter); }
            }
        }
        catch (Exception ex) { ErrorLog.Write("DXGI memory: " + ex.Message); }
        finally { if (factory != IntPtr.Zero) Marshal.Release(factory); }
        return double.NaN;
    }
}
