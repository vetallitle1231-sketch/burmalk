using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Burmalk.Services
{
    internal sealed class BoardFanSensor
    {
        public string Id, Name, NativeName, HardwareId;
        public Func<double> Read;
        public override string ToString() => Name;
    }

    internal static class CpuFanBinding
    {
        public static string ChannelName(string name, bool rpm)
        {
            var generic = Regex.Match(name ?? "", "^Fan\\s*#?\\s*(\\d+)$", RegexOptions.IgnoreCase);
            return generic.Success ? (rpm ? "Датчик оборотов №" : "PWM-канал №") + generic.Groups[1].Value + " · разъём неизвестен" : name;
        }
        public static bool IsPrimaryCpu(string name)
        {
            string normalized = Regex.Replace(name ?? "", "[\\s_-]", "").ToLowerInvariant();
            return normalized is "cpufan" or "cpufancontrol" or "cpufanpwmcontrol" or "cpufandccontrol";
        }
        public static (TuningSetting Control, BoardFanSensor Sensor) Resolve(
            IEnumerable<TuningSetting> controls, IEnumerable<BoardFanSensor> sensors,
            string boardId, string savedBoard, string savedControl, string savedSensor)
        {
            var controlList = controls.ToArray(); var sensorList = sensors.ToArray();
            if (!string.IsNullOrEmpty(savedBoard) && savedBoard == boardId)
            {
                var c = controlList.FirstOrDefault(s => s.Id == savedControl);
                var r = sensorList.FirstOrDefault(s => s.Id == savedSensor);
                if (c != null && r != null) return (c, r);
            }
            // PWM and RPM indices are different domains. Only explicit, unique
            // CPU_FAN labels on the same chip may be paired automatically.
            var namedControls = controlList.Where(s => IsPrimaryCpu(s.NativeName)).ToArray();
            var namedSensors = sensorList.Where(s => IsPrimaryCpu(s.NativeName)).ToArray();
            if (namedControls.Length == 1 && namedSensors.Length == 1 && namedControls[0].HardwareId == namedSensors[0].HardwareId)
                return (namedControls[0], namedSensors[0]);
            return (null, null);
        }
    }
}
