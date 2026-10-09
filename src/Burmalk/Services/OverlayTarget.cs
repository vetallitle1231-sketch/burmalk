using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Burmalk.Services
{
    internal static class OverlayTarget
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Bounds {public int Left,Top,Right,Bottom;}
        [StructLayout(LayoutKind.Sequential)] private struct Point {public int X,Y;}
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window,out Bounds rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window,ref Point point);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window,StringBuilder text,int count);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window,out Bounds rect);
        [DllImport("user32.dll",SetLastError=true)] internal static extern bool SetWindowPos(IntPtr window,IntPtr insertAfter,int x,int y,int width,int height,uint flags);
        internal static int Pid(IntPtr window){GetWindowThreadProcessId(window,out uint pid);return (int)pid;}
        internal static bool IsExternalTarget(IntPtr window)
        {
            if(window==IntPtr.Zero||Pid(window)==Environment.ProcessId||!TryBounds(window,out _))return false;
            var cls=new StringBuilder(128);GetClassName(window,cls,cls.Capacity);
            return cls.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
        }
        internal static IntPtr ForProcess(int pid)
        {
            if(pid<=0||pid==Environment.ProcessId)return IntPtr.Zero;
            var foreground=GetForegroundWindow();if(Pid(foreground)==pid)return foreground;
            try{using var process=Process.GetProcessById(pid);return process.MainWindowHandle;}catch{return IntPtr.Zero;}
        }
        internal static bool TryBounds(IntPtr window,out Bounds bounds)
        {
            bounds=default;
            if(window==IntPtr.Zero||!IsWindow(window)||!IsWindowVisible(window)||IsIconic(window)||!GetClientRect(window,out var client))return false;
            var start=new Point{X=client.Left,Y=client.Top};var end=new Point{X=client.Right,Y=client.Bottom};
            if(!ClientToScreen(window,ref start)||!ClientToScreen(window,ref end)||end.X<=start.X||end.Y<=start.Y)return false;
            bounds=new Bounds{Left=start.X,Top=start.Y,Right=end.X,Bottom=end.Y};return true;
        }
    }
}
