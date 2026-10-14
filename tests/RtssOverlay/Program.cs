using System;
using System.IO.MemoryMappedFiles;
using System.Text;
using Burmalk.Services;
using Burmalk.Models;

if(Array.Exists(args,arg=>arg=="--probe-live"))
{
    using var live=MemoryMappedFile.OpenExisting("RTSSSharedMemoryV2",MemoryMappedFileRights.Read);
    using var liveView=live.CreateViewAccessor(0,0,MemoryMappedFileAccess.Read);
    Console.WriteLine($"RTSS mapping: signature=0x{liveView.ReadUInt32(0):X8}, version=0x{liveView.ReadUInt32(4):X8}, app entries={liveView.ReadUInt32(16)}, OSD slots={liveView.ReadUInt32(28)}");
    if(args.Length>1&&int.TryParse(args[1],out int requestedPid))
    {
        var liveFrames=RtssOverlay.ReadFrames(liveView,requestedPid);
        Console.WriteLine($"Game PID {requestedPid}: connected={liveFrames.Available}, FPS={liveFrames.Fps:0.0}, frame={liveFrames.FrameMs:0.00} ms");
    }
    long liveEntrySize=liveView.ReadUInt32(20),liveOffset=liveView.ReadUInt32(24),liveCount=liveView.ReadUInt32(28);
    if(liveEntrySize>=4608&&liveCount<=256&&liveOffset+liveEntrySize*liveCount<=liveView.Capacity)
    for(long i=1;i<liveCount;i++)
    {
        var owner=new byte[256];liveView.ReadArray(liveOffset+i*liveEntrySize+256,owner,0,owner.Length);
        if(Encoding.ASCII.GetString(owner).TrimEnd('\0')!="Burmalk")continue;
        var osd=new byte[256];liveView.ReadArray(liveOffset+i*liveEntrySize+512,osd,0,osd.Length);
        Console.WriteLine("Burmalk OSD slot="+i+", text="+Encoding.Latin1.GetString(osd).TrimEnd('\0').Replace('\n','|'));
    }
    return;
}
using var map=MemoryMappedFile.CreateNew(null,20000);
using var v=map.CreateViewAccessor();
int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
string Read(long offset){var data=new byte[256];v.ReadArray(offset,data,0,256);return Encoding.ASCII.GetString(data).TrimEnd('\0');}
Check(!RtssOverlay.Write(v,"bad",42),"Reject missing signature");
v.Write(0,0x52545353u);v.Write(4,0x20014u);
v.Write(8,512u);v.Write(12,64u);v.Write(16,1u);v.Write(64,42u);
v.Write(20,4608u);v.Write(24,1024u);v.Write(28,3u);
v.WriteArray(1024+256,Encoding.ASCII.GetBytes("OtherClient"),0,11);
Check(RtssOverlay.Write(v,"BURMALK\nCPU 50 C",42),"Publish to connected game");
Check(Read(1024+4608+256)=="Burmalk","Own secondary slot");
Check(Read(1024+4608+512)=="BURMALK\nCPU 50 C","OSD content");
Check(Read(1024+256)=="OtherClient","Preserve primary client");
v.Write(36,1);Check(!RtssOverlay.Write(v,"locked",42),"Respect renderer lock");
Check(v.ReadInt32(36)==1,"Do not release somebody else's lock");v.Write(36,0);
Check(!RtssOverlay.Write(v,"wrong game",99),"Reject unconnected game");
Check(Read(1024+4608+512)==""&&Read(1024+4608+256)=="","Clear our slot after game leaves");
Check(v.ReadInt32(36)==0,"Release acquired lock");
v.Write(24,uint.MaxValue);Check(!RtssOverlay.Write(v,"bad",42),"Reject out-of-range table");
v.Write(24,1024u);
v.Write(64+268,1000u);v.Write(64+272,2000u);v.Write(64+276,120u);v.Write(64+280,8000u);
var frames=RtssOverlay.ReadFrames(v,42);
Check(frames.Available&&Math.Abs(frames.Fps-120)<0.01&&Math.Abs(frames.FrameMs-8)<0.01,"Read FPS and frame time measured by RTSS");
Check(!RtssOverlay.ReadFrames(v,99).Available,"No FPS for a different process");
var display=new OverlayText();var settings=new AppSettings();var game=new GameCurrent{Pid=42};
var sample=new Sample{CpuPercent=37,CpuTempC=61,CpuGhz=4.2,GpuPercent=80,GpuTempC=69,GpuClockMhz=1900,GpuPowerW=180,GpuVramUsedGb=4,GpuVramTotalGb=8,RamUsedGb=17,NetDownKBps=500,NetUpKBps=60};
var now=DateTime.UtcNow;
string first=display.Build(settings,null,game,sample,frames,now);
Check(first.Contains("FPS 120  AVG 120")&&first.Contains("FRAME 8.0 ms"),"Show live FPS, mean FPS and frame time");
Check(first.Contains("CPU 37%  61 C  4200 MHz")&&first.Contains("GPU 80%  69 C  1900 MHz  180 W")&&first.Contains("VRAM 4.0 GiB / 8.0 GiB"),"Show hardware details without invented values");
Check(first.Contains("NET DOWN 500 KB/s  UP 60 KB/s"),"Show network throughput");
string second=display.Build(settings,null,game,sample,new RtssOverlay.FrameStats(60,16),now.AddSeconds(1));
Check(second.Contains("AVG 90"),"Update mean FPS from RTSS samples");
Check(!display.Build(settings,null,new GameCurrent{Pid=77},sample,new RtssOverlay.FrameStats(double.NaN,double.NaN),now.AddSeconds(2)).Contains("FPS"),"Reset FPS at a new game and omit unavailable metrics");
settings.OverlayShowFps=false;settings.OverlayShowFrameTime=false;settings.OverlayShowNetwork=false;
var custom=display.Build(settings,null,game,sample,frames,now);
Check(!custom.Contains("FPS ")&&custom.Contains("AVG 120")&&!custom.Contains("FRAME ")&&!custom.Contains("NET "),"Independent FPS, average, frame time and network switches");
settings.OverlayColor="55e6ce";settings.OverlayTextScale=140;settings.OverlayLayout="Compact";
custom=display.Build(settings,null,game,sample,frames,now);
Check(custom.StartsWith("<C=55E6CE><S=105>BURMALK<S=140>")&&custom.EndsWith("<S><C>"),"RTSS color and percentage size tags are scoped; branding is smaller");
Check(!custom.Contains('\n')&&custom.Contains("  |  "),"Compact overlay layout");
settings.OverlayColor="bad<tag>";settings.OverlayTextScale=int.MaxValue;
Check(display.Build(settings,null,game,sample,frames,now).StartsWith("<C=FFFFFF><S=135>"),"Unsafe color and size are bounded");
settings.OverlayShowAverage=false;settings.OverlayShowCpu=false;settings.OverlayShowGpu=false;settings.OverlayShowRam=false;settings.OverlayShowVram=false;
Check(display.BuildPlain(settings,null,game,sample,frames,now)=="BURMALK","Empty selection retains small application identity");
var profile=new GameProfile {ShowCpu=true,ShowGpu=false,ShowRam=false,ShowVram=false};
Check(display.BuildPlain(settings,profile,game,sample,frames,now).Contains("CPU "),"Per-game hardware preferences still apply");
Console.WriteLine($"PASS: {checks} RTSS protocol checks using an isolated anonymous mapping.");
