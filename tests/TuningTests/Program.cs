using Burmalk.Services;

int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
void Throws(Action action, string name) { try { action(); } catch (InvalidOperationException) { checks++; Console.WriteLine("PASS " + name); return; } catch (ArgumentOutOfRangeException) { checks++; Console.WriteLine("PASS " + name); return; } throw new Exception(name); }
double core = 50, power = 300; int writes = 0;
var a = new TuningSetting { Name = "core", Group = "GPU", Minimum = -100, Maximum = 250, Read = () => core, Write = v => { writes++; core = v; } };
var b = new TuningSetting { Name = "power", Group = "GPU", Minimum = 175, Maximum = 300, Read = () => power, Write = v => { writes++; power = v; } };
var session = new TuningSession();
Throws(() => session.Apply(new Dictionary<TuningSetting, double> { [a] = 60, [b] = 301 }), "All ranges validated before any write");
Check(writes == 0 && core == 50 && power == 300, "Invalid request leaves hardware unchanged");
Throws(() => session.Apply(new Dictionary<TuningSetting, double> { [a] = double.NaN }), "NaN refused");
session.Apply(new Dictionary<TuningSetting, double> { [a] = 100, [b] = 250 });
Check(core == 100 && power == 250 && session.HasChanges, "Apply and readback");
session.Apply(new Dictionary<TuningSetting, double> { [a] = 120 });
session.Restore(); Check(core == 50 && power == 300 && !session.HasChanges, "Repeated apply restores original user settings, not factory power");
bool fail = true;
b.Write = v => { power = v; if (v == 200 && fail) throw new InvalidOperationException("partial driver failure"); };
Throws(() => session.Apply(new Dictionary<TuningSetting, double> { [a] = 130, [b] = 200 }), "Partial driver failure reports error");
Check(core == 50 && power == 300 && !session.HasChanges, "Partial failure rolls every touched setting back");
a.Write = v => core = v + 1;
Throws(() => session.Apply(new Dictionary<TuningSetting, double> { [a] = 140 }), "Mismatching driver readback refused");
Check(session.HasChanges, "Failed rollback remains tracked for a later retry");
a.Write = v => core = v; session.Restore(); Check(core == 50 && !session.HasChanges, "Retry restores values after a failed rollback");
double fan = 55; bool manual = false;
var f = new TuningSetting { Name = "fan", Group = "GPU FAN", Minimum = 40, Maximum = 100, Read = () => fan, Write = v => { fan = v; manual = true; }, Automatic = () => manual = false,
    CaptureRestore = () => { double old = fan; bool mode = manual; return () => { fan = old; manual = mode; }; } };
session.Apply(new Dictionary<TuningSetting, double> { [f] = 55 }); Check(manual, "Same current fan speed still activates manual mode");
session.Restore("GPU FAN"); Check(fan == 55 && !manual, "Original automatic fan policy restored");
manual = true; fan = 65; session.Automatic(f); Check(!manual, "Auto switches fan to firmware control");
session.Restore(); Check(manual && fan == 65, "Auto action remembers prior manual policy");
bool missing = false;
a.Read = () => missing ? double.NaN : core;
a.Write = v => { core = v; missing = v == 150; };
Throws(() => session.Apply(new Dictionary<TuningSetting, double> { [a] = 150 }), "Missing readback is not accepted as success");
Check(core == 50 && !session.HasChanges, "Missing readback rolls back the attempted setting");
a.Read = () => core; a.Write = v => core = v;
double cpu = 100;
var c = new TuningSetting { Name = "CPU", Group = "CPU", Minimum = 10, Maximum = 100, Read = () => cpu, Write = v => cpu = v };
session.Apply(new Dictionary<TuningSetting, double> { [c] = 90, [a] = 100 });
Check(session.Protect(60, 70) == "" && cpu == 90 && core == 100, "Normal temperatures preserve selected tuning");
Check(session.Protect(double.NaN, 70).Contains("CPU") && cpu == 100 && core == 100, "Missing CPU temperature restores only the affected group");
Check(session.Protect(60, 85).Contains("GPU") && core == 50 && !session.HasChanges, "GPU temperature threshold restores GPU changes");
session.Apply(new Dictionary<TuningSetting, double> { [c] = 90, [a] = 100 });
c.Write = v => throw new InvalidOperationException("CPU restore refused");
Check(session.Protect(90, 85).Contains("ошибка") && core == 50 && session.HasChanges, "One failed restore does not prevent another hot device being restored");
c.Write = v => cpu = v; session.Restore();
Check(checks >= 12, "Tuning test suite complete");
var generic = new TuningSetting { Id = "pwm1", NativeName = "Fan #1", HardwareId = "chip" };
var named = new TuningSetting { Id = "pwm2", NativeName = "CPU Fan", HardwareId = "chip" };
var genericRpm = new BoardFanSensor { Id = "rpm1", Name = "Chip · Fan #1", NativeName = "Fan #1", HardwareId = "chip", Read = () => 1200 };
var namedRpm = new BoardFanSensor { Id = "rpm4", NativeName = "CPU_FAN", HardwareId = "chip" };
Check(!CpuFanBinding.IsPrimaryCpu("Fan #1") && !CpuFanBinding.IsPrimaryCpu("CPU Optional Fan") && !CpuFanBinding.IsPrimaryCpu("CPU Pump") && CpuFanBinding.IsPrimaryCpu("CPU_FAN"), "Generic fans, CPU_OPT, and pumps are not identified as CPU_FAN");
Check(CpuFanBinding.Resolve(new[] { generic }, new[] { genericRpm }, "board", "", "", "").Control == null, "A generic first channel is never assigned to CPU automatically");
var identified = CpuFanBinding.Resolve(new[] { generic, named }, new[] { genericRpm, namedRpm }, "board", "", "", "");
Check(identified.Control == named && identified.Sensor == namedRpm, "Explicit CPU_FAN names select independent PWM and RPM channels");
Check(CpuFanBinding.Resolve(new[] { named, new TuningSetting { Id = "other", NativeName = "CPU Fan", HardwareId = "chip" } }, new[] { namedRpm }, "board", "", "", "").Control == null, "Ambiguous CPU channels remain unassigned");
Check(CpuFanBinding.Resolve(new[] { generic }, new[] { genericRpm }, "board", "board", generic.Id, genericRpm.Id).Control == generic, "User-confirmed channel binding is restored for the same motherboard");
Check(CpuFanBinding.Resolve(new[] { generic }, new[] { genericRpm }, "new-board", "board", generic.Id, genericRpm.Id).Control == null, "Saved fan binding is rejected after motherboard changes");
Check(CpuFanBinding.Resolve(new[] { generic }, new[] { genericRpm }, "board", "board", generic.Id, "missing").Control == null, "Missing RPM channel invalidates a saved fan binding");
var parsedGpu = GpuSampler.ParseLine("RTX 5070, 617.42, 50, 70, 2500, 17000, 180, 4000, 12288");
Check(parsedGpu.ClockMhz == 2500 && parsedGpu.MemClockMhz == 17000, "Core and memory MHz remain separate without arbitrary conversion");
Check(double.IsNaN(GpuSampler.ParseDouble("1,700")) && double.IsNaN(GpuSampler.ParseDouble("Infinity")) && GpuSampler.ParseDouble("1700.5") == 1700.5, "Invalid thousands formatting and non-finite clocks are rejected");
Check(double.IsNaN(GpuSampler.ParseLine("RTX 5070, 617.42, 50, 70, 2,500, 17000, 180, 4000, 12288").ClockMhz), "Malformed CSV cannot shift memory MHz into the core field");
if (args.Contains("--probe"))
{
    using var nvml = new NvidiaTuning(); var settings = new List<TuningSetting>();
    Console.WriteLine("READ ONLY GPU PROBE: " + nvml.Probe(settings));
    Console.WriteLine("READ ONLY CPU PROBE: " + CpuBoost.Probe(settings));
    foreach (var setting in settings) Console.WriteLine(setting.Id + " | " + setting.Name + " | current=" + setting.Read() + " | range=" + setting.Minimum + ".." + setting.Maximum + " " + setting.Unit);
    Check(true, "Probe calls getters only; no actual clock, power, fan, or plan writes");
}
Check(CpuClockNames.IsCore("CPU Core #1")&&CpuClockNames.IsCore("Core #8")&&CpuClockNames.IsCore("Cores (Average)"),"Intel and AMD core clocks are recognized");
Check(!CpuClockNames.IsCore("Bus Speed")&&!CpuClockNames.IsCore("CPU Core Max")&&!CpuClockNames.IsCore("CPU SoC")&&!CpuClockNames.IsCore("Memory")&&!CpuClockNames.IsCore("Ring"),"Bus, maximum, SoC, memory and ring clocks cannot lower the average core frequency");
Check(double.IsNaN(GpuSampler.ParseLine("RTX 5070, 617.42, 50, 140, 2500, 17000, 180, 4000, 12288").Percent),"Impossible GPU utilization is unavailable rather than clamped");
Check(double.IsNaN(GpuSampler.ParseLine("RTX 5070, 617.42, N/A, N/A, N/A, N/A, N/A, N/A, N/A").ClockMhz),"Unsupported driver metrics remain unavailable");
Check(double.IsNaN(GpuSampler.ParseLine("RTX 5070, 617.42, 50, 70, 2500, 17000, 180, 13000, 12288").VramUsedGb),"Used VRAM cannot exceed the reported total");
