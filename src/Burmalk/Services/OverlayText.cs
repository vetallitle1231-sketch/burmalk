using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Burmalk.Models;

namespace Burmalk.Services;

internal sealed class OverlayText
{
    private int _pid;
    private DateTime _session;
    private FpsTimeline _fps = new();
    private readonly record struct Row(string Label, string Color, string Value);

    public void ObserveFrames(GameCurrent game, RtssOverlay.FrameStats frame, DateTime now)
    {
        int pid=game?.Pid??0;
        DateTime session=game?.StartedUtc??default;
        if(pid!=_pid||session!=_session){_pid=pid;_session=session;_fps=new FpsTimeline();}
        if(pid>0&&frame.Available)_fps.AcceptRtss(frame.Time0,frame.Stamp,frame.Frames,now);
    }

    public string Build(AppSettings settings, GameProfile profile, GameCurrent game, Sample sample, RtssOverlay.FrameStats frame, DateTime now)
    {
        var rows = Rows(settings, profile, game, sample, frame, now);
        string color = Regex.IsMatch(settings.OverlayColor ?? "", "^[0-9A-Fa-f]{6}$") ? settings.OverlayColor.ToUpperInvariant() : "FFFFFF";
        int size = Math.Clamp(settings.OverlayTextScale, 60, 180);
        string separator = settings.OverlayLayout == "Compact" ? "  |  " : "\n";
        var text = new StringBuilder("<C=" + color + "><S=" + (int)(size * .75) + ">BURMALK  RTSS<S=" + size + ">");
        foreach (var row in rows)
        {
            text.Append(separator).Append("<C=").Append(settings.OverlaySensorColors ? row.Color : color).Append('>')
                .Append(row.Label.PadRight(5)).Append("<C=").Append(color).Append("> ").Append(row.Value);
        }
        return text.Append("<S><C>").ToString();
    }

    public string BuildPlain(AppSettings settings, GameProfile profile, GameCurrent game, Sample sample, RtssOverlay.FrameStats frame, DateTime now)
    {
        var text = new StringBuilder("BURMALK  RTSS");
        foreach (var row in Rows(settings, profile, game, sample, frame, now))
            text.Append(settings.OverlayLayout == "Compact" ? "  |  " : "\n").Append(row.Label.PadRight(5)).Append(' ').Append(row.Value);
        return text.ToString();
    }

    private List<Row> Rows(AppSettings settings, GameProfile profile, GameCurrent game, Sample sample, RtssOverlay.FrameStats frame, DateTime now)
    {
        int pid = game?.Pid ?? 0;
        ObserveFrames(game,frame,now);
        bool framesFresh = pid > 0 && frame.Available && _fps.Source == "RTSS" && _fps.Available(now);
        double fps = framesFresh ? _fps.Current : double.NaN;
        double average = framesFresh ? _fps.Average : double.NaN;
        var rows = new List<Row>();
        if (settings.OverlayShowFps)
            rows.Add(new("FPS", "FFD166", N(fps, fps<10?"0.0":"0") + (settings.OverlayShowAverage ? "    AVG " + N(average, "0.0") : "")));
        else if (settings.OverlayShowAverage) rows.Add(new("AVG", "FFD166", N(average, "0.0") + " FPS"));
        if (settings.OverlayShowFrameTime)
            rows.Add(new("FRAME", "CBD5E1", N(framesFresh ? 1000d / fps : double.NaN, "0.00") + " ms AVG   " +
                N(framesFresh ? frame.FrameMs : double.NaN, "0.00") + " ms LAST"));

        long maxAge = Math.Clamp((long)settings.UpdateIntervalMs * 2 + 1000, 3000, 21000);
        bool fresh = sample != null && Fresh(sample.TimestampUtc, now, maxAge);
        bool gpuFresh = fresh && Fresh(sample.GpuTimestampUtc, now, Math.Max(8000, maxAge));
        sample ??= new Sample();
        if (profile?.ShowGpu ?? settings.OverlayShowGpu)
        {
            if (gpuFresh && !string.IsNullOrWhiteSpace(sample.GpuName) && sample.GpuName != "Unavailable")
                rows.Add(new("GPU", "A3E635", SafeName(sample.GpuName)));
            rows.Add(new("GPU", "A3E635", P(gpuFresh ? sample.GpuPercent : double.NaN) + "   " +
                Temperature(gpuFresh ? sample.GpuTempC : double.NaN) + "   CORE " +
                Unit(gpuFresh ? sample.GpuClockMhz : double.NaN, "MHz") + "   " + Unit(gpuFresh ? sample.GpuPowerW : double.NaN, "W")));
        }
        if (profile?.ShowCpu ?? settings.OverlayShowCpu)
        {
            string clock = (sample.CpuFrequencySource??"").Contains("Windows power", StringComparison.Ordinal) ? "CLK(OS) " : "CLK(avg) ";
            rows.Add(new("CPU", "67E8F9", P(fresh ? sample.CpuPercent : double.NaN) + "   " +
                CpuTemperatureTag(sample.CpuTemperatureSource)+Temperature(fresh ? sample.CpuTempC : double.NaN) + "   " + clock + Unit(fresh ? sample.CpuGhz * 1000 : double.NaN, "MHz")));
        }
        if (profile?.ShowRam ?? settings.OverlayShowRam)
            rows.Add(new("RAM", "C4B5FD", Memory(fresh ? sample.RamUsedGb : double.NaN, fresh ? sample.RamTotalGb : double.NaN)));
        if (profile?.ShowVram ?? settings.OverlayShowVram)
            rows.Add(new("VRAM", "C4B5FD", Memory(gpuFresh ? sample.GpuVramUsedGb : double.NaN, gpuFresh ? sample.GpuVramTotalGb : double.NaN) +
                "   MEM " + Unit(gpuFresh ? sample.GpuMemClockMhz : double.NaN, "MHz")));
        if (settings.OverlayShowNetwork)
            rows.Add(new("NET", "94A3B8", "DOWN " + Rate(fresh ? sample.NetDownKBps : double.NaN) + "   UP " + Rate(fresh ? sample.NetUpKBps : double.NaN)));
        return rows;
    }
    private static bool Fresh(long ticks, DateTime now, long maxAgeMs) => ticks > 0 && ticks <= now.Ticks && now.Ticks - ticks <= maxAgeMs * TimeSpan.TicksPerMillisecond;
    private static string N(double v, string format) => double.IsFinite(v) && v >= 0 ? v.ToString(format, CultureInfo.InvariantCulture) : "--";
    private static string Unit(double v, string unit) => N(v, "0") + " " + unit;
    private static string P(double v) => N(v >= 0 && v <= 100 ? v : double.NaN, "0") + "%";
    private static string Temperature(double v) => double.IsFinite(v) && v >= -100 && v <= 200 ? v.ToString("0", CultureInfo.InvariantCulture) + " °C" : "-- °C";
    private static string CpuTemperatureTag(string source) => (source??"") switch
    {
        var s when s.Contains("Tctl",StringComparison.OrdinalIgnoreCase)=>"TCTL ",
        var s when s.Contains("Tdie",StringComparison.OrdinalIgnoreCase)=>"TDIE ",
        var s when s.Contains("Package",StringComparison.OrdinalIgnoreCase)=>"PKG ",
        var s when s.StartsWith("Core max",StringComparison.OrdinalIgnoreCase)=>"COREMAX ",
        _=>""
    };
    private static string Memory(double used, double total)
    {
        if (double.IsFinite(total) && (total <= 0 || used > total)) used = total = double.NaN;
        return N(used, "0.00") + " / " + N(total, "0.00") + " GiB";
    }
    private static string Rate(double v) => double.IsFinite(v) && v >= 1024 ? N(v / 1024, "0.00") + " MiB/s" : N(v, "0") + " KiB/s";
    private static string SafeName(string value)
    {
        string clean=Regex.Replace(value,@"[<>\x00-\x1F]","").Trim();
        return clean[..Math.Min(64,clean.Length)];
    }
}
