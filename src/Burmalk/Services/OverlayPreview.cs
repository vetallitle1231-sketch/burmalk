using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Burmalk.Views;

namespace Burmalk.Services;

// Offscreen software-only UI proof. Does not start sensors, RTSS, hotkeys or a game.
internal static class OverlayPreview
{
    public static void Save(string directory)
    {
        var panel=new OverlayCustomizationPanel();
        var page=new Border{Background=new SolidColorBrush(Color.FromRgb(8,15,32)),Padding=new Thickness(24),Child=panel};
        Render(page,Path.Combine(directory,"Overlay-Settings.png"),1060);
        var card=panel.PreviewVisual;
        // Detach only the preview card from this isolated test panel.
        panel.Children.Remove(card);
        var proof=new StackPanel();
        proof.Children.Add(new TextBlock{Text="BURMALK 1.1.11  /  FPS OVERLAY",Foreground=Brushes.White,FontSize=20,Margin=new Thickness(0,0,0,10)});
        proof.Children.Add(new TextBlock{Text="Пример оформления и значений. Игровой оверлей имеет прозрачный фон.",Foreground=Brushes.LightGray,FontSize=14,Margin=new Thickness(0,0,0,20)});
        proof.Children.Add(card);
        Render(new Border{Background=new SolidColorBrush(Color.FromRgb(8,15,32)),Padding=new Thickness(28),Child=proof},Path.Combine(directory,"Overlay-Preview.png"),1060);
    }
    private static void Render(FrameworkElement visual,string path,int width)
    {
        visual.Measure(new Size(width,double.PositiveInfinity));
        int height=(int)Math.Ceiling(visual.DesiredSize.Height);
        visual.Arrange(new Rect(0,0,width,height));visual.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(path);encoder.Save(stream);
    }
}
