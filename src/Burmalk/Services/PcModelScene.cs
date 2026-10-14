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
    public readonly List<PcModelRotor> Rotors = new();
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
        // Connector blocks and routed power leads tie the board into the complete build.
        double boardRight = -.32 + bw / 2;
        Box("board", boardRight - .07, 1.12, -1.31, .15, .53, .22, _dark);
        for (int wire = 0; wire < 4; wire++)
        {
            double y = .97 + wire * .075;
            Curve("board", new Point3D(boardRight, y, -1.21), new Point3D(boardRight + .26, y, -1.07), new Point3D(1.70, y - .16, -1.16), new Point3D(1.73, .32 + wire * .035, -1.62), .022, Color.FromRgb((byte)(75 + wire * 7), (byte)(80 + wire * 7), (byte)(90 + wire * 7)));
        }
        Box("board", -1.26, 2.46, -1.29, .36, .11, .20, _dark);
        for (int wire = 0; wire < 3; wire++)
            Curve("board", new Point3D(-1.37 + wire * .11, 2.48, -1.20), new Point3D(-1.60 + wire * .11, 2.74, -1.18), new Point3D(-1.86 + wire * .07, 2.80, -1.27), new Point3D(-1.93, 2.66, -1.60), .022, Color.FromRgb(58, 65, 75));
        if (_o.Drives > 0)
        {
            Box("disks", -.39, .13, -1.33, 1.32, .21, .13, _dark);
            var m2Fins = new List<MeshGeometry3D>();
            for (int i = 0; i < 17; i++) m2Fins.Add(Cuboid(new Point3D(-.98 + i * .071, .13, -1.25), .025, .16, .025));
            Add("disks", Merge(m2Fins), _metal);
        }
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
        for (int i = 0; i < Math.Max(0, _o.Drives - 1); i++)
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
        for (int i = 0; i < 20; i++) Box("cpu", -.69, .51 + i * .068, -.55, 1.15, .028, .98, _metal);
        for (int i = 0; i < 4; i++) Pipe("cpu", new Point3D(-1.01 + i * .21, .42, -.48), new Point3D(-1.01 + i * .21, 1.89, -.48), .034, Color.FromRgb(145, 125, 106));
        Fan("cpu", new Point3D(-.69, 1.16, .015), new Vector3D(0, 0, 1), .56, "cpu");
        if (_o.AirCoolerFans == 2) Fan("cpu", new Point3D(-.69, 1.16, -1.04), new Vector3D(0, 0, -1), .56, "cpu");
    }
    private void WaterCooler()
    {
        Pipe("cpu", new Point3D(-.68, 1.22, -1.05), new Point3D(-.68, 1.22, -.62), .43, _dark);
        Pipe("cpu", new Point3D(-.68, 1.22, -.62), new Point3D(-.68, 1.22, -.60), .35, _metal);
        Add("cpu", Annulus(new Point3D(-.68, 1.22, -.588), new Vector3D(0, 0, 1), .335, .03), _o.Lighting == "Off" ? _shell : _led, _o.Lighting != "Off");
        // Neutral small pump grille, reminiscent of the reference's circular block.
        for (int i = -4; i <= 4; i++) Box("cpu", -.68 + i * .058, 1.22, -.58, .018, .30 + .18 * (1 - Math.Abs(i) / 4d), .012, _dark);
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
        double length = .55 + _o.GpuFans * 1.02, x = -1.97 + length / 2;
        var graphite = Color.FromRgb(55, 61, 70); var plate = Color.FromRgb(82, 90, 102);
        // The heatsink is open at the sides: individual fins, not a painted grille on a box.
        var fins = new List<MeshGeometry3D>();
        for (double fx = -1.84; fx < -1.97 + length - .10; fx += .042)
            fins.Add(Cuboid(new Point3D(fx, -.79, -.035), .009, .34, 1.24));
        Add("gpu", Merge(fins), Color.FromRgb(172, 181, 191));
        Box("gpu", x, -.55, -.21, length - .15, .035, 1.16, Color.FromRgb(37, 43, 47));
        Add("gpu", BeveledPrism(new Point3D(x, -.48, -.035), length, .085, 1.34, .13), plate);
        // Brushed backplate seams, captive screws and diagonal raised ribs.
        foreach (double fx in new[] { -1.72, x, -1.97 + length - .22 }) foreach (double z in new[] { -.51, .46 })
            Pipe("gpu", new Point3D(fx, -.431, z), new Point3D(fx, -.415, z), .025, _dark);
        for (int i = 0; i < _o.GpuFans * 3; i++)
            Pipe("gpu", new Point3D(-1.70 + i * .30, -.43, -.46), new Point3D(-1.59 + i * .30, -.43, .35), .012, Color.FromRgb(114, 123, 136));
        // Four heatpipes run through the fin stack and curl at the far end.
        for (int i = 0; i < 4; i++)
        {
            double z = -.42 + i * .265;
            Curve("gpu", new Point3D(-1.76, -.95, z), new Point3D(x, -.96, z), new Point3D(-1.97 + length - .08, -.96, z), new Point3D(-1.97 + length - .12, -.67, z), .035, Color.FromRgb(146, 127, 110));
        }
        // A separate angular shroud surrounds circular fan openings.
        foreach (double z in new[] { -.68, .63 })
        {
            Add("gpu", BeveledPrism(new Point3D(x, -1.065, z), length, .16, .10, .035), graphite);
            Add("gpu", BeveledPrism(new Point3D(x, -.59, z), length, .13, .10, .035), graphite);
        }
        foreach (double end in new[] { -1.97 + .1375, -1.97 + length - .1375 })
            Add("gpu", BeveledPrism(new Point3D(end, -1.115, -.025), .275, .075, 1.34, .08), graphite);
        for (int i = 0; i < _o.GpuFans; i++)
        {
            double cx = x + Spaced(i, _o.GpuFans, 1.02);
            Add("gpu", FanPanel(new Point3D(cx, -1.153, -.025), .51, .67, .485), graphite);
            Fan("gpu", new Point3D(cx, -1.075, -.025), new Vector3D(0, -1, 0), .49, "gpu", true);
        }
        // Angular metallic shoulder lines make the fan face read as a GPU shroud.
        foreach (double side in new[] { -1d, 1d })
        {
            Pipe("gpu", new Point3D(-1.77, -1.167, -.025 + side * .60), new Point3D(-1.97 + length - .20, -1.167, -.025 + side * .60), .012, Color.FromRgb(135, 146, 162));
            Pipe("gpu", new Point3D(-1.85, -1.167, -.025 + side * .42), new Point3D(-1.77, -1.167, -.025 + side * .60), .018, Color.FromRgb(104, 114, 130));
            Pipe("gpu", new Point3D(-1.97 + length - .20, -1.167, -.025 + side * .60), new Point3D(-1.97 + length - .11, -1.167, -.025 + side * .42), .018, Color.FromRgb(104, 114, 130));
        }
        // Side trim is deliberately short; most of the radiator remains exposed.
        Add("gpu", BeveledPrism(new Point3D(x - length * .12, -.69, .693), length * .54, .15, .042, .015), Color.FromRgb(101, 111, 126));
        Box("gpu", x - length * .12, -.605, .722, length * .50, .025, .018, _o.Lighting == "Off" ? Color.FromRgb(198, 206, 216) : _led);
        // PCB connector and a slotted metal I/O bracket with three recessed ports.
        Box("gpu", x - length * .13, -.535, -.705, length * .58, .025, .24, Color.FromRgb(38, 43, 46));
        Box("gpu", x - length * .13, -.532, -.837, length * .48, .018, .050, Color.FromRgb(185, 152, 88));
        for (int i = 0; i < 16; i++) Box("gpu", x - length * .37 + i * length * .03, -.52, -.838, .011, .006, .052, _dark);
        Box("gpu", -1.99, -.80, -.06, .045, .81, 1.63, _metal);
        Add("gpu", Grille(new Point3D(-2.017, -.92, -.035), new Vector3D(0, 1, 0), new Vector3D(0, 0, 1), .22, 1.34, .06, .016), _dark);
        foreach (double z in new[] { -.48, -.07, .34 }) Box("gpu", -2.019, -.58, z, .018, .115, .30, _dark);
        // Power connector belongs to the rotated card; its sleeved cable stays in case space.
        var plug = new Point3D(x + length * .21, -.65, .79);
        Add("gpu", BeveledPrism(plug, .34, .20, .22, .025), _dark);
        for (int i = 0; i < 4; i++) Box("gpu", plug.X - .12 + i * .08, plug.Y + .104, plug.Z, .044, .012, .105, Color.FromRgb(116, 123, 133));
        Transform3D rotation = Transform3D.Identity;
        if (_o.VerticalGpu)
        {
            rotation = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), -90), x, -.79, -.06);
            rotation.Freeze();
            for (int i = first; i < Root.Children.Count; i++)
            {
                var transforms = new Transform3DGroup();
                transforms.Children.Add(Root.Children[i].Transform); transforms.Children.Add(rotation);
                Root.Children[i].Transform = transforms;
            }
        }
        var cableStart = rotation.Transform(plug + new Vector3D(0, 0, .12));
        for (int i = 0; i < 6; i++)
        {
            var a = cableStart + new Vector3D((i - 2.5) * .034, 0, 0);
            var b = _o.VerticalGpu ? new Point3D(-1.97 + length + .16, a.Y + .12, a.Z + .25) : a + new Vector3D(.16, -.20, .27);
            var c = _o.VerticalGpu ? new Point3D(-1.97 + length + .22 + i * .024, -1.05, 1.10) : new Point3D(1.46 + i * .027, -1.05, 1.10);
            Curve("gpu", a, b, c, new Point3D(1.50 + i * .027, -1.82, .70), .017, i % 2 == 0 ? Color.FromRgb(55, 61, 70) : Color.FromRgb(78, 84, 94));
        }
        Box("gpu", 1.53, -1.60, .91, .26, .045, .13, _dark);
        if (!_o.VerticalGpu)
        {
            Pipe("gpu", new Point3D(-1.97 + length - .18, -1.78, .63), new Point3D(-1.97 + length - .18, -1.02, .63), .026, _metal);
            Box("gpu", -1.97 + length - .18, -1.01, .63, .23, .06, .24, _dark);
        }
    }
    private void Fan(string role, Point3D center, Vector3D normal, double radius, string part, bool integrated = false)
    {
        FanCounts[role] = FanCounts.TryGetValue(role, out int count) ? count + 1 : 1;
        Basis(normal, out var u, out var v);
        var frame = new List<MeshGeometry3D>();
        double outer = radius + .055, rail = .075;
        foreach (double side in new[] { -1d, 1d })
        { frame.Add(OrientedBox(center + u * side * outer, u, v, normal, rail, outer * 2 + rail, .15)); frame.Add(OrientedBox(center + v * side * outer, u, v, normal, outer * 2 + rail, rail, .15)); }
        if (!integrated) Add(part, Merge(frame), _shell);
        Add(part, Annulus(center + normal * .077, normal, radius, integrated ? .022 : .035), _o.Lighting == "Off" ? integrated ? Color.FromRgb(100, 109, 122) : _metal : _led, _o.Lighting != "Off");
        Add(part, Annulus(center, normal, radius * .97, .10), _dark);
        Pipe(part, center - normal * .09, center + normal * .10, radius * .19, integrated ? Color.FromRgb(62, 69, 79) : _metal);
        var blades = new MeshGeometry3D();
        int bladeCount = integrated ? 11 : 9;
        for (int blade = 0; blade < bladeCount; blade++)
        {
            int start = blades.Positions.Count;
            for (int step = 0; step <= 10; step++)
            {
                double t = step / 10d, r = radius * (.19 + t * .69), a = blade * Math.PI * 2 / bladeCount + t * (integrated ? .36 : .55);
                foreach (double offset in new[] { 0d, .13 + .27 * t }) { blades.Positions.Add(center + (u * Math.Cos(a + offset) + v * Math.Sin(a + offset)) * r + normal * .061); blades.Normals.Add(normal); }
                if (step > 0) foreach (int k in new[] { 0, 3, 1, 0, 2, 3 }) blades.TriangleIndices.Add(start + (step - 1) * 2 + k);
            }
        }
        var rotor = Add(part, blades, Color.FromRgb(89, 95, 104));
        var angle = new AxisAngleRotation3D(normal, 0);
        rotor.Transform = new RotateTransform3D(angle, center.X, center.Y, center.Z);
        Rotors.Add(new PcModelRotor(role, angle));
        if (integrated) Add(part, Annulus(center + normal * .105, normal, radius * .13, .016), Color.FromRgb(137, 146, 159));
        else foreach (double a in new[] { -1d, 1d }) foreach (double b in new[] { -1d, 1d })
                Pipe(part, center + u*a*outer + v*b*outer + normal*.077, center + u*a*outer + v*b*outer + normal*.092, .025, _dark);
    }
    private void Curve(string part, Point3D a, Point3D b, Point3D c, Point3D d, double radius, Color color)
    {
        var segments = new List<MeshGeometry3D>(); var previous = a;
        for (int i = 1; i <= 24; i++)
        {
            double t = i / 24d, q = 1 - t;
            var next = new Point3D(q*q*q*a.X + 3*q*q*t*b.X + 3*q*t*t*c.X + t*t*t*d.X, q*q*q*a.Y + 3*q*q*t*b.Y + 3*q*t*t*c.Y + t*t*t*d.Y, q*q*q*a.Z + 3*q*q*t*b.Z + 3*q*t*t*c.Z + t*t*t*d.Z);
            segments.Add(Tube(previous, next, radius)); previous = next;
        }
        Add(part, Merge(segments), color);
    }
    private static MeshGeometry3D FanPanel(Point3D center, double halfWidth, double halfDepth, double hole)
    {
        var mesh = new MeshGeometry3D();
        for (int i = 0; i < 64; i++)
        {
            int start = mesh.Positions.Count;
            foreach (var sample in new[] { (i, true), (i, false), (i + 1, false), (i + 1, true) })
            {
                double angle = sample.Item1 * Math.PI * 2 / 64, c = Math.Cos(angle), s = Math.Sin(angle);
                double r = sample.Item2 ? Math.Min(halfWidth / Math.Max(Math.Abs(c), 1e-8), halfDepth / Math.Max(Math.Abs(s), 1e-8)) : hole;
                mesh.Positions.Add(center + new Vector3D(c*r, 0, s*r)); mesh.Normals.Add(new Vector3D(0, -1, 0));
            }
            foreach (int j in new[] { 0, 2, 1, 0, 3, 2 }) mesh.TriangleIndices.Add(start + j);
        }
        return mesh;
    }
    private static MeshGeometry3D BeveledPrism(Point3D center, double width, double height, double depth, double bevel)
    {
        double w = width / 2, d = depth / 2; bevel = Math.Min(bevel, Math.Min(w, d) * .8);
        var outline = new[] { new Vector3D(-w + bevel, 0, -d), new Vector3D(w - bevel, 0, -d), new Vector3D(w, 0, -d + bevel), new Vector3D(w, 0, d - bevel), new Vector3D(w - bevel, 0, d), new Vector3D(-w + bevel, 0, d), new Vector3D(-w, 0, d - bevel), new Vector3D(-w, 0, -d + bevel) };
        var mesh = new MeshGeometry3D();
        for (int i = 0; i < outline.Length; i++)
        {
            int j = (i + 1) % outline.Length;
            foreach (bool top in new[] { false, true })
            {
                int start = mesh.Positions.Count; var axis = new Vector3D(0, top ? height / 2 : -height / 2, 0);
                foreach (var p in top ? new[] { new Vector3D(), outline[j], outline[i] } : new[] { new Vector3D(), outline[i], outline[j] }) { mesh.Positions.Add(center + axis + p); mesh.Normals.Add(new Vector3D(0, top ? 1 : -1, 0)); }
                foreach (int k in new[] { 0, 1, 2 }) mesh.TriangleIndices.Add(start + k);
            }
            var edge = outline[j] - outline[i]; var normal = new Vector3D(edge.Z, 0, -edge.X); normal.Normalize(); int side = mesh.Positions.Count;
            foreach (var p in new[] { outline[i] + new Vector3D(0, -height / 2, 0), outline[i] + new Vector3D(0, height / 2, 0), outline[j] + new Vector3D(0, height / 2, 0), outline[j] + new Vector3D(0, -height / 2, 0) }) { mesh.Positions.Add(center + p); mesh.Normals.Add(normal); }
            foreach (int k in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(side + k);
        }
        return mesh;
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
