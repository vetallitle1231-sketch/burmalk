using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Burmalk.Models;
using Burmalk.Services;

namespace Burmalk.Views;

public sealed class OverlayCustomizationPanel : StackPanel
{
    private readonly TextBlock _preview = ViewUi.Text(""), _status = ViewUi.Text(""), _scaleLabel = ViewUi.Text("");
    private readonly TextBox _color;
    private readonly ComboBox _layout;
    private readonly Slider _scale;
    private bool _ready;
    public OverlayCustomizationPanel()
    {
        var settings = App.Store.Settings;
        Children.Add(ViewUi.Text("Выбери показатели и вид текста. Изменения сохраняются сразу. Фон остаётся прозрачным; положение на экране задаётся в RTSS."));
        var metrics = new WrapPanel();
        Add("FPS", settings.OverlayShowFps, v => settings.OverlayShowFps = v);
        Add("Средний FPS", settings.OverlayShowAverage, v => settings.OverlayShowAverage = v);
        Add("Время кадра", settings.OverlayShowFrameTime, v => settings.OverlayShowFrameTime = v);
        Add("CPU", settings.OverlayShowCpu, v => settings.OverlayShowCpu = v);
        Add("GPU", settings.OverlayShowGpu, v => settings.OverlayShowGpu = v);
        Add("RAM", settings.OverlayShowRam, v => settings.OverlayShowRam = v);
        Add("VRAM", settings.OverlayShowVram, v => settings.OverlayShowVram = v);
        Add("Сеть", settings.OverlayShowNetwork, v => settings.OverlayShowNetwork = v);
        Children.Add(metrics);
        void Add(string title, bool enabled, Action<bool> set)
        {
            var check = new CheckBox { Content = title, IsChecked = enabled, Margin = new Thickness(0, 10, 18, 10) };
            check.Click += (_, _) => { set(check.IsChecked == true); Commit(); }; metrics.Children.Add(check);
        }
        Children.Add(ViewUi.Text("Вид оверлея"));
        _layout = new ComboBox { ItemsSource = new[] { "Столбик", "Компактная строка" }, SelectedIndex = settings.OverlayLayout == "Compact" ? 1 : 0, Width = 250, HorizontalAlignment = HorizontalAlignment.Left };
        _layout.SelectionChanged += (_, _) => { settings.OverlayLayout = _layout.SelectedIndex == 1 ? "Compact" : "Lines"; Commit(); }; Children.Add(_layout);
        Children.Add(_scaleLabel);
        _scale = new Slider { Minimum = 60, Maximum = 180, TickFrequency = 10, IsSnapToTickEnabled = true, Value = settings.OverlayTextScale, Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
        _scale.ValueChanged += (_, _) => { settings.OverlayTextScale = (int)_scale.Value; Commit(); }; Children.Add(_scale);
        Children.Add(ViewUi.Text("Цвет текста · HEX"));
        _color = new TextBox { Text = "#" + settings.OverlayColor, MaxLength = 7, Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        _color.TextChanged += (_, _) =>
        {
            string color = _color.Text.Trim().TrimStart('#');
            if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^[0-9A-Fa-f]{6}$")) { _status.Text = "Введи цвет из шести символов, например #55E6CE."; return; }
            settings.OverlayColor = color.ToUpperInvariant(); Commit();
        }; Children.Add(_color);
        var colors = new WrapPanel();
        foreach (var preset in new[] { ("Белый", "FFFFFF"), ("Бирюзовый", "55E6CE"), ("Фиолетовый", "BCA0FF"), ("Золотой", "FFD166") })
        {
            string value = preset.Item2;
            var button = ViewUi.Button(preset.Item1, () => _color.Text = "#" + value); button.Margin = new Thickness(0, 8, 8, 8); colors.Children.Add(button);
        }
        Children.Add(colors);
        Children.Add(ViewUi.Text("Предпросмотр · пример значений, не текущие измерения"));
        _preview.FontFamily = new FontFamily("Consolas"); _preview.TextWrapping = TextWrapping.Wrap;
        Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(9, 14, 26)), CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Child = _preview });
        Children.Add(ViewUi.Text("Профиль конкретной игры может отдельно менять CPU / GPU / RAM / VRAM. FPS и оформление общие для всех игр."));
        Children.Add(_status); _ready = true; UpdatePreview();
    }
    private void Commit()
    {
        if (!_ready) return;
        UpdatePreview(); App.Store.Save(); OverlayManager.RefreshContent();
        _status.Text = App.Store.LastSaveSucceeded ? "Оверлей сохранён." : App.Store.PersistenceStatus;
    }
    private void UpdatePreview()
    {
        var s = App.Store.Settings; _scaleLabel.Text = "Размер текста: " + s.OverlayTextScale + "%";
        _preview.FontSize = 14 * s.OverlayTextScale / 100d;
        _preview.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#" + s.OverlayColor));
        _preview.Text = new OverlayText().BuildPlain(s, null, new GameCurrent { Pid = 1 },
            new Sample { CpuPercent = 35, CpuTempC = 58, CpuGhz = 4.2, GpuPercent = 92, GpuTempC = 67, GpuClockMhz = 1900, GpuPowerW = 160, GpuVramUsedGb = 4, GpuVramTotalGb = 8, RamUsedGb = 12, NetDownKBps = 32, NetUpKBps = 8 },
            new RtssOverlay.FrameStats(144, 6.9), DateTime.UtcNow);
    }
}
