using Burmalk.Services;
void Check(bool yes, string text) { if (!yes) throw new Exception(text); Console.WriteLine("PASS " + text); }
var t = new FpsTimeline(); var now = DateTime.UtcNow;
Check(!t.Available(now), "No invented FPS before frames arrive");
Check(t.AcceptRtss(120, 42, now) && t.Available(now), "RTSS frames immediately populate the game widget");
Check(!t.AcceptRtss(120, 42, now.AddSeconds(1)) && t.Count == 1, "Unchanged shared-memory frame is not counted twice");
Check(!t.Available(now.AddSeconds(4)), "Stale FPS disappears after three seconds");
Check(t.Accept(90, "PresentMon", now.AddSeconds(4)), "PresentMon can recover when RTSS stops updating");
Check(t.AcceptRtss(150, 43, now.AddSeconds(5)), "RTSS automatically resumes");
Check(!t.Accept(200, "PresentMon", now.AddSeconds(6)), "Late fallback result cannot overwrite fresh RTSS");
Check(t.Count == 3 && t.Average == 120 && t.Minimum == 90 && t.Maximum == 150, "Average and extrema only use accepted samples");
Check(!t.Accept(double.NaN, "RTSS", now.AddSeconds(7)), "Invalid frame rate is rejected");
Check(!new FpsTimeline().Available(now), "A new game starts with a clean frame history");
