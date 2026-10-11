using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Burmalk.Models;

namespace Burmalk.Services
{
    internal sealed class LibraryGame
    {
        public string Name;
        public string Executable;
        public string IconPath;
    }

    internal static class GameLibraryService
    {
        public static Task<List<LibraryGame>> ScanAsync(IEnumerable<string> savedPaths) => Task.Run(() =>
        {
            var games = new Dictionary<string, LibraryGame>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in savedPaths ?? Array.Empty<string>()) AddExe(games, path, null);
            foreach (string root in SteamRoots()) ScanSteam(root, games);
            foreach (string root in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
            })
            {
                if (!Directory.Exists(root)) continue;
                try { foreach (string link in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories)) AddShortcut(games, link); }
                catch (Exception ex) { ErrorLog.Write("Game shortcut scan: " + ex.Message); }
            }
            return games.Values.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        });

        private static IEnumerable<string> SteamRoots()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Steam")
            };
            foreach (string path in candidates) if (Directory.Exists(Path.Combine(path, "steamapps"))) roots.Add(path);
            foreach (string root in roots.ToArray())
            {
                string libraries = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(libraries)) continue;
                try
                {
                    string text = File.ReadAllText(libraries);
                    foreach (Match match in Regex.Matches(text, "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                    {
                        string path = match.Groups[1].Value.Replace("\\\\", "\\");
                        if (Directory.Exists(Path.Combine(path, "steamapps"))) roots.Add(path);
                    }
                }
                catch (Exception ex) { ErrorLog.Write("Steam library scan: " + ex.Message); }
            }
            return roots;
        }

        private static void ScanSteam(string root, Dictionary<string, LibraryGame> games)
        {
            string apps = Path.Combine(root, "steamapps");
            if (!Directory.Exists(apps)) return;
            try
            {
                foreach (string manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                {
                    try
                    {
                        string text = File.ReadAllText(manifest);
                        string name = Field(text, "name"), folder = Field(text, "installdir");
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(folder) || IsToolEntry(name)) continue;
                        string install = Path.Combine(apps, "common", folder);
                        string exe = FindGameExecutable(install, folder);
                        if (!string.IsNullOrEmpty(exe))
                        {
                            AddExe(games, exe, name);
                            string appId = Field(text, "appid");
                            if (appId != null && games.TryGetValue(exe, out var game)) game.IconPath = SteamIcon(root, appId);
                        }
                    }
                    catch (Exception ex) { ErrorLog.Write("Steam game manifest: " + ex.Message); }
                }
            }
            catch (Exception ex) { ErrorLog.Write("Steam game scan: " + ex.Message); }
        }

        private static string Field(string text, string field)
        {
            var match = Regex.Match(text, "\\\"" + Regex.Escape(field) + "\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string SteamIcon(string root, string appId)
        {
            if (!Regex.IsMatch(appId, "^[0-9]+$")) return null;
            foreach (string steam in SteamRoots())
            {
                string cache = Path.Combine(steam, "appcache", "librarycache");
                try
                {
                    if (!Directory.Exists(cache)) continue;
                    string icon = Directory.EnumerateFiles(cache, appId + "_icon.*").FirstOrDefault();
                    string folder = Path.Combine(cache, appId);
                    if (icon == null && Directory.Exists(folder)) icon = Directory.EnumerateFiles(folder, "*icon*").FirstOrDefault(p => new[] { ".jpg", ".png", ".ico" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase));
                    if (icon != null) return icon;
                }
                catch { }
            }
            return null;
        }

        internal static string FindGameExecutable(string folder, string installName)
        {
            if (!Directory.Exists(folder)) return null;
            var found = new List<string>();
            var queue = new Queue<(string path, int depth)>(); queue.Enqueue((folder, 0));
            while (queue.Count > 0)
            {
                var (path, depth) = queue.Dequeue();
                try
                {
                    found.AddRange(Directory.EnumerateFiles(path, "*.exe", SearchOption.TopDirectoryOnly)
                        .Where(p => !Path.GetFileName(p).StartsWith("unins", StringComparison.OrdinalIgnoreCase)
                            && !Path.GetFileName(p).Contains("crash", StringComparison.OrdinalIgnoreCase)
                            && !Path.GetFileName(p).Contains("setup", StringComparison.OrdinalIgnoreCase)));
                    if (depth < 3)
                        foreach (string child in Directory.EnumerateDirectories(path).Where(p =>
                            !Path.GetFileName(p).Contains("redist", StringComparison.OrdinalIgnoreCase)
                            && !Path.GetFileName(p).Contains("easyanticheat", StringComparison.OrdinalIgnoreCase)))
                            queue.Enqueue((child, depth + 1));
                }
                catch { }
                if (found.Count > 80) break;
            }
            string[] preferred = PreferredExecutables(installName);
            foreach (string name in preferred)
            {
                string match = found.FirstOrDefault(p => string.Equals(Path.GetFileName(p), name, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            string normalized = Regex.Replace(installName, "[^a-z0-9]", "", RegexOptions.IgnoreCase);
            return found.Where(p => !IsHelperExecutable(Path.GetFileName(p)))
                .OrderByDescending(p => Regex.Replace(Path.GetFileNameWithoutExtension(p), "[^a-z0-9]", "", RegexOptions.IgnoreCase).Equals(normalized, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(p => new FileInfo(p).Length).FirstOrDefault();
        }

        private static string[] PreferredExecutables(string gameName)
        {
            if (gameName.Contains("counter-strike", StringComparison.OrdinalIgnoreCase)) return new[] { "cs2.exe", "csgo.exe" };
            if (gameName.Contains("dota 2", StringComparison.OrdinalIgnoreCase)) return new[] { "dota2.exe" };
            if (gameName.Contains("valorant", StringComparison.OrdinalIgnoreCase)) return new[] { "VALORANT-Win64-Shipping.exe" };
            if (gameName.Contains("fortnite", StringComparison.OrdinalIgnoreCase)) return new[] { "FortniteClient-Win64-Shipping.exe" };
            if (gameName.Contains("apex", StringComparison.OrdinalIgnoreCase)) return new[] { "r5apex.exe" };
            if (gameName.Contains("rocket league", StringComparison.OrdinalIgnoreCase)) return new[] { "RocketLeague.exe" };
            if (gameName.Contains("grand theft auto", StringComparison.OrdinalIgnoreCase) || gameName.Contains("gta v", StringComparison.OrdinalIgnoreCase)) return new[] { "GTA5.exe" };
            if (gameName.Contains("cyberpunk", StringComparison.OrdinalIgnoreCase)) return new[] { "Cyberpunk2077.exe" };
            if (gameName.Contains("elden ring", StringComparison.OrdinalIgnoreCase)) return new[] { "eldenring.exe" };
            if (gameName.Contains("witcher 3", StringComparison.OrdinalIgnoreCase)) return new[] { "witcher3.exe" };
            if (gameName.Contains("beamng", StringComparison.OrdinalIgnoreCase)) return new[] { "BeamNG.drive.exe" };
            if (gameName.Contains("overwatch", StringComparison.OrdinalIgnoreCase)) return new[] { "Overwatch.exe" };
            return Array.Empty<string>();
        }

        private static bool IsHelperExecutable(string name) => name.Contains("console", StringComparison.OrdinalIgnoreCase)
            || name.Contains("launcher", StringComparison.OrdinalIgnoreCase)
            || name.Contains("crash", StringComparison.OrdinalIgnoreCase)
            || name.Contains("unins", StringComparison.OrdinalIgnoreCase)
            || name.Contains("setup", StringComparison.OrdinalIgnoreCase)
            || name.Contains("server", StringComparison.OrdinalIgnoreCase)
            || name.Contains("helper", StringComparison.OrdinalIgnoreCase);

        private static void AddShortcut(Dictionary<string, LibraryGame> games, string link)
        {
            string shortcutName = Path.GetFileNameWithoutExtension(link);
            if (!LooksLikeGame(shortcutName)) return;
            object shell = null, shortcut = null;
            try
            {
                Type type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return;
                shell = Activator.CreateInstance(type);
                shortcut = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { link });
                string target = shortcut.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string;
                AddExe(games, target, shortcutName);
            }
            catch (Exception ex) { ErrorLog.Write("Game shortcut: " + ex.Message); }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        internal static bool IsToolEntry(string name) => name.Equals("ShareX", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Wallpaper Engine", StringComparison.OrdinalIgnoreCase)
            || name.Equals("OBS Studio", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Blender", StringComparison.OrdinalIgnoreCase)
            || name.Contains("redistributable", StringComparison.OrdinalIgnoreCase)
            || name.Contains("steamworks", StringComparison.OrdinalIgnoreCase)
            || name.Contains("proton", StringComparison.OrdinalIgnoreCase)
            || name.Contains("runtime", StringComparison.OrdinalIgnoreCase)
            || name.Contains("dedicated server", StringComparison.OrdinalIgnoreCase);

        private static bool LooksLikeGame(string name)
        {
            string[] terms = { "dota", "counter-strike", "counter strike", "valorant", "fortnite", "apex legends", "rocket league", "grand theft auto", "gta ", "elden ring", "cyberpunk", "witcher", "minecraft", "overwatch", "how to fish", "pubg", "baldur", "starfield", "game" };
            return terms.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        private static void AddExe(Dictionary<string, LibraryGame> games, string path, string name)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)) return;
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(path);
            if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) || name.Contains("crash", StringComparison.OrdinalIgnoreCase)) return;
            games[path] = new LibraryGame { Name = name, Executable = path };
        }

        public static void Launch(LibraryGame game)
        {
            if (game == null || !File.Exists(game.Executable)) throw new FileNotFoundException("Файл игры не найден.", game?.Executable);
            Process.Start(new ProcessStartInfo(game.Executable) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(game.Executable) });
        }
    }
}
