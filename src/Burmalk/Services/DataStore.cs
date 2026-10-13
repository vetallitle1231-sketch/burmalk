using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using System.Threading;
using Burmalk.Models;

namespace Burmalk.Services
{
    /// <summary>
    /// Owns %LocalAppData%\Burmalk: settings.json, sessions.json, profiles.json.
    /// All files are pretty-printed; every read/write is exception-guarded.
    /// </summary>
    public class DataStore
    {
        private readonly object _gate = new object();
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true, IncludeFields = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public AppSettings Settings { get; private set; } = new AppSettings();
        public PerformanceRecord Performance { get; private set; }
        public PerformanceRecord Benchmarks { get; private set; }
        public List<DiskMeasurement> DiskMeasurements { get; private set; } = new List<DiskMeasurement>();
        public List<GameSession> Sessions { get; private set; } = new List<GameSession>();
        public Dictionary<string, GameProfile> Profiles { get; private set; } = new Dictionary<string, GameProfile>();

        public DataStore(string rootDir = null)
        {
            RootDir = rootDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Burmalk");
        }

        public string RootDir { get; }
        private string SettingsPath { get { return Path.Combine(RootDir, "settings.json"); } }
        private string SessionsPath { get { return Path.Combine(RootDir, "sessions.json"); } }
        private string ProfilesPath { get { return Path.Combine(RootDir, "profiles.json"); } }

        public void Load()
        {
            lock (_gate)
            {
                try { Directory.CreateDirectory(RootDir); } catch { }
                // Import previous local data once; never overwrite or remove user files.
                if (RootDir == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Burmalk"))
                {
                    string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcDashboard");
                    foreach (string name in new[] { "settings.json", "sessions.json", "profiles.json" })
                    {
                        string destination=Path.Combine(RootDir,name), source=Path.Combine(legacy,name);
                        try { if(!File.Exists(destination)&&File.Exists(source)) File.Copy(source,destination,false); }
                        catch(Exception ex) { ErrorLog.Write("Previous data import: "+ex.Message); }
                    }
                }
                Settings = ReadJson<AppSettings>(SettingsPath) ?? new AppSettings();
                Performance = ReadJson<PerformanceRecord>(Path.Combine(RootDir, "performance.json"));
                Benchmarks = ReadJson<PerformanceRecord>(Path.Combine(RootDir, "benchmarks.json"));
                DiskMeasurements = ReadJson<List<DiskMeasurement>>(Path.Combine(RootDir, "disk-tests.json")) ?? new List<DiskMeasurement>();
                Settings.DashboardVisible ??= new AppSettings().DashboardVisible;
                Settings.DashboardLayout ??= new AppSettings().DashboardLayout;
                Settings.DashboardPositions ??= new Dictionary<string, DashboardPosition>();
                foreach (var key in Settings.DashboardPositions.Keys.ToArray())
                {
                    var position = Settings.DashboardPositions[key];
                    if (position == null || !double.IsFinite(position.X) || !double.IsFinite(position.Y)) Settings.DashboardPositions.Remove(key);
                    else { position.X = Math.Clamp(position.X, 0, 10000); position.Y = Math.Clamp(position.Y, 0, 10000); }
                }
                Settings.GameLibraryPaths ??= Array.Empty<string>();
                Settings.PcModel ??= new PcModelOptions(); Settings.PcModel.Normalize();
                if (!new[] { "Auto", "Desktop", "Laptop" }.Contains(Settings.PcModelChassis)) Settings.PcModelChassis = "Auto";
                if (!new[] { "Auto", "Air", "Aio" }.Contains(Settings.PcModelCooling)) Settings.PcModelCooling = "Auto";
                Settings.PcWidgetYaw = double.IsFinite(Settings.PcWidgetYaw) ? Settings.PcWidgetYaw % 360 : 32;
                Settings.PcWidgetPitch = double.IsFinite(Settings.PcWidgetPitch) ? Math.Clamp(Settings.PcWidgetPitch, -65, 65) : 15;
                Settings.PcWidgetDistance = double.IsFinite(Settings.PcWidgetDistance) ? Math.Clamp(Settings.PcWidgetDistance, 7, 22) : 18;
                Sessions = ReadJson<List<GameSession>>(SessionsPath) ?? new List<GameSession>();
                Profiles = ReadJson<Dictionary<string, GameProfile>>(ProfilesPath) ?? new Dictionary<string, GameProfile>();
                Profiles = new Dictionary<string, GameProfile>(Profiles, StringComparer.OrdinalIgnoreCase);
                Settings.UpdateIntervalMs = Math.Clamp(Settings.UpdateIntervalMs, 300, 10000);
                Settings.GraphHistorySeconds = Math.Clamp(Settings.GraphHistorySeconds, 10, 3600);
                Settings.OverlayRefreshMs = Math.Clamp(Settings.OverlayRefreshMs, 300, 10000);
                if(!Appearance.Themes.Contains(Settings.Theme))Settings.Theme="Burmalk";
                Settings.BackgroundDim=double.IsFinite(Settings.BackgroundDim)?Math.Clamp(Settings.BackgroundDim,0.1,0.9):0.5;
            }
        }

        /// <summary>Persist everything (used on exit / after edits).</summary>
        public void Save()
        {
            lock (_gate)
            {
                WriteJson(SettingsPath, Settings);
                WriteJson(SessionsPath, Sessions);
                WriteJson(ProfilesPath, Profiles);
            }
        }

        public void AddSession(GameSession session)
        {
            lock (_gate)
            {
                Sessions.Add(session);
                // keep the list bounded so the file does not grow unbounded
                if (Sessions.Count > 2000)
                    Sessions.RemoveRange(0, Sessions.Count - 2000);
                WriteJson(SessionsPath, Sessions);
            }
        }

        public void SavePerformance(PerformanceRecord record)
        {
            lock (_gate)
            {
                if (Performance != null && Performance.TestedUtc != record.TestedUtc)
                    WriteJson(Path.Combine(RootDir, "rank-history", Performance.TestedUtc.ToString("yyyyMMdd-HHmmss") + ".json"), Performance);
                Performance = record;
                Benchmarks = record;
                WriteJson(Path.Combine(RootDir, "performance.json"), record);
                WriteJson(Path.Combine(RootDir, "benchmarks.json"), record);
            }
        }

        public void SaveDiskMeasurement(DiskMeasurement measurement)
        {
            lock (_gate)
            {
                DiskMeasurements = DiskMeasurements.Where(d => !string.Equals(d.Root, measurement.Root, StringComparison.OrdinalIgnoreCase)).Append(measurement).ToList();
                WriteJson(Path.Combine(RootDir, "disk-tests.json"), DiskMeasurements);
            }
        }

        public void SaveComponentBenchmark(PerformanceRecord component, bool gpu)
        {
            lock (_gate)
            {
                if (Benchmarks == null || Benchmarks.MethodVersion != component.MethodVersion || Benchmarks.CpuModel != component.CpuModel || Benchmarks.GpuModel != component.GpuModel)
                    Benchmarks = new PerformanceRecord { MethodVersion = component.MethodVersion, CpuModel = component.CpuModel, GpuModel = component.GpuModel, RamGb = component.RamGb };
                else Benchmarks = JsonSerializer.Deserialize<PerformanceRecord>(JsonSerializer.Serialize(Benchmarks, JsonOpts), JsonOpts);
                if (gpu)
                {
                    Benchmarks.GpuEngine = component.GpuEngine; Benchmarks.GpuRenderer = component.GpuRenderer; Benchmarks.GpuFps = component.GpuFps; Benchmarks.GpuCompleted = component.GpuCompleted; Benchmarks.GpuSeconds = component.GpuSeconds; Benchmarks.RenderTier = component.RenderTier;
                }
                else
                {
                    Benchmarks.CpuWorkers = component.CpuWorkers; Benchmarks.CpuMillionOps = component.CpuMillionOps; Benchmarks.CpuCompleted = component.CpuCompleted; Benchmarks.CpuSeconds = component.CpuSeconds;
                    Benchmarks.RamMegabytesPerSecond = component.RamMegabytesPerSecond; Benchmarks.RamCompleted = component.RamCompleted; Benchmarks.MemoryPassed = component.MemoryPassed; Benchmarks.MemoryBytesVerified = component.MemoryBytesVerified;
                }
                Benchmarks.TestedUtc = component.TestedUtc;
                WriteJson(Path.Combine(RootDir, "benchmarks.json"), Benchmarks);
            }
        }

        public IEnumerable<GameSession> SessionsForGame(string gameName)
        {
            lock (_gate)
            {
                return Sessions
                    .Where(s => s.GameName == gameName)
                    .OrderByDescending(s => s.StartedUtc)
                    .ToList();
            }
        }
        public List<GameSession> SessionSnapshot()
        {
            lock (_gate) { return Sessions.ToList(); }
        }

        public GameProfile GetProfile(string gameName)
        {
            lock (_gate)
            {
                GameProfile p;
                if (Profiles.TryGetValue(gameName, out p)) return p;
                return null;
            }
        }

        public void SaveProfile(GameProfile profile)
        {
            lock (_gate)
            {
                Profiles[profile.GameName] = profile;
                WriteJson(ProfilesPath, Profiles);
            }
        }

        public IEnumerable<GameProfile> AllProfiles
        {
            get { lock (_gate) { return Profiles.Values.ToList(); } }
        }

        private static T ReadJson<T>(string path)
        {
            try
            {
                if (!File.Exists(path)) return default(T);
                string txt = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(txt)) return default(T);
                return JsonSerializer.Deserialize<T>(txt, JsonOpts);
            }
            catch (Exception ex)
            {
                ErrorLog.Write("DataStore.ReadJson " + path + ": " + ex.Message);
                try { File.Copy(path, path+".invalid-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss"), false); } catch { }
                return default(T);
            }
        }

        private static void WriteJson<T>(string path, T value)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(value, JsonOpts));
                File.Move(tmp, path, true);
            }
            catch (Exception ex)
            {
                ErrorLog.Write("DataStore.WriteJson " + path + ": " + ex.Message);
            }
        }
    }
}


