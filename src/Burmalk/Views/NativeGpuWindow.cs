using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Burmalk.Services;

namespace Burmalk.Views;
internal sealed class NativeGpuWindow : Window
{
    public static async Task<NativeGpuResult> RunAsync(Window owner, CancellationToken token, Func<bool> safe, IProgress<string> status = null)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        var image = new Image { Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, -1) };
        var text = ViewUi.Text("Подготовка аппаратного OpenGL-теста…"); text.FontSize = 18;
        var bar = new ProgressBar { Minimum = 0, Maximum = 120, Height = 4, Margin = new Thickness(0, 12, 0, 12) };
        var root = new Grid { Margin = new Thickness(24) }; root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(image);
        var bottom = new StackPanel(); bottom.Children.Add(bar); bottom.Children.Add(text); bottom.Children.Add(ViewUi.Button("Остановить тест", cancel.Cancel)); Grid.SetRow(bottom, 1); root.Children.Add(bottom);
        var window = new NativeGpuWindow { Title = "BURMALK · GPU TEST · 1280 × 720", Width = 1000, Height = 700, MinWidth = 700, MinHeight = 500, Background = new SolidColorBrush(Color.FromRgb(8, 16, 31)), Content = root, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        if (string.IsNullOrEmpty(App.SelfTestMode) && owner != null) window.Owner = owner;
        window.Closed += (s, e) => cancel.Cancel();
        bool captured = false;
        var progress = new Progress<GpuFrame>(frame =>
        {
            if (!window.IsVisible) return;
            bar.Value = frame.Seconds; text.Text = frame.Renderer + "\n" + frame.Fps.ToString("0.0") + " FPS · " + Math.Min(120, frame.Seconds).ToString("0") + " / 120 с";
            status?.Report("GPU · " + frame.Seconds.ToString("0") + " / 120 с · " + frame.Fps.ToString("0.0") + " FPS");
            var bitmap = BitmapSource.Create(NativeGpuBenchmark.Width, NativeGpuBenchmark.Height, 96, 96, PixelFormats.Bgra32, null, frame.Pixels, NativeGpuBenchmark.Width * 4); bitmap.Freeze(); image.Source = bitmap;
            if (!captured && frame.Seconds > 8 && Verification.DirectoryPath != null) { captured = true; Verification.Capture(window, "GPU-native-preview"); }
        });
        try { window.Show(); return await NativeGpuBenchmark.RunAsync(progress, cancel.Token, safe); }
        finally { window.Close(); }
    }
}
