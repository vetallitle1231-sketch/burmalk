using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Burmalk.Models;

namespace Burmalk.Services;

internal sealed class PcModelScene
{
    public readonly Model3DGroup Root = new();
    public readonly Dictionary<GeometryModel3D, string> Parts = new();
    public readonly Dictionary<string, int> FanCounts = new();
    private readonly Color _shell, _metal = Color.FromRgb(138, 145, 155), _dark = Color.FromRgb(30, 35, 42), _pcb = Color.FromRgb(38, 42, 47), _led;
    private readonly PcModelOptions _o;
    private PcModelScene(PcModelOptions options)
    {
        _o = options; _o.Normalize();
        _shell = _o.CaseColor == "Graphite" ? Color.FromRgb(62, 68, 77) : _o.CaseColor == "Silver" ? Color.FromRgb(164, 171, 181) : Color.FromRgb(228, 231, 235);
        _led = _o.Lighting == "Teal" ? Color.FromRgb(64, 221, 213) : _o.Lighting == "Purple" ? Color.FromRgb(169, 118, 239) : _o.Lighting == "Warm" ? Color.FromRgb(250, 192, 115) : _metal;
        Root.Children.Add(new AmbientLight(Color.FromRgb(115, 121, 129)));
        Root.Children.Add(new DirectionalLight(Color.FromRgb(246, 249, 255), new Vector3D(-3, -4, -6)));
        Root.Children.Add(new DirectionalLight(Color.FromRgb(168, 181, 201), new Vector3D(4, -1, 2)));
    }
    public static PcModelScene Desktop(PcModelOptions options, bool water)
    {
        var s = new PcModelScene(options); s.BuildDesktop(water); return s;
    }
    private GeometryModel3D Add(string part, MeshGeometry3D mesh, Color color, bool emissive = false)
    {
        mesh.Freeze(); var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(122, 130, 145)), 52));
        if (emissive) material.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(100, color.R, color.G, color.B))));
        material.Freeze(); var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
        Root.Children.Add(model); if (part != null) Parts[model] = part; return model;
    }
    private void Box(string part, double x, double y, double z, double w, double h, double d, Color color) => Add(part, Cuboid(new Point3D(x, y, z), w, h, d), color);
    private void Pipe(string part, Point3D a, Point3D b, double radius, Color color) => Add(part, Tube(a, b, radius), color);
    private void BuildDesktop(bool water)
    {
        // Side-window view is +Z; the front intake is the +X face, like the reference.
        Box("case", 0, -3.0, 0, 4.6, .16, 3.7, _shell);
        foreach (double z in new[] { -1.76, 1.76 }) Box("case", 0, 3.0, z, 4.6, .16, .18, _shell);
        foreach (double x in new[] { -2.20, 2.20 }) Box("case", x, 3.0, 0, .20, .16, 3.7, _shell);
        Box("case", 0, 0, -1.79, 4.6, 6, .14, _shell);
        Box("case", -2.23, 0, -.35, .12, 6, 2.9, _shell);
        foreach (double x in new[] { -2.23, 2.23 }) foreach (double z in new[] { -1.79, 1.79 })
            Box("case", x, 0, z, .12, 6.06, .12, _shell);
        // Fine perforated front and top. Combined meshes keep draw calls small.
        Add("case", Grille(new Point3D(2.245, -.05, 0), new Vector3D(0, 1, 0), new Vector3D(0, 0, 1), 5.85, 3.45, .045, .010), Color.FromRgb(74, 80, 89));
        Add("case", Grille(new Point3D(0, 3.095, 0), new Vector3D(1, 0, 0), new Vector3D(0, 0, 1), 4.24, 3.34, .075, .014), _metal);
        Box("case", 0, -2.43, .10, 4.40, 1.08, 3.40, _shell);
        Box("case", 0, -1.85, .1, 4.35, .07, 3.35, _shell);
        Add("case", Grille(new Point3D(0, -1.805, .35), new Vector3D(1, 0, 0), new Vector3D(0, 0, 1), 3.55, 2.35, .18, .018), _dark);
        foreach (double x in new[] { -1.65, 1.65 }) foreach (double z in new[] { -1.25, 1.25 })
            Box("case", x, -3.22, z, .52, .30, .68, _dark);
        foreach (double x in new[] { -2.14, 2.14 }) foreach (double y in new[] { -2.83, 2.83 })
            Pipe("case", new Point3D(x, y, 1.805), new Point3D(x, y, 1.835), .038, _metal);
        if (_o.Lighting != "Off") Box("case", -2.14, 0, 1.83, .022, 5.45, .018, _led);

        double bh = _o.BoardSize == "ITX" ? 2.65 : _o.BoardSize == "mATX" ? 3.48 : 4.43;
        double bw = _o.BoardSize == "ITX" ? 2.65 : 3.55;
        Box("board", -.32, 2.52 - bh / 2, -1.57, bw, bh, .13, _pcb);
        Box("board", -1.76, 1.28, -1.28, .43, 2.3, .42, _dark);
        Box("board", -.73, 2.21, -1.27, 1.54, .35, .42, _dark);
        for (int i = 0; i < 13; i++) Box("board", -1.35 + i * .10, 2.23, -1.035, .025, .25, .022, _metal);
        for (int i = 0; i < 6; i++) Pipe("board", new Point3D(-1.19 + i * .17, .08, -1.455), new Point3D(-1.19 + i * .17, .19, -1.455), .043, _metal);
        for (int i = 0; i < (_o.BoardSize == "ITX" ? 1 : 3); i++) Box("board", -.4, (_o.BoardSize == "ITX" ? .01 : -.32) - i * .28, -1.45, 2.0, .09, .12, _dark);
        for (int i = 0; i < (_o.BoardSize == "ITX" ? 3 : 7); i++) Box("board", -.32 + bw / 2 - .12, .65 - i * .2, -1.43, .16, .11, .18, _dark);
        // Small traces and cables rather than manufacturer logos or model names.
        for (int i = 0; i < 10; i++) Box("board", -.72 + i * .18, .40, -1.491, .012, .32 + (i % 3) * .10, .009, Color.FromRgb(83, 87, 91));
        for (int i = 0; i < _o.RamModules; i++)
        {
            double x = .26 + i * (_o.BoardSize == "ITX" ? .21 : .265);
            Box("ram", x, 1.18, -1.0, .13, 1.82, .48, _dark);
            Box("ram", x, 2.10, -.98, .15, .045, .44, _o.Lighting == "Off" ? _metal : _led);
            Box("ram", x, 1.25, -.745, .032, 1.52, .014, _metal);
            Box("ram", x, .35, -.75, .14, .08, .06, _metal);
        }
        Box("cpu", -.68, 1.22, -1.23, .73, .73, .22, _metal);
        if (water) WaterCooler(); else AirCooler();
        if (_o.GpuFans > 0) GraphicsCard();
        for (int i = 0; i < _o.Drives; i++)
        {
            Box("disks", 1.36, -1.26 - i * .15, -.8, .71, .10, 1.32, _dark);
            Box("disks", 1.36, -1.20 - i * .15, -.8, .60, .008, 1.20, _metal);
        }
        for (int i = 0; i < _o.FrontFans; i++) Fan("front", new Point3D(2.05, Spaced(i, _o.FrontFans, 1.72) + .10, 0), new Vector3D(-1, 0, 0), .68, "cooling");
        if (_o.RearFans > 0) Fan("rear", new Point3D(-2.13, 1.66, -.18), new Vector3D(1, 0, 0), .65, "cooling");
        if (!water) for (int i = 0; i < _o.TopFans; i++) Fan("top", new Point3D(Spaced(i, _o.TopFans, 1.23), 2.75, 0), new Vector3D(0, -1, 0), .55, "cooling");
        for (int i = 0; i < _o.BottomFans; i++) Fan("bottom", new Point3D(Spaced(i, _o.BottomFans, 1.23), -1.72, .24), new Vector3D(0, 1, 0), .54, "cooling");
        // Glass is last for transparent blending; it never consumes component picking.
        if (_o.SidePanel == "Glass") Add(null, Cuboid(new Point3D(0, .54, 1.84), 4.34, 4.72, .012), Color.FromArgb(27, 148, 178, 200));
    }
    private static double Spaced(int index, int count, double distance) => (index - (count - 1) / 2d) * distance;
    private void AirCooler()
    {
        for (int i = 0; i < 20; i++) Box("cooling", -.69, .51 + i * .068, -.55, 1.15, .028, .98, _metal);
        for (int i = 0; i < 4; i++) Pipe("cooling", new Point3D(-1.01 + i * .21, .42, -.48), new Point3D(-1.01 + i * .21, 1.89, -.48), .034, Color.FromRgb(145, 125, 106));
        Fan("cpu", new Point3D(-.69, 1.16, .015), new Vector3D(0, 0, 1), .56, "cooling");
        if (_o.AirCoolerFans == 2) Fan("cpu", new Point3D(-.69, 1.16, -1.04), new Vector3D(0, 0, -1), .56, "cooling");
    }
    private void WaterCooler()
    {
        Pipe("cooling", new Point3D(-.68, 1.22, -1.05), new Point3D(-.68, 1.22, -.62), .43, _dark);
        Pipe("cooling", new Point3D(-.68, 1.22, -.62), new Point3D(-.68, 1.22, -.60), .35, _metal);
        Add("cooling", Annulus(new Point3D(-.68, 1.22, -.588), new Vector3D(0, 0, 1), .335, .03), _o.Lighting == "Off" ? _shell : _led, _o.Lighting != "Off");
        // Neutral small pump grille, reminiscent of the reference's circular block.
        for (int i = -4; i <= 4; i++) Box("cooling", -.68 + i * .058, 1.22, -.58, .018, .30 + .18 * (1 - Math.Abs(i) / 4d), .012, _dark);
        double width = _o.RadiatorFans * 1.23 + .10;
        Box("cooling", 0, 2.69, 0, width, .30, 1.39, _dark);
        Add("cooling", Grille(new Point3D(0, 2.867, 0), new Vector3D(1, 0, 0), new Vector3D(0, 0, 1), width - .07, 1.3, .065, .015), _metal);
        for (int i = 0; i < _o.RadiatorFans; i++) Fan("radiator", new Point3D(Spaced(i, _o.RadiatorFans, 1.23), 2.47, 0), new Vector3D(0, -1, 0), .55, "cooling");
        for (int side = 0; side < 2; side++)
        {
            Point3D a = new(-.38 + side * .12, 1.17, -.78), b = new(1.28 + side * .10, .96, .10), c = new(1.54 + side * .10, 2.37, .16), d = new(Math.Min(width / 2 - .10, .91) + side * .10, 2.68, -.12);
            var path = new List<MeshGeometry3D>(); Point3D previous = a;
            for (int i = 1; i <= 32; i++) { double t = i / 32d, q = 1 - t; Point3D next = new(q*q*q*a.X+3*q*q*t*b.X+3*q*t*t*c.X+t*t*t*d.X, q*q*q*a.Y+3*q*q*t*b.Y+3*q*t*t*c.Y+t*t*t*d.Y, q*q*q*a.Z+3*q*q*t*b.Z+3*q*t*t*c.Z+t*t*t*d.Z); path.Add(Tube(previous, next, .067)); previous = next; }
            Add("cooling", Merge(path), Color.FromRgb(50, 54, 59));
        }
    }
    private void GraphicsCard()
    {
        int first = Root.Children.Count;
        double length = 1.15 + _o.GpuFans * .86, x = -1.98 + length / 2;
        Box("gpu", x, -.79, -.06, length, .46, 1.63, _dark);
        Box("gpu", x, -.53, -.06, length - .08, .055, 1.60, _metal);
        Add("gpu", Grille(new Point3D(x, -.79, .769), new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), length - .10, .35, .08, .012), _metal);
        Box("gpu", x, -.61, .82, length - .10, .035, .045, _o.Lighting == "Off" ? _shell : _led);
        Box("gpu", -1.96, -.65, -.10, .055, .76, 1.68, _metal);
        for (int i = 0; i < _o.GpuFans; i++)
        {
            double cx = x + Spaced(i, _o.GpuFans, .88);
            Fan("gpu", new Point3D(cx, -1.035, -.03), new Vector3D(0, -1, 0), .38, "gpu");
        }
        if (_o.VerticalGpu)
        {
            var rotate = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), -90), x, -.79, -.06);
            rotate.Freeze(); for (int i = first; i < Root.Children.Count; i++) Root.Children[i].Transform = rotate;
        }
        Box("gpu", length / 2 + x - .18, -1.41, .63, .075, .72, .075, _dark);
    }
    private void Fan(string role, Point3D center, Vector3D normal, double radius, string part)
    {
        FanCounts[role] = FanCounts.TryGetValue(role, out int count) ? count + 1 : 1;
        Basis(normal, out var u, out var v);
        var frame = new List<MeshGeometry3D>();
        double outer = radius + .055, rail = .075;
        foreach (double side in new[] { -1d, 1d })
        { frame.Add(OrientedBox(center + u * side * outer, u, v, normal, rail, outer * 2 + rail, .15)); frame.Add(OrientedBox(center + v * side * outer, u, v, normal, outer * 2 + rail, rail, .15)); }
        Add(part, Merge(frame), _shell);
        Add(part, Annulus(center + normal * .077, normal, radius, .035), _o.Lighting == "Off" ? _metal : _led, _o.Lighting != "Off");
        Add(part, Annulus(center, normal, radius * .97, .10), _dark);
        Pipe(part, center - normal * .09, center + normal * .10, radius * .19, _metal);
        var blades = new MeshGeometry3D();
        for (int blade = 0; blade < 9; blade++)
        {
            int start = blades.Positions.Count;
            for (int step = 0; step <= 10; step++)
            {
                double t = step / 10d, r = radius * (.19 + t * .69), a = blade * Math.PI * 2 / 9 + t * .55;
                foreach (double offset in new[] { 0d, .13 + .27 * t }) { blades.Positions.Add(center + (u * Math.Cos(a + offset) + v * Math.Sin(a + offset)) * r + normal * .061); blades.Normals.Add(normal); }
                if (step > 0) foreach (int k in new[] { 0, 3, 1, 0, 2, 3 }) blades.TriangleIndices.Add(start + (step - 1) * 2 + k);
            }
        }
        Add(part, blades, Color.FromRgb(89, 95, 104));
        foreach (double a in new[] { -1d, 1d }) foreach (double b in new[] { -1d, 1d })
            Pipe(part, center + u*a*outer + v*b*outer + normal*.077, center + u*a*outer + v*b*outer + normal*.092, .025, _dark);
    }
    private static void Basis(Vector3D normal, out Vector3D u, out Vector3D v)
    { normal.Normalize(); u = Vector3D.CrossProduct(Math.Abs(normal.Y) < .9 ? new Vector3D(0, 1, 0) : new Vector3D(0, 0, 1), normal); u.Normalize(); v = Vector3D.CrossProduct(normal, u); }
    private static MeshGeometry3D Annulus(Point3D center, Vector3D normal, double radius, double width)
    {
        Basis(normal, out var u, out var v); var m = new MeshGeometry3D();
        for (int i = 0; i < 64; i++) { int start = m.Positions.Count; foreach (var p in new[] { (i, radius), (i, radius-width), (i+1, radius-width), (i+1, radius) }) { double a = p.Item1*Math.PI*2/64; m.Positions.Add(center+(u*Math.Cos(a)+v*Math.Sin(a))*p.Item2); m.Normals.Add(normal); } foreach (int k in new[] { 0,2,1,0,3,2 }) m.TriangleIndices.Add(start+k); } return m;
    }
    private static MeshGeometry3D Grille(Point3D center, Vector3D u, Vector3D v, double width, double height, double spacing, double thickness)
    {
        var strips = new List<MeshGeometry3D>(); var normal = Vector3D.CrossProduct(u, v);
        for (double x = -width/2; x <= width/2; x += spacing) strips.Add(OrientedBox(center+u*x, u,v,normal, thickness,height,.015));
        for (double y = -height/2; y <= height/2; y += spacing) strips.Add(OrientedBox(center+v*y, u,v,normal, width,thickness,.015));
        return Merge(strips);
    }
    private static MeshGeometry3D OrientedBox(Point3D center, Vector3D u, Vector3D v, Vector3D n, double w, double h, double d)
    {
        var m = Cuboid(new Point3D(), w,h,d);
        for (int i = 0; i < m.Positions.Count; i++) { var p=m.Positions[i]; m.Positions[i]=center+u*p.X+v*p.Y+n*p.Z; var a=m.Normals[i]; m.Normals[i]=u*a.X+v*a.Y+n*a.Z; } return m;
    }
    private static MeshGeometry3D Merge(IEnumerable<MeshGeometry3D> meshes)
    {
        var m = new MeshGeometry3D(); foreach (var source in meshes) { int start=m.Positions.Count; foreach(var p in source.Positions)m.Positions.Add(p); foreach(var n in source.Normals)m.Normals.Add(n); foreach(int i in source.TriangleIndices)m.TriangleIndices.Add(start+i); } return m;
    }
    private static MeshGeometry3D Cuboid(Point3D c, double w, double h, double d)
    {
        var m=new MeshGeometry3D(); var points=new[]{new Point3D(-w/2,-h/2,-d/2),new Point3D(w/2,-h/2,-d/2),new Point3D(w/2,h/2,-d/2),new Point3D(-w/2,h/2,-d/2),new Point3D(-w/2,-h/2,d/2),new Point3D(w/2,-h/2,d/2),new Point3D(w/2,h/2,d/2),new Point3D(-w/2,h/2,d/2)};
        int[][] faces={new[]{4,5,6,7},new[]{1,0,3,2},new[]{0,4,7,3},new[]{5,1,2,6},new[]{3,7,6,2},new[]{0,1,5,4}};
        Vector3D[] ns={new(0,0,1),new(0,0,-1),new(-1,0,0),new(1,0,0),new(0,1,0),new(0,-1,0)};
        for(int f=0;f<6;f++){int start=m.Positions.Count;foreach(int i in faces[f]){m.Positions.Add(c+(Vector3D)points[i]);m.Normals.Add(ns[f]);}foreach(int i in new[]{0,1,2,0,2,3})m.TriangleIndices.Add(start+i);}return m;
    }
    private static MeshGeometry3D Tube(Point3D a, Point3D b, double radius)
    {
        var n=b-a; n.Normalize(); Basis(n,out var u,out var v); var m=new MeshGeometry3D();
        for(int i=0;i<=24;i++){double angle=i*Math.PI*2/24;var normal=u*Math.Cos(angle)+v*Math.Sin(angle);m.Positions.Add(a+normal*radius);m.Positions.Add(b+normal*radius);m.Normals.Add(normal);m.Normals.Add(normal);if(i>0)foreach(int k in new[]{0,3,1,0,2,3})m.TriangleIndices.Add((i-1)*2+k);}
        foreach(bool top in new[]{false,true}){var center=top?b:a;var normal=top?n:-n;int start=m.Positions.Count;m.Positions.Add(center);m.Normals.Add(normal);for(int i=0;i<=24;i++){double angle=i*Math.PI*2/24;m.Positions.Add(center+(u*Math.Cos(angle)+v*Math.Sin(angle))*radius);m.Normals.Add(normal);}for(int i=0;i<24;i++){m.TriangleIndices.Add(start);m.TriangleIndices.Add(start+i+(top?1:2));m.TriangleIndices.Add(start+i+(top?2:1));}}return m;
    }
}
