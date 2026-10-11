using Burmalk.Models;
using Burmalk.Services;

long now = DateTime.UtcNow.Ticks;
Sample Normal() => new() { TimestampUtc = now, CpuTempC = 45, GpuTempC = 40, RamPercent = 35, GpuVramUsedGb = 3, GpuVramTotalGb = 12 };
StorageDrive Disk(double used = 50) => new() { Letter = "C:", TotalGb = 100, FreeGb = (long)(100 - used), UsedPercent = used };
HealthReport Rate(Sample s) => HealthAssessment.Evaluate(s, new[] { Disk() }, now);
void Check(bool pass, string name) { if (!pass) throw new Exception(name); Console.WriteLine("PASS " + name); }

Check(Rate(Normal()).Level == HealthLevel.Excellent, "Complete cool readings with ample memory and disk space are excellent");
foreach (var (value, expected) in new[] { (69.9, HealthLevel.Excellent), (70, HealthLevel.Good), (80, HealthLevel.Fair), (90, HealthLevel.Poor) })
{ var s = Normal(); s.CpuTempC = value; Check(Rate(s).Level == expected, "CPU temperature boundary " + value); }
foreach (var (value, expected) in new[] { (64.9, HealthLevel.Excellent), (65, HealthLevel.Good), (75, HealthLevel.Fair), (85, HealthLevel.Poor) })
{ var s = Normal(); s.GpuTempC = value; Check(Rate(s).Level == expected, "GPU temperature boundary " + value); }
foreach (var (value, expected) in new[] { (75, HealthLevel.Good), (90, HealthLevel.Fair), (97, HealthLevel.Poor) })
{ var s = Normal(); s.RamPercent = value; Check(Rate(s).Level == expected, "Memory pressure boundary " + value); }
foreach (var (value, expected) in new[] { (85, HealthLevel.Good), (93, HealthLevel.Fair), (98, HealthLevel.Poor) })
Check(HealthAssessment.Evaluate(Normal(), new[] { Disk(value) }, now).Level == expected, "Disk occupancy boundary " + value);
var vram = Normal(); vram.GpuVramTotalGb = 100; vram.GpuVramUsedGb = 98;
Check(Rate(vram).Level == HealthLevel.Poor, "Exhausted VRAM changes the aggregate assessment");
var missing = Normal(); missing.CpuTempC = double.NaN;
Check(Rate(missing).Level == HealthLevel.Good && Rate(missing).Partial && Rate(missing).Missing.Contains("Температура CPU"), "Missing essential sensor prevents a complete green assessment");
missing.GpuTempC = 90;
Check(Rate(missing).Level == HealthLevel.Poor, "Missing sensors never hide a critical available reading");
var load = Normal(); load.CpuPercent = 100; load.GpuPercent = 100; load.GpuPowerW = 500;
Check(HealthAssessment.Evaluate(load, new[] { Disk() }, now, false, double.NaN, 0).Level == HealthLevel.Excellent, "Gaming load, offline network and GPU zero-fan mode alone are not faults");
var stale = Normal(); stale.TimestampUtc = now - TimeSpan.TicksPerSecond * 31; stale.CpuTempC = 99;
Check(HealthAssessment.Evaluate(stale, Array.Empty<StorageDrive>(), now).Level == null, "Stale thermal data does not produce a current rating");
var empty = HealthAssessment.Evaluate(new Sample { TimestampUtc = now }, Array.Empty<StorageDrive>(), now);
Check(empty.Level == null && empty.AssessedCount == 0, "No readings means no fabricated health grade");
var invalid = Normal(); invalid.CpuTempC = double.PositiveInfinity; invalid.GpuTempC = -3; invalid.RamPercent = 130; invalid.GpuVramUsedGb = 50;
Check(HealthAssessment.Evaluate(invalid, Array.Empty<StorageDrive>(), now).Level == null, "Invalid and non-finite inputs are excluded");
Check(HealthAssessment.Evaluate(Normal(), new[] { Disk(), Disk(99) }, now).Level == HealthLevel.Poor, "Worst disk matters even when another disk has plenty of room");
var critical = Normal(); critical.CpuTempC = 92;
Check(Rate(critical).Reason.Contains("Температура CPU") && Rate(critical).Reason.Contains("92"), "The aggregate grade explains its limiting measurement");
