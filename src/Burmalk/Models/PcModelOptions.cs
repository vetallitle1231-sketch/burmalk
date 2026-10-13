using System;
using System.Text.Json.Serialization;

namespace Burmalk.Models;

// Appearance presets only; never used to identify sensors or control real fans.
public sealed class PcModelOptions
{
    public string CaseColor = "White", SidePanel = "Open", BoardSize = "ATX", Lighting = "Off";
    public int GpuFans = 2, RamModules = 2, FrontFans = 2, RearFans = 1, TopFans = 2, BottomFans = 0;
    public int AirCoolerFans = 1, RadiatorFans = 2, Drives = 2;
    public bool VerticalGpu;
    public void Normalize()
    {
        if (CaseColor is not ("White" or "Graphite" or "Silver")) CaseColor = "White";
        if (SidePanel is not ("Open" or "Glass")) SidePanel = "Open";
        if (BoardSize is not ("ATX" or "mATX" or "ITX")) BoardSize = "ATX";
        if (Lighting is not ("Off" or "Teal" or "Purple" or "Warm")) Lighting = "Off";
        GpuFans = Math.Clamp(GpuFans, 0, 3); RamModules = Math.Clamp(RamModules, 0, 4);
        FrontFans = Math.Clamp(FrontFans, 0, 3); RearFans = Math.Clamp(RearFans, 0, 1);
        TopFans = Math.Clamp(TopFans, 0, 3); BottomFans = Math.Clamp(BottomFans, 0, 3);
        AirCoolerFans = Math.Clamp(AirCoolerFans, 1, 2); RadiatorFans = Math.Clamp(RadiatorFans, 1, 3);
        Drives = Math.Clamp(Drives, 0, 4);
    }
    [JsonIgnore]
    public string Signature => $"{CaseColor}/{SidePanel}/{BoardSize}/{Lighting}/{GpuFans}/{RamModules}/{FrontFans}/{RearFans}/{TopFans}/{BottomFans}/{AirCoolerFans}/{RadiatorFans}/{Drives}/{VerticalGpu}";
}
