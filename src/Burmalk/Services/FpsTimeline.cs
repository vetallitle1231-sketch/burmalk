using System;

namespace Burmalk.Services;
internal sealed class FpsTimeline
{
    private uint _stamp, _acceptedEnd;
    private bool _seenRtss, _hasAcceptedEnd;
    private DateTime _firstRtssRead;
    private double _frames, _milliseconds;
    public DateTime LastUtc { get; private set; }
    public string Source { get; private set; } = "";
    public int Count { get; private set; }
    public double Current { get; private set; } = double.NaN;
    public double Average { get; private set; } = double.NaN;
    public double Minimum { get; private set; } = double.PositiveInfinity;
    public double Maximum { get; private set; }
    public bool Available(DateTime now) => Count > 0 && now >= LastUtc && (now - LastUtc).TotalSeconds <= 3;
    public bool WaitingForRtss(DateTime now) => _seenRtss && Count == 0 && now >= _firstRtssRead && now - _firstRtssRead <= TimeSpan.FromSeconds(3);
    // Count complete, distinct measurement intervals, never repeated overlay refreshes.
    public bool AcceptRtss(uint start, uint end, uint frames, DateTime now)
    {
        uint elapsed = unchecked(end - start);
        if (start == 0 || end == 0 || elapsed == 0 || elapsed > 60000 || frames == 0) return false;
        double fps = 1000d * frames / elapsed;
        if (!Valid(fps)) return false;
        // An existing mapping may belong to a paused/exited game. Prove it advances first.
        if (!_seenRtss) { _seenRtss = true; _stamp = end; _firstRtssRead=now; return false; }
        if (end == _stamp) return false;
        if (unchecked(end - _stamp) > int.MaxValue)
        { _stamp = end; ResetValues(); _hasAcceptedEnd = false; _firstRtssRead=now; return false; }
        _stamp = end;
        if (_hasAcceptedEnd && unchecked(start - _acceptedEnd) > int.MaxValue) return false;
        if (Source != "RTSS") ResetValues();
        _hasAcceptedEnd = true; _acceptedEnd = end;
        _frames += frames; _milliseconds += elapsed;
        LastUtc = now; Source = "RTSS"; Current = fps; Count++;
        Average = 1000d * _frames / _milliseconds;
        Minimum = Math.Min(Minimum, fps); Maximum = Math.Max(Maximum, fps);
        return true;
    }
    public bool Accept(double fps, string source, DateTime now)
    {
        if (!Valid(fps) || (source != "RTSS" && Source == "RTSS" && Available(now))) return false;
        if (Source != source) ResetValues();
        LastUtc = now; Source = source; Current = fps; Count++;
        Average = Count == 1 ? fps : Average + (fps - Average) / Count;
        Minimum = Math.Min(Minimum, fps); Maximum = Math.Max(Maximum, fps); return true;
    }
    private void ResetValues()
    {
        Count = 0; _frames = _milliseconds = 0; Current = Average = double.NaN;
        Minimum = double.PositiveInfinity; Maximum = 0; LastUtc = DateTime.MinValue;
    }
    private static bool Valid(double n) => double.IsFinite(n) && n > 0 && n < 100000;
}
