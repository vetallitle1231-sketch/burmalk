using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Documents;
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
    internal FrameworkElement PreviewVisual => (FrameworkElement)_preview.Parent;
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
        _layout = new ComboBox { ItemsSource = new[] { "Панель датчиков", "Компактная строка" }, SelectedIndex = settings.OverlayLayout == "Compact" ? 1 : 0, Width = 250, HorizontalAlignment = HorizontalAlignment.Left };
        _layout.SelectionChanged += (_, _) => { settings.OverlayLayout = _layout.SelectedIndex == 1 ? "Compact" : "Lines"; Commit(); }; Children.Add(_layout);
        var sensorColors=new CheckBox{Content="Цветные подписи FPS / GPU / CPU / RAM",IsChecked=settings.OverlaySensorColors,Margin=new Thickness(0,10,0,6)};
        sensorColors.Click+=(_,_)=>{settings.OverlaySensorColors=sensorColors.IsChecked==true;Commit();};Children.Add(sensorColors);
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
        Children.Add(ViewUi.Text("FPS — измеренные RTSS кадры активной игры. AVG = сумма кадров / время наблюдённых интервалов в этом сеансе оверлея; одинаковое измерение учитывается один раз. Перед первым показом ожидается обновление счётчика. Застывшие FPS пропадают через 3 секунды."));
        Children.Add(ViewUi.Text("FRAME AVG — среднее время кадра в текущем интервале; LAST — длительность последнего кадра RTSS. CPU / GPU / RAM и сетевой трафик относятся ко всему ПК. CLK(avg) — средняя частота ядер, CLK(OS) — показание Windows, которое может не отражать boost. PKG / TCTL / TDIE / COREMAX подписывают выбранный температурный датчик CPU. MEM — сообщённая драйвером частота памяти GPU, без умножения DDR. GiB / KiB — двоичные единицы. Недоступные и устаревшие данные: --. FPS обновляется не реже раза в секунду, GPU опрашивается примерно раз в две секунды."));
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
        var now=DateTime.UtcNow;var display=new OverlayText();var game=new GameCurrent{Pid=1};
        display.ObserveFrames(game,new RtssOverlay.FrameStats(144,6.9,2000,1000,144),now.AddSeconds(-1));
        string text=display.BuildPlain(s,null,game,
            new Sample{TimestampUtc=now.Ticks,GpuTimestampUtc=now.Ticks,GpuName="NVIDIA GeForce · пример",CpuTemperatureSource="CPU Package",CpuPercent=35,CpuTempC=58,CpuGhz=4.2,GpuPercent=92,GpuTempC=67,GpuClockMhz=1900,GpuMemClockMhz=7000,GpuPowerW=160,GpuVramUsedGb=4,GpuVramTotalGb=8,RamUsedGb=12,RamTotalGb=31.92,NetDownKBps=32,NetUpKBps=8},
            new RtssOverlay.FrameStats(144,6.9,3000,2000,144),now);
        _preview.Inlines.Clear();
        var parts=text.Split(s.OverlayLayout=="Compact"?"  |  ":"\n");
        foreach(string part in parts)
        {
            if(_preview.Inlines.Count>0)_preview.Inlines.Add(new Run(s.OverlayLayout=="Compact"?"  |  ":"\n"));
            if(part.StartsWith("BURMALK")){_preview.Inlines.Add(new Run(part){FontSize=_preview.FontSize*.75,Foreground=new SolidColorBrush(Color.FromRgb(148,163,184))});continue;}
            string label=part.Length>=5?part[..5]:part;
            string color=label.Trim() switch{"FPS" or "AVG"=>"FFD166","GPU"=>"A3E635","CPU"=>"67E8F9","RAM" or "VRAM"=>"C4B5FD",_=>"94A3B8"};
            _preview.Inlines.Add(new Run(label){Foreground=s.OverlaySensorColors?new SolidColorBrush((Color)ColorConverter.ConvertFromString("#"+color)):_preview.Foreground});
            _preview.Inlines.Add(new Run(part[label.Length..]));
        }
    }
}
