using System;
using System.Collections.Generic;
using System.Globalization;
using Burmalk.Models;

namespace Burmalk.Services;

internal sealed class OverlayText
{
    private int _pid;
    private double _fpsTotal;
    private int _fpsCount;
    private DateTime _lastFpsSample;
    public string Build(AppSettings settings,GameProfile profile,GameCurrent game,Sample sample,RtssOverlay.FrameStats frame,DateTime now)
    {
        string plain = BuildPlain(settings, profile, game, sample, frame, now);
        string color = System.Text.RegularExpressions.Regex.IsMatch(settings.OverlayColor ?? "", "^[0-9A-Fa-f]{6}$") ? settings.OverlayColor.ToUpperInvariant() : "FFFFFF";
        // RTSS S uses an absolute percentage (100 is normal), not a delta.
        int size = Math.Clamp(settings.OverlayTextScale, 60, 180);
        return "<C=" + color + "><S=" + (int)(size * .75) + ">BURMALK<S=" + size + ">" + plain.Substring(7) + "<S><C>";
    }
    public string BuildPlain(AppSettings settings,GameProfile profile,GameCurrent game,Sample sample,RtssOverlay.FrameStats frame,DateTime now)
    {
        if(game.Pid!=_pid){_pid=game.Pid;_fpsTotal=0;_fpsCount=0;_lastFpsSample=DateTime.MinValue;}
        var lines=new List<string>{"BURMALK"};
        double fps=frame.Available?frame.Fps:game.FpsAvailable?game.FpsCurrent:double.NaN;
        if(double.IsFinite(fps)&&fps>0&&now-_lastFpsSample>=TimeSpan.FromMilliseconds(800))
        {
            _fpsTotal+=fps;_fpsCount++;_lastFpsSample=now;
        }
        if(double.IsFinite(fps)&&fps>0&&(settings.OverlayShowFps||settings.OverlayShowAverage))
        {
            double average=_fpsCount>0?_fpsTotal/_fpsCount:game.FpsAvailable?game.FpsAvg:double.NaN;
            string value = settings.OverlayShowFps ? "FPS " + Number(fps,"0") : "";
            if (settings.OverlayShowAverage && double.IsFinite(average)) value += (value.Length > 0 ? "  " : "") + "AVG " + Number(average,"0");
            if (value.Length > 0) lines.Add(value);
        }
        if(settings.OverlayShowFrameTime&&double.IsFinite(frame.FrameMs)&&frame.FrameMs>0)lines.Add("FRAME "+Number(frame.FrameMs,"0.0")+" ms");
        if(profile?.ShowCpu??settings.OverlayShowCpu)
            lines.Add("CPU "+Percent(sample.CpuPercent)+Optional(sample.CpuTempC," C", "0")+Optional(sample.CpuGhz*1000," MHz","0"));
        if(profile?.ShowGpu??settings.OverlayShowGpu)
            lines.Add("GPU "+Percent(sample.GpuPercent)+Optional(sample.GpuTempC," C","0")+Optional(sample.GpuClockMhz," MHz","0")+Optional(sample.GpuPowerW," W","0"));
        if(profile?.ShowVram??settings.OverlayShowVram)
            lines.Add("VRAM "+Gb(sample.GpuVramUsedGb)+(double.IsFinite(sample.GpuVramTotalGb)?" / "+Gb(sample.GpuVramTotalGb):""));
        if(profile?.ShowRam??settings.OverlayShowRam)lines.Add("RAM "+Gb(sample.RamUsedGb));
        if(settings.OverlayShowNetwork&&(double.IsFinite(sample.NetDownKBps)||double.IsFinite(sample.NetUpKBps)))
            lines.Add("NET DOWN "+Rate(sample.NetDownKBps)+"  UP "+Rate(sample.NetUpKBps));
        return string.Join(settings.OverlayLayout == "Compact" ? "  |  " : "\n",lines);
    }
    private static string Number(double v,string format)=>v.ToString(format,CultureInfo.InvariantCulture);
    private static string Percent(double v)=>double.IsFinite(v)?Number(v,"0")+"%":"--";
    private static string Optional(double v,string suffix,string format)=>double.IsFinite(v)?"  "+Number(v,format)+suffix:"";
    private static string Gb(double v)=>double.IsFinite(v)?Number(v,"0.0")+" GiB":"--";
    private static string Rate(double v)=>double.IsFinite(v)?Number(v,"0")+" KB/s":"--";
}
