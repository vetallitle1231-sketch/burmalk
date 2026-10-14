using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using Burmalk.Models;

namespace Burmalk.Services;

internal static class PcModelPreview
{
    internal static void ExportObj(string path, PcModelOptions options, bool water, bool gpuOnly = false)
    {
        var scene = PcModelScene.Desktop(options, water);
        string name = System.IO.Path.GetFileNameWithoutExtension(path), materialFile = name + ".mtl";
        using var obj = new StreamWriter(path, false, new UTF8Encoding(false));
        using var mtl = new StreamWriter(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), materialFile), false, new UTF8Encoding(false));
        obj.WriteLine("# Burmalk configurable PC model. Y is up. Arbitrary illustration units."); obj.WriteLine("mtllib " + materialFile);
        string N(double number) => number.ToString("0.######", CultureInfo.InvariantCulture);
        int offset = 1, index = 0;
        foreach (var model in scene.Root.Children.OfType<GeometryModel3D>())
        {
            if (gpuOnly && (!scene.Parts.TryGetValue(model, out string selectedPart) || selectedPart != "gpu")) continue;
            var mesh = (MeshGeometry3D)model.Geometry; string id = "material_" + index++;
            var diffuse = ((MaterialGroup)model.Material).Children.OfType<DiffuseMaterial>().First(); var color = ((SolidColorBrush)diffuse.Brush).Color;
            mtl.WriteLine("newmtl " + id); mtl.WriteLine($"Kd {N(color.R / 255d)} {N(color.G / 255d)} {N(color.B / 255d)}"); mtl.WriteLine("d " + N(color.A / 255d)); mtl.WriteLine("Ks 0.16 0.16 0.16\nNs 52\n");
            obj.WriteLine("g " + (scene.Parts.TryGetValue(model, out string part) ? part : "panel") + "_" + index); obj.WriteLine("usemtl " + id);
            foreach (var point in mesh.Positions) { var p = model.Transform.Transform(point); obj.WriteLine($"v {N(p.X)} {N(p.Y)} {N(p.Z)}"); }
            foreach (var vector in mesh.Normals) { var n = model.Transform.Transform(vector); obj.WriteLine($"vn {N(n.X)} {N(n.Y)} {N(n.Z)}"); }
            for (int i = 0; i < mesh.TriangleIndices.Count; i += 3)
            { int a = offset + mesh.TriangleIndices[i], b = offset + mesh.TriangleIndices[i + 1], c = offset + mesh.TriangleIndices[i + 2]; obj.WriteLine($"f {a}//{a} {b}//{b} {c}//{c}"); }
            offset += mesh.Positions.Count;
        }
    }
    // Renders the very same meshes used by the interactive view, with a studio background.
    internal static void Save(string path, PcModelOptions options, bool water, string title, double yaw = 29, bool gpuOnly = false)
    {
        const int width = 1200, height = 1120;
        var canvas = new Grid { Width = width, Height = height, Background = new SolidColorBrush(Color.FromRgb(242, 244, 247)) };
        canvas.Children.Add(new Ellipse { Width = gpuOnly ? 560 : 660, Height = 80, Fill = new RadialGradientBrush(Color.FromArgb(70, 66, 73, 86), Colors.Transparent), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, gpuOnly ? 230 : 112), IsHitTestVisible = false });
        double angle = yaw * Math.PI / 180;
        var target = gpuOnly ? new Point3D(-.2, -.88, .1) : new Point3D();
        var camera = new OrthographicCamera { Position = target + new Vector3D(Math.Sin(angle) * 16, 3.2, Math.Cos(angle) * 16), LookDirection = new Vector3D(-Math.Sin(angle) * 16, -3.2, -Math.Cos(angle) * 16), UpDirection = new Vector3D(0, 1, 0), Width = gpuOnly ? 5.5 : 9.8, NearPlaneDistance = .1, FarPlaneDistance = 100 };
        var viewport = new Viewport3D { Camera = camera, Margin = new Thickness(0, 105, 0, 105) };
        var scene = PcModelScene.Desktop(options, water); var root = scene.Root;
        if (gpuOnly)
        {
            root = new Model3DGroup();
            foreach (var model in scene.Root.Children)
                if (model is Light || model is GeometryModel3D geometry && scene.Parts.TryGetValue(geometry, out string part) && part == "gpu") root.Children.Add(model);
        }
        viewport.Children.Add(new ModelVisual3D { Content = root }); canvas.Children.Add(viewport);
        canvas.Children.Add(new TextBlock { Text = "BURMALK  /  3D PC", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(57, 67, 86)), Margin = new Thickness(58, 35, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
        canvas.Children.Add(new TextBlock { Text = title, FontSize = 29, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(26, 35, 51)), Margin = new Thickness(58, 61, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
        canvas.Children.Add(new TextBlock { Text = gpuOnly ? "КОЖУХ · РАДИАТОР · ТЕПЛОВЫЕ ТРУБКИ · ЗАДНЯЯ ПЛАСТИНА · ПИТАНИЕ" : "КОРПУС · ОХЛАЖДЕНИЕ · GPU · RAM · ВЕНТИЛЯТОРЫ — НАСТРАИВАЮТСЯ", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(77, 87, 104)), Margin = new Thickness(58, 0, 0, 44), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom });
        canvas.Measure(new Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height)); canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); png.Save(file);
    }
}
