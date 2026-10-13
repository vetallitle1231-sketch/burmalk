using System;

namespace Burmalk.Services;
internal sealed class FpsTimeline
{
    private uint _stamp;
    public DateTime LastUtc { get; private set; }
    public string Source { get; private set; } = "";
    public int Count { get; private set; }
    public double Current { get; private set; }
    public double Average { get; private set; }
    public double Minimum { get; private set; } = double.PositiveInfinity;
    public double Maximum { get; private set; }
    public bool Available(DateTime now) => Count > 0 && now >= LastUtc && (now - LastUtc).TotalSeconds <= 3;
    public bool AcceptRtss(double fps, uint stamp, DateTime now)
    { if (stamp == 0 || stamp == _stamp || !Valid(fps)) return false; _stamp = stamp; return Accept(fps, "RTSS", now); }
    public bool Accept(double fps, string source, DateTime now)
    {
        if (!Valid(fps) || (source != "RTSS" && Source == "RTSS" && Available(now))) return false;
        LastUtc = now; Source = source; Current = fps; Count++; Average += (fps - Average) / Count; Minimum = Math.Min(Minimum, fps); Maximum = Math.Max(Maximum, fps); return true;
    }
    private static bool Valid(double n) => double.IsFinite(n) && n > 0 && n < 10000;
}
