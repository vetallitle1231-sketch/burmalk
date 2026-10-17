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
var display=new OverlayText();var settings=new AppSettings();var game=new GameCurrent{Pid=42,FpsAvailable=true,FpsCurrent=999,FpsAvg=999};
var now=DateTime.UtcNow;
var sample=new Sample{TimestampUtc=now.Ticks,GpuTimestampUtc=now.Ticks,GpuName="NVIDIA RTX Test",CpuPercent=37,CpuTempC=61,CpuGhz=4.2,GpuPercent=80,GpuTempC=69,GpuClockMhz=1900,GpuMemClockMhz=17000,GpuPowerW=180,GpuVramUsedGb=4,GpuVramTotalGb=8,RamUsedGb=17,RamTotalGb=31.92,NetDownKBps=500,NetUpKBps=2048};
string waiting=display.BuildPlain(settings,null,game,sample,frames,now.AddSeconds(-1));
Check(waiting.Contains("FPS   --")&&!waiting.Contains("999"),"An unchanging RTSS mapping never borrows dashboard FPS");
frames=new RtssOverlay.FrameStats(120,8,3000,2000,120);
string first=display.BuildPlain(settings,null,game,sample,frames,now);
Check(first.Contains("FPS   120    AVG 120.0"),"Live FPS and counted-frame AVG");
Check(first.Contains("FRAME 8.33 ms AVG   8.00 ms LAST"),"Average frame duration and last-frame duration have distinct labels");
Check(first.Contains("CPU   37%   61 °C   CLK(avg) 4200 MHz")&&first.Contains("CORE 1900 MHz")&&first.Contains("MEM 17000 MHz"),"CPU core, GPU core and GPU memory frequencies cannot be confused");
Check(first.Contains("VRAM  4.00 / 8.00 GiB")&&first.Contains("RAM   17.00 / 31.92 GiB"),"Memory reports measured binary units and usable capacity");
Check(first.Contains("DOWN 500 KiB/s   UP 2.00 MiB/s"),"Network rates have correct binary units");
var secondFrame=new RtssOverlay.FrameStats(60,16,4500,3000,90);
string second=display.BuildPlain(settings,null,game,sample,secondFrame,now.AddSeconds(1.5));
Check(second.Contains("AVG 84.0"),"210 frames / 2.5 seconds is 84 AVG, rather than an average of displayed numbers");
Check(display.BuildPlain(settings,null,game,sample,secondFrame,now.AddSeconds(2)).Contains("AVG 84.0"),"Repainting does not change the average");
string expired=display.BuildPlain(settings,null,game,sample,secondFrame,now.AddSeconds(10));
Check(expired.Contains("FPS   --    AVG --")&&expired.Contains("CPU   --%")&&expired.Contains("CORE -- MHz"),"Frozen frame counters and stale hardware are not displayed as current");
Check(!expired.Contains("999"),"A fallback from another frame source cannot silently replace RTSS");
var otherGame=new GameCurrent{Pid=77,FpsAvailable=true,FpsCurrent=777};
Check(display.BuildPlain(settings,null,otherGame,sample,frames,now).Contains("FPS   --")&&!display.BuildPlain(settings,null,otherGame,sample,frames,now).Contains("777"),"Changing target resets frame history and rejects unverified fallback values");
var oldGpu=new Sample{TimestampUtc=now.Ticks,GpuTimestampUtc=now.AddSeconds(-30).Ticks,CpuPercent=37,GpuClockMhz=2500,GpuTempC=70};
var oldText=new OverlayText().BuildPlain(settings,null,game,oldGpu,default,now);
Check(oldText.Contains("CPU   37%")&&oldText.Contains("CORE -- MHz")&&!oldText.Contains("2500"),"GPU cache age is checked separately from the rest of the snapshot");
var zero=new Sample{TimestampUtc=now.Ticks,GpuTimestampUtc=now.Ticks,CpuPercent=0,GpuPercent=0,GpuPowerW=0,GpuVramUsedGb=0,GpuVramTotalGb=8,RamUsedGb=0,RamTotalGb=32,NetDownKBps=0,NetUpKBps=0};
Check(new OverlayText().BuildPlain(settings,null,game,zero,default,now).Contains("CPU   0%"),"A genuine idle zero remains different from missing data");
sample.GpuPercent=150;sample.GpuVramUsedGb=12;
var invalidText=new OverlayText().BuildPlain(settings,null,game,sample,default,now);
Check(invalidText.Contains("GPU   --%")&&invalidText.Contains("VRAM  -- / -- GiB"),"Impossible utilization and used-memory pairs are rejected");sample.GpuPercent=80;sample.GpuVramUsedGb=4;
settings.OverlayShowFps=false;settings.OverlayShowFrameTime=false;settings.OverlayShowNetwork=false;
var customDisplay=new OverlayText();customDisplay.ObserveFrames(game,new RtssOverlay.FrameStats(120,8,2000,1000,120),now.AddSeconds(-1));
var custom=customDisplay.Build(settings,null,game,sample,frames,now);
Check(!custom.Contains("FPS ")&&custom.Contains("AVG  ")&&!custom.Contains("FRAME")&&!custom.Contains("NET "),"FPS, AVG, frame time and network switches stay independent");
settings.OverlayColor="55e6ce";settings.OverlayTextScale=140;settings.OverlayLayout="Compact";
custom=customDisplay.Build(settings,null,game,sample,frames,now);
Check(custom.StartsWith("<C=55E6CE><S=105>BURMALK  RTSS<S=140>")&&custom.EndsWith("<S><C>"),"RTSS color/size tags remain scoped and branding stays small");
Check(!custom.Contains('\n')&&custom.Contains("  |  ")&&custom.Contains("<C=A3E635>GPU"),"Compact format retains colored sensor labels");
settings.OverlaySensorColors=false;
Check(!customDisplay.Build(settings,null,game,sample,frames,now).Contains("<C=A3E635>"),"Single-color customization is retained");
settings.OverlayColor="bad<tag>";settings.OverlayTextScale=int.MaxValue;
Check(customDisplay.Build(settings,null,game,sample,frames,now).StartsWith("<C=FFFFFF><S=135>"),"Unsafe color and size are bounded");
settings.OverlayShowAverage=false;settings.OverlayShowCpu=false;settings.OverlayShowGpu=false;settings.OverlayShowRam=false;settings.OverlayShowVram=false;
Check(customDisplay.BuildPlain(settings,null,game,sample,frames,now)=="BURMALK  RTSS","Empty selection retains application identity");
var profile=new GameProfile{ShowCpu=true,ShowGpu=false,ShowRam=false,ShowVram=false};
Check(customDisplay.BuildPlain(settings,profile,game,sample,frames,now).Contains("CPU "),"Per-game sensor choices still apply");
v.Write(64+276,0u);v.Write(64+280,8000u);
Check(!RtssOverlay.ReadFrames(v,42).Available,"A last-frame duration alone cannot invent interval FPS");
v.Write(64+268,uint.MaxValue-295);v.Write(64+272,704u);v.Write(64+276,120u);
Check(Math.Abs(RtssOverlay.ReadFrames(v,42).Fps-120)<.001,"RTSS DWORD time rollover is handled correctly");
var slowDisplay=new OverlayText();var slowSettings=new AppSettings();
slowDisplay.ObserveFrames(game,new RtssOverlay.FrameStats(.5,1500,3000,1000,1),now.AddSeconds(-2));
Check(slowDisplay.BuildPlain(slowSettings,null,game,sample,new RtssOverlay.FrameStats(.5,1500,5000,3000,1),now).Contains("FPS   0.5"),"A real sub-one FPS rate is not rounded to a misleading zero");
Console.WriteLine($"PASS: {checks} RTSS accuracy and rendering checks using an isolated anonymous mapping.");
