using System;
using System.Windows.Media.Media3D;

namespace Burmalk.Services;

// Decorative movement only. Never interprets or sets a real fan's RPM.
internal sealed class PcModelRotor
{
    public readonly string Role;
    public readonly AxisAngleRotation3D Rotation;
    public PcModelRotor(string role, AxisAngleRotation3D rotation) { Role = role; Rotation = rotation; }
    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        double speed = Role == "gpu" ? 165 : Role == "cpu" ? 130 : 100;
        Rotation.Angle = (Rotation.Angle + Math.Min(seconds, .2) * speed) % 360;
    }
}
