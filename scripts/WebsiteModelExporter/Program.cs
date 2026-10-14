using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Burmalk.Models;
using Burmalk.Services;

// Export application-owned geometry without creating a window or sampling hardware.
var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
var scene=PcModelScene.Desktop(PcModelOptions.CompleteTower(), false);
var groups=new Dictionary<string,Mesh>();
RotateTransform3D Find(Transform3D t, AxisAngleRotation3D angle)
{
    if(t is RotateTransform3D r && ReferenceEquals(r.Rotation, angle))return r;
    if(t is Transform3DGroup group)foreach(var child in group.Children){var result=Find(child,angle);if(result!=null)return result;}
    return null;
}
foreach(var node in scene.Root.Children.OfType<GeometryModel3D>())
{
    var geometry=(MeshGeometry3D)node.Geometry;
    var material=(MaterialGroup)node.Material;
    var color=((SolidColorBrush)material.Children.OfType<DiffuseMaterial>().First().Brush).Color;
    string part=scene.Parts.GetValueOrDefault(node,"case");
    var rotor=scene.Rotors.FirstOrDefault(r=>Find(node.Transform,r.Rotation)!=null);
    var center=new Point3D();var axis=new Vector3D();
    if(rotor!=null){var r=Find(node.Transform,rotor.Rotation);center=node.Transform.Transform(new Point3D(r.CenterX,r.CenterY,r.CenterZ));axis=node.Transform.Transform(rotor.Rotation.Axis);axis.Normalize();}
    bool glow=material.Children.OfType<EmissiveMaterial>().Any();
    string key=part+"/"+color+"/"+glow+(rotor==null?"":"/rotor-"+groups.Count);
    if(!groups.TryGetValue(key,out var mesh)){mesh=new Mesh { Part=part, Color=new[]{color.R/255f,color.G/255f,color.B/255f,color.A/255f},Glow=glow,Role=rotor?.Role,Center=new[]{(float)center.X,(float)center.Y,(float)center.Z},Axis=new[]{(float)axis.X,(float)axis.Y,(float)axis.Z} };groups.Add(key,mesh);}
    int first=mesh.Positions.Count/3;
    foreach(var p in geometry.Positions){var point=node.Transform.Transform(p)-center;mesh.Positions.AddRange(new[]{(float)point.X,(float)point.Y,(float)point.Z});}
    foreach(var n in geometry.Normals){var normal=node.Transform.Transform(n);normal.Normalize();mesh.Normals.AddRange(new[]{(float)normal.X,(float)normal.Y,(float)normal.Z});}
    foreach(var index in geometry.TriangleIndices)mesh.Indices.Add((uint)(first+index));
}
using(var writer=new BinaryWriter(File.Create(Path.Combine(output,"pc.bin"))))
{
    foreach(var mesh in groups.Values)
    {
        mesh.PositionRange=new[]{(int)writer.BaseStream.Position,mesh.Positions.Count};foreach(var v in mesh.Positions)writer.Write(v);
        mesh.NormalRange=new[]{(int)writer.BaseStream.Position,mesh.Normals.Count};foreach(var v in mesh.Normals)writer.Write(v);
        mesh.IndexRange=new[]{(int)writer.BaseStream.Position,mesh.Indices.Count};foreach(var v in mesh.Indices)writer.Write(v);
    }
}
var manifest=new{version="1.1.11",source="PcModelScene.Desktop(PcModelOptions.CompleteTower(), false)",vertices=groups.Values.Sum(m=>m.Positions.Count/3),triangles=groups.Values.Sum(m=>m.Indices.Count/3),meshes=groups.Values.Select(m=>new{part=m.Part,color=m.Color,glow=m.Glow,position=m.PositionRange,normal=m.NormalRange,index=m.IndexRange,rotor=m.Role==null?null:new{role=m.Role,center=m.Center,axis=m.Axis}})};
File.WriteAllText(Path.Combine(output,"pc.json"),JsonSerializer.Serialize(manifest));
Console.WriteLine($"Exported actual Burmalk model: {manifest.vertices} vertices, {manifest.triangles} triangles, {groups.Count} draw groups, {scene.Rotors.Count} rotors.");
class Mesh
{
    public string Part,Role;public float[]Color,Center,Axis;public bool Glow;public List<float>Positions=new(),Normals=new();public List<uint>Indices=new();public int[]PositionRange,NormalRange,IndexRange;
}
