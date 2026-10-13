using System;
namespace Burmalk.Models;
public sealed class DiskMeasurement
{
    public string Root = "", PhysicalModel = "";
    public long VolumeBytes, Bytes;
    public DateTime TestedUtc;
    public double ReadMBps, WriteMBps;
}
