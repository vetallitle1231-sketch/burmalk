using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Burmalk.Services;
internal static class NvidiaPhysicalMemory
{
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr Query(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Initialize();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Enumerate([Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 64)] IntPtr[] handles, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private delegate int Name(IntPtr handle, StringBuilder text);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Memory(IntPtr handle, out uint kibibytes);
    private static T Api<T>(uint id) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Query(id));
    public static double ReadGiB(string expected)
    {
        if (expected?.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) != true) return double.NaN;
        try
        {
            if (Api<Initialize>(0x0150E828)() != 0) return double.NaN;
            var handles = new IntPtr[64];
            if (Api<Enumerate>(0xE5AC921F)(handles, out uint count) != 0) return double.NaN;
            for (int i = 0; i < Math.Min(count, 64); i++)
            {
                var name = new StringBuilder(64);
                if (Api<Name>(0xCEEE8E9F)(handles[i], name) != 0 || !expected.Contains(name.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
                // Public NVIDIA API reports physical framebuffer KiB, without dedicated system RAM.
                if (Api<Memory>(0x46FBEB03)(handles[i], out uint kib) == 0 && kib > 0) return kib / 1048576d;
            }
        }
        catch { }
        return double.NaN;
    }
}
