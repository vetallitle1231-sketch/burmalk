using System;
using System.Collections.Generic;
using System.Management;

namespace Burmalk.Services
{
    /// <summary>Thin WMI query helper. All queries are exception-guarded so that a
    /// missing/misbehaving WMI class can never crash the app.</summary>
    public static class WmiHelper
    {
        public static List<Dictionary<string, object>> Query(string wql, int timeoutMs = 8000, string scopePath = "root\\CIMV2")
        {
            var results = new List<Dictionary<string, object>>();
            try
            {
                var scope = new ManagementScope(scopePath);
                using (var selector = new ManagementObjectSearcher(scope, new SelectQuery(wql)))
                {
                    selector.Options.Timeout = new TimeSpan(0, 0, timeoutMs / 1000);
                    foreach (var obj in selector.Get())
                    {
                        var row = new Dictionary<string, object>();
                        foreach (var p in obj.Properties)
                            row[p.Name] = p.Value;
                        results.Add(row);
                        obj.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                // Surface the real CIM error (code + inner) so "Invalid query" cases
                // are diagnosable instead of being swallowed.
                string detail = ex.Message;
                if (ex is ManagementException me)
                    detail += string.Format(" [CIM error=0x{0:X} hresult=0x{1:X}]", me.ErrorCode, me.HResult);
                if (ex.InnerException != null)
                    detail += " :: " + ex.InnerException;
                ErrorLog.Write("WmiHelper.Query " + wql + ": " + detail);
            }
            return results;
        }
    }
}

