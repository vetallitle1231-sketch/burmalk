using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using Microsoft.Win32.SafeHandles;

namespace Burmalk.Services
{
    public static class CpuSensorAccess
    {
        public static bool IsAdministrator => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        [StructLayout(LayoutKind.Sequential)] private struct ServiceStatus {public uint Type,State,Accepted,ExitCode,SpecificExitCode,Checkpoint,WaitHint;}
        [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr OpenSCManager(string machine,string database,uint access);
        [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr OpenService(IntPtr manager,string name,uint access);
        [DllImport("advapi32.dll",SetLastError=true)] private static extern bool QueryServiceStatus(IntPtr service,out ServiceStatus status);
        [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool StartService(IntPtr service,uint argc,IntPtr argv);
        [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(IntPtr handle);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
        public static int DeviceError()
        {
            using var device=CreateFile(@"\\?\GLOBALROOT\Device\PawnIO",3,3,IntPtr.Zero,3,0,IntPtr.Zero);
            return device.IsInvalid?Marshal.GetLastWin32Error():0;
        }
        private static string RunningStatus()=>DeviceError() switch {0=>"PawnIO ready",2 or 3=>"PawnIO device missing · click Enable CPU temperature to repair setup",5=>"Administrator access required · click Enable CPU temperature",var code=>"PawnIO device access error: "+code};
        public static string EnsureDriver()
        {
            var manager=OpenSCManager(null,null,1);if(manager==IntPtr.Zero)return "Cannot access Windows services: "+Marshal.GetLastWin32Error();
            IntPtr service=IntPtr.Zero;
            try
            {
                service=OpenService(manager,"PawnIO",4|(IsAdministrator?16u:0u));
                if(service==IntPtr.Zero)return Marshal.GetLastWin32Error()==1060?"PawnIO driver is not installed":"PawnIO access denied";
                if(!QueryServiceStatus(service,out var state))return "Cannot query PawnIO: "+Marshal.GetLastWin32Error();
                if(state.State==4)return RunningStatus();
                if(!IsAdministrator)return "PawnIO stopped · click Enable CPU temperature and approve Windows UAC";
                if(!StartService(service,0,IntPtr.Zero)&&Marshal.GetLastWin32Error()!=1056)return "PawnIO could not start: "+new Win32Exception(Marshal.GetLastWin32Error()).Message;
                for(int i=0;i<40;i++){if(QueryServiceStatus(service,out state)&&state.State==4)return RunningStatus();System.Threading.Thread.Sleep(50);}
                return "PawnIO is starting; restart Burmalk if sensors remain unavailable";
            }
            finally{if(service!=IntPtr.Zero)CloseServiceHandle(service);CloseServiceHandle(manager);}
        }
        public static bool RestartElevated()
        {
            string profile = App.IsExperimentalProfile ? " --profile-experimental" : "";
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--start-minimized")>=0)profile+=" --start-minimized";
            var child=Process.Start(new ProcessStartInfo(Environment.ProcessPath,"--wait-for-process "+Environment.ProcessId + profile){UseShellExecute=true,Verb="runas"});
            if(child==null)return false;
            Application.Current.Shutdown();return true;
        }
        public static async void Enable()
        {
            try
            {
                if(DeviceError() is 2 or 3)
                {
                    string installer=Path.Combine(AppContext.BaseDirectory,"Support","PawnIO_setup.exe");
                    if(!File.Exists(installer)){MessageBox.Show("The CPU driver installer is missing. Reinstall Burmalk using the current setup.","Burmalk");return;}
                    using(var stream=File.OpenRead(installer))if(Convert.ToHexString(SHA256.HashData(stream))!="1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032")throw new InvalidOperationException("CPU driver installer integrity check failed.");
                    using var setup=Process.Start(new ProcessStartInfo(installer,"-install -silent"){UseShellExecute=true,Verb="runas"});
                    if(setup==null)return;
                    await setup.WaitForExitAsync();
                    if(setup.ExitCode==3010){MessageBox.Show("CPU driver installed. Restart Windows, then enable CPU temperature again.","Burmalk");return;}
                    if(setup.ExitCode!=0)throw new InvalidOperationException("CPU driver setup returned "+setup.ExitCode);
                }
                bool previous=App.Store.Settings.CpuSensorsEnabled;
                App.Store.Settings.CpuSensorsEnabled=true;App.Store.Save();
                try{RestartElevated();}catch{App.Store.Settings.CpuSensorsEnabled=previous;App.Store.Save();throw;}
            }
            catch(Win32Exception ex) when(ex.NativeErrorCode==1223) { MessageBox.Show("Windows permission was cancelled. Burmalk continues without hardware CPU temperature.","Burmalk"); }
            catch(Exception ex) {MessageBox.Show("Unable to enable CPU sensors: "+ex.Message,"Burmalk");}
        }
    }
}
