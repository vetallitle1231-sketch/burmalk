using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Burmalk.Services;

namespace Burmalk.Views
{
    public sealed class DiskSpeedPanel : UserControl
    {
        private readonly ComboBox _drives = new ComboBox { MinWidth = 320, Margin = new Thickness(0, 4, 8, 8) };
        private readonly TextBlock _status = ViewUi.Text("Выберите диск и запустите проверку.");
        private readonly TextBlock _results = ViewUi.Text("Чтение — МБ/с    Запись — МБ/с", "BigValue");
        private readonly TextBlock _folderLabel = ViewUi.Text("");
        private readonly Dictionary<string, string> _folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (DiskSpeedResult result, DateTime time)> _history = new Dictionary<string, (DiskSpeedResult, DateTime)>(StringComparer.OrdinalIgnoreCase);
        private readonly Button _start, _stop, _reload, _chooseFolder;
        private CancellationTokenSource _run;

        private sealed class DriveChoice
        {
            public string Root;
            public string Label;
            public override string ToString() => Label;
        }

        public DiskSpeedPanel()
        {
            _start = ViewUi.Button("Проверить чтение и запись", () => RunTest());
            _stop = ViewUi.Button("Остановить тест", () => _run?.Cancel()); _stop.IsEnabled = false;
            _reload = ViewUi.Button("Обновить диски", LoadDrives);
            _chooseFolder = ViewUi.Button("Выбрать папку для теста", ChooseFolder);
            _drives.SelectionChanged += (s, e) => ShowSelection();
            var actions = new WrapPanel();
            foreach (var button in new[] { _start, _stop, _reload, _chooseFolder }) actions.Children.Add(button);
            Content = ViewUi.Panel("ПРОВЕРКА СКОРОСТИ ДИСКА", _drives, _results,
                ViewUi.Text("Короткий последовательный тест: запись и чтение 128 МиБ без файлового кэша Windows. Создаётся отдельный временный файл; после теста он удаляется."),
                _folderLabel, actions, _status);
            Unloaded += (s, e) => _run?.Cancel();
            LoadDrives();
            foreach (var saved in App.Store.DiskMeasurements)
                _history[saved.Root] = (new DiskSpeedResult { ReadMBps = saved.ReadMBps, WriteMBps = saved.WriteMBps, Bytes = saved.Bytes }, saved.TestedUtc.ToLocalTime());
            ShowSelection();
        }

        private void LoadDrives()
        {
            if (_run != null) return;
            string selected = (_drives.SelectedItem as DriveChoice)?.Root;
            var choices = new List<DriveChoice>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)) continue;
                    choices.Add(new DriveChoice { Root = drive.Name, Label = drive.Name + "  " + drive.VolumeLabel + " · " + drive.DriveFormat + " · свободно " + (drive.AvailableFreeSpace / 1073741824.0).ToString("0.0") + " ГиБ" });
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            _drives.ItemsSource = choices;
            _drives.SelectedItem = choices.FirstOrDefault(d => d.Root == selected) ?? choices.FirstOrDefault();
            _start.IsEnabled = choices.Count > 0; _chooseFolder.IsEnabled = choices.Count > 0;
            if (choices.Count == 0) _status.Text = "Нет доступных локальных дисков.";
        }

        private string SelectedFolder(DriveChoice drive)
        {
            if (!_folders.TryGetValue(drive.Root, out var folder)) folder = DiskSpeedTest.DefaultFolder(drive.Root);
            return folder;
        }

        private void ShowSelection()
        {
            if (_drives.SelectedItem is not DriveChoice drive) return;
            _folderLabel.Text = "Папка теста: " + SelectedFolder(drive);
            if (_history.TryGetValue(drive.Root, out var saved)) ShowResult(drive.Root, saved.result, saved.time);
            else { _results.Text = "Чтение — МБ/с    Запись — МБ/с"; _status.Text = "Выбран " + drive.Root + ". Нажмите «Проверить чтение и запись»."; }
        }

        private void ChooseFolder()
        {
            if (_run != null || _drives.SelectedItem is not DriveChoice drive) return;
            var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Выберите доступную папку на диске " + drive.Root, InitialDirectory = SelectedFolder(drive) };
            if (picker.ShowDialog() != true) return;
            if (!string.Equals(Path.GetPathRoot(picker.FolderName), drive.Root, StringComparison.OrdinalIgnoreCase))
            { _status.Text = "Выберите папку именно на диске " + drive.Root + ", или сначала смените диск в списке."; return; }
            _folders[drive.Root] = picker.FolderName; ShowSelection();
        }

        private void ShowResult(string root, DiskSpeedResult result, DateTime time)
        {
            _results.Text = "Чтение " + result.ReadMBps.ToString("0.0") + " МБ/с    Запись " + result.WriteMBps.ToString("0.0") + " МБ/с";
            _status.Text = root + " · " + time.ToString("HH:mm:ss") + " · проверено " + (result.Bytes / 1048576) + " МиБ. Временный файл удалён.\nРезультат зависит от нагрузки и внутреннего кэша накопителя; это тест скорости, не диагностика исправности.";
        }

        private async void RunTest()
        {
            if (_run != null || _drives.SelectedItem is not DriveChoice drive) return;
            string folder = SelectedFolder(drive);
            _run = new CancellationTokenSource();
            _start.IsEnabled = _reload.IsEnabled = _chooseFolder.IsEnabled = _drives.IsEnabled = false;
            _stop.IsEnabled = true; _results.Text = "Чтение — МБ/с    Запись — МБ/с";
            try
            {
                var progress = new Progress<string>(text => { if (_run != null) _status.Text = drive.Root + " · " + text; });
                var result = await Task.Run(() => DiskSpeedTest.Run(folder, _run.Token, progress));
                DateTime time = DateTime.Now; _history[drive.Root] = (result, time); ShowResult(drive.Root, result, time);
                App.Store.SaveDiskMeasurement(new Burmalk.Models.DiskMeasurement { Root = drive.Root, VolumeBytes = new DriveInfo(drive.Root).TotalSize,
                    PhysicalModel = App.Monitor.Storage.PhysicalDrives.FirstOrDefault(d => d.VolumeLetters.Contains(drive.Root.TrimEnd('\\')))?.Model ?? "",
                    TestedUtc = time.ToUniversalTime(), ReadMBps = result.ReadMBps, WriteMBps = result.WriteMBps, Bytes = result.Bytes });
            }
            catch (OperationCanceledException) { _status.Text = "Тест остановлен. Временный файл удалён."; }
            catch (Exception ex) { _status.Text = "Не удалось проверить " + drive.Root + ": " + ex.Message + "\nЕсли папка недоступна для записи, выберите другую папку на этом диске."; }
            finally
            {
                _run.Dispose(); _run = null;
                _start.IsEnabled = _reload.IsEnabled = _chooseFolder.IsEnabled = _drives.IsEnabled = true;
                _stop.IsEnabled = false;
            }
        }
    }
}
