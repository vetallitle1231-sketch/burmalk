using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace Burmalk.Services;

// RTSS SDK v2.14+ shared-memory protocol. Only our own secondary OSD slot is changed.
internal static class RtssOverlay
{
    internal readonly record struct FrameStats(double Fps,double FrameMs, uint Stamp = 0, uint Time0 = 0, uint Frames = 0)
    {
        public bool Available=>double.IsFinite(Fps)&&Fps>0;
        public uint ElapsedMs => unchecked(Stamp - Time0);
    }
    internal const string Owner="Burmalk";
    public static string Status {get;private set;}="RTSS is not connected.";
    public static bool Publish(string text,int pid)
    {
        try
        {
            using var map=MemoryMappedFile.OpenExisting("RTSSSharedMemoryV2",MemoryMappedFileRights.ReadWrite);
            using var view=map.CreateViewAccessor();
            bool result=Write(view,text,pid);
            Status=result?"RTSS overlay connected to the game.":"RTSS is running; waiting for it to connect to this game. Restart the game if needed.";
            return result;
        }
        catch(FileNotFoundException){Status="Start RTSS to use the fullscreen overlay.";}
        catch(UnauthorizedAccessException){Status="RTSS access denied. Run Burmalk and RTSS with matching permissions.";}
        catch(Exception ex){Status="RTSS: "+ex.Message;}
        return false;
    }
    // RTSS itself measures presented frames, so FPS stays available when ETW capture is unavailable.
    public static FrameStats ReadFrames(int pid)
    {
        if(pid<=0)return default;
        try
        {
            using var map=MemoryMappedFile.OpenExisting("RTSSSharedMemoryV2",MemoryMappedFileRights.Read);
            using var view=map.CreateViewAccessor(0,0,MemoryMappedFileAccess.Read);
            return ReadFrames(view,pid);
        }
        catch{return default;}
    }
    internal static FrameStats ReadFrames(MemoryMappedViewAccessor v,int pid)
    {
        if(pid<=0||v.Capacity<40||v.ReadUInt32(0)!=0x52545353||v.ReadUInt32(4)<0x20000)return default;
        long size=v.ReadUInt32(8),offset=v.ReadUInt32(12),count=v.ReadUInt32(16);
        if(size<284||count>4096||offset<40||offset+count*size>v.Capacity)return default;
        for(long i=0;i<count;i++)
        {
            long at=offset+i*size;
            if(v.ReadUInt32(at)!=(uint)pid)continue;
            uint time1=v.ReadUInt32(at+272),time0=v.ReadUInt32(at+268),frames=v.ReadUInt32(at+276),microseconds=v.ReadUInt32(at+280);
            if(time1!=v.ReadUInt32(at+272)||time0!=v.ReadUInt32(at+268)||frames!=v.ReadUInt32(at+276))return default;
            uint elapsed=unchecked(time1-time0);
            double fps=time0>0&&time1>0&&elapsed>0&&elapsed<=60000&&frames>0?1000d*frames/elapsed:double.NaN;
            double frameMs=microseconds>0?microseconds/1000d:double.NaN;
            // Last-frame time and interval FPS are different measurements. No reciprocal fallback.
            return new FrameStats(double.IsFinite(fps)&&fps>0&&fps<100000?fps:double.NaN,double.IsFinite(frameMs)&&frameMs>0&&frameMs<=60000?frameMs:double.NaN,time1,time0,frames);
        }
        return default;
    }
    internal static unsafe bool Write(MemoryMappedViewAccessor v,string text,int pid)
    {
        if(v.Capacity<40||v.ReadUInt32(0)!=0x52545353||v.ReadUInt32(4)<0x2000e)return false;
        long appSize=v.ReadUInt32(8),appOffset=v.ReadUInt32(12),appCount=v.ReadUInt32(16);
        long size=v.ReadUInt32(20),offset=v.ReadUInt32(24),count=v.ReadUInt32(28);
        if(size<4608||count<2||count>256||offset<40||offset+count*size>v.Capacity||
           appSize<4||appCount>4096||appOffset<40||appOffset+appCount*appSize>v.Capacity)return false;
        bool connected=false;
        if(pid>0)for(long i=0;i<appCount;i++)if(v.ReadUInt32(appOffset+i*appSize)==pid){connected=true;break;}
        // No content is left behind if the requested game is not connected.
        text=connected?text:"";
        byte* pointer=null;
        v.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        try
        {
            ref int busy=ref *(int*)(pointer+v.PointerOffset+36);
            if(Interlocked.CompareExchange(ref busy,1,0)!=0)return false;
            try
            {
                long slot=-1;
                for(long i=1;i<count;i++)if(ReadString(v,offset+i*size+256,256)==Owner){slot=offset+i*size;break;}
                if(slot<0&&text.Length>0)for(long i=1;i<count;i++)if(v.ReadByte(offset+i*size+256)==0){slot=offset+i*size;break;}
                if(slot<0)return text.Length==0&&connected;
                WriteString(v,slot+512,4096,text);
                WriteString(v,slot,256,"");
                WriteString(v,slot+256,256,text.Length>0?Owner:"");
                v.Write(32,unchecked(v.ReadUInt32(32)+1));
                return connected;
            }
            finally{Volatile.Write(ref busy,0);}
        }
        finally{v.SafeMemoryMappedViewHandle.ReleasePointer();}
    }
    private static string ReadString(MemoryMappedViewAccessor v,long at,int capacity)
    {var b=new byte[capacity];v.ReadArray(at,b,0,b.Length);int end=Array.IndexOf(b,(byte)0);return Encoding.ASCII.GetString(b,0,end<0?capacity:end);}
    private static void WriteString(MemoryMappedViewAccessor v,long at,int capacity,string text)
    {var b=new byte[capacity];Encoding.Latin1.GetBytes(text.AsSpan(0,Math.Min(text.Length,capacity-1)),b);v.WriteArray(at,b,0,b.Length);}
    public static void Clear()=>Publish("",0);
    public static void Start()
    {
        string path=Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Unwinder\RTSS","InstallPath",null) as string;
        if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"RivaTuner Statistics Server","RTSS.exe");
        if(!File.Exists(path)){Status="Install RTSS from guru3d.com, then click Start RTSS.";return;}
        try
        {
            if(System.Diagnostics.Process.GetProcessesByName("RTSS").Length==0)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden});
            Status="RTSS is ready. Open or restart your game to connect its overlay.";
        }
        catch(Exception ex){Status="Cannot start RTSS: "+ex.Message;}
    }
}
