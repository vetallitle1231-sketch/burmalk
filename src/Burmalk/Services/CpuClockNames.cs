using System;
using System.Text.RegularExpressions;

namespace Burmalk.Services;

internal static class CpuClockNames
{
    public static bool IsCore(string name) =>
        string.Equals(name,"Cores (Average)",StringComparison.OrdinalIgnoreCase)||
        Regex.IsMatch(name??"",@"^(?:CPU )?Core\s*#?\d+(?:\s|$)",RegexOptions.IgnoreCase);
}
