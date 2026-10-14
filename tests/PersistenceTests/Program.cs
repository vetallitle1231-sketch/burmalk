using System.Text.Json;
using System.Text.Json.Nodes;
using Burmalk.Models;
using Burmalk.Services;

string root=Path.GetFullPath(args.Length>0?args[0]:Path.Combine(Path.GetTempPath(),"Burmalk-Persistence-"+Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
DataStore Open(string dir=null){var store=new DataStore(dir??root);store.Load();return store;}
var initial=Open();initial.Settings.Theme="Purple";initial.Settings.OverlayColor="55E6CE";initial.Settings.OverlayTextScale=140;
initial.Settings.BackgroundImage="wallpapers/user.png";initial.Settings.DashboardPositions["pc3d"]=new(){X=411,Y=123};initial.Settings.PcModel.GpuFans=2;initial.Settings.StartWithWindows=true;
initial.Save();Check(initial.LastSaveSucceeded,"First save succeeds");
string path=Path.Combine(root,"settings.json");
Check(File.Exists(path+".lastgood"),"Latest state backup exists on first save");
var restart=Open();Check(restart.Settings.OverlayTextScale==140&&restart.Settings.PcModel.GpuFans==2&&restart.Settings.DashboardPositions["pc3d"].X==411&&restart.Settings.StartWithWindows&&restart.Settings.BackgroundImage=="wallpapers/user.png","Restart preserves preferences, wallpaper, model, widgets and startup");
restart.Settings.OverlayTextScale=160;restart.Save();Check(File.Exists(path+".bak"),"Previous committed state is backed up");
File.WriteAllText(path,"{ interrupted");var recovered=Open();Check(recovered.Settings.OverlayTextScale==160&&recovered.PersistenceStatus.Contains("восстановлены"),"Corrupted primary recovers latest saved state");
Check(Directory.GetFiles(root,"settings.json.invalid-*").Length>0,"Corrupted original is preserved for recovery");
File.WriteAllText(path,"");File.WriteAllText(path+".lastgood","null");var previous=Open();Check(previous.Settings.OverlayTextScale==140,"Previous state recovers when latest and primary are both damaged");
var first=Open();var second=Open();first.Settings.OverlayColor="FFD166";first.Save();second.Settings.BackgroundDim=.7;second.Save();
var combined=Open();Check(combined.Settings.OverlayColor=="FFD166"&&combined.Settings.BackgroundDim==.7,"Stale instance merges only edited settings");
second.Save();Check(Open().Settings.OverlayColor=="FFD166","Repeated stale exit cannot reset another instance's edits");
var json=JsonNode.Parse(File.ReadAllText(path)).AsObject();json["FuturePreference"]="keep";File.WriteAllText(path,json.ToJsonString());combined.Settings.OverlayShowAverage=false;combined.Save();
Check(JsonNode.Parse(File.ReadAllText(path))["FuturePreference"].GetValue<string>()=="keep","Unknown preferences survive same-version updates");
File.Delete(path);Check(Open().Settings.OverlayColor=="FFD166","Missing primary recovers newest backup");
string broken=Path.Combine(root,"unrecoverable");Directory.CreateDirectory(broken);string bad=Path.Combine(broken,"settings.json");File.WriteAllText(bad,"{bad");var blocked=Open(broken);blocked.Settings.OverlayTextScale=180;blocked.Save();
Check(!blocked.LastSaveSucceeded&&File.ReadAllText(bad)=="{bad","Unrecoverable data is not silently overwritten with defaults");
string legacy=Path.Combine(root,"legacy");Directory.CreateDirectory(legacy);File.WriteAllText(Path.Combine(legacy,"settings.json"),"{\"OverlayEnabled\":true,\"PcModelCooling\":\"Air\"}");
var migrated=Open(legacy);migrated.Save();Check(Open(legacy).Settings.OverlayEnabled&&Open(legacy).Settings.OverlayShowFps&&Open(legacy).Settings.PcModelCooling=="Air","Old settings gain overlay defaults without losing existing choices");
string lockedRoot=Path.Combine(root,"locked-recovery");var lockedStore=Open(lockedRoot);lockedStore.Settings.OverlayTextScale=170;lockedStore.Save();string lockedPath=Path.Combine(lockedRoot,"settings.json");File.WriteAllText(lockedPath,"{broken");
using(var fileLock=new FileStream(lockedPath,FileMode.Open,FileAccess.Read,FileShare.Read))
{
    var lockedRead=Open(lockedRoot);Check(lockedRead.Settings.OverlayTextScale==170&&!lockedRead.LastSaveSucceeded,"Recovery still uses valid preferences when another process prevents repairing the primary file");
}
Console.WriteLine($"PASS: {checks} isolated persistence checks. Fixtures: {root}");

namespace Burmalk.Services
{
    // File persistence tests do not initialize WPF, hardware, games or user logging.
    internal static class Appearance {public static string[] Themes={"Burmalk","Purple"};}
    internal static class ErrorLog {public static void Write(string text) { }}
}
