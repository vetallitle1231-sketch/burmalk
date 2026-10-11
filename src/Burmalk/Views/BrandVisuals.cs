using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Burmalk.Views
{
    internal static class BrandVisuals
    {
        public static Border Mark(double size = 38)
        {
            return new Border { Width = size, Height = size, Child = new Image {
                Source = new BitmapImage(new System.Uri("pack://application:,,,/Assets/Burmalk.png")), Stretch = Stretch.Uniform } };
        }
        public static Border Badge(string caption)
        {
            var text = ViewUi.Text(caption); text.FontSize = 10; text.FontWeight = FontWeights.SemiBold;
            text.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            var badge = new Border { Child = text, CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 2, 9, 2), HorizontalAlignment = HorizontalAlignment.Left };
            badge.SetResourceReference(Border.BackgroundProperty, "AccentSoft"); return badge;
        }
        public static UIElement Navigation(string glyph, string caption)
        {
            var row = new DockPanel();
            var icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 15, Width = 27, VerticalAlignment = VerticalAlignment.Center };
            var text = new TextBlock { Text = caption, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
            foreach (var label in new[] { icon, text }) label.SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1) });
            DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon); row.Children.Add(text); return row;
        }
    }
}
