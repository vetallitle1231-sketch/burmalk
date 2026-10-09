using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Burmalk.Services
{
    public sealed class BenchmarkReport
    {
        public double CpuMillionOps,MemoryGiBs,DiskWriteMiBs,DiskReadMiBs,Seconds;
        public string DiskPath;
    }
    public static class BenchmarkRunner
    {
        private static long _sink;
        public static BenchmarkReport Run()
        {
            var total=Stopwatch.StartNew();var report=new BenchmarkReport();
            long n=1;for(int i=0;i<100000;i++)n=unchecked(n*1664525+1013904223);
            var watch=Stopwatch.StartNew();long iterations=0;
            while(watch.Elapsed.TotalSeconds<1.5){for(int i=0;i<10000;i++)n=unchecked(n*1664525+1013904223);iterations+=10000;}
            Interlocked.Exchange(ref _sink,n);report.CpuMillionOps=iterations/watch.Elapsed.TotalSeconds/1e6;
            var from=new byte[32*1024*1024];var to=new byte[from.Length];new Random(42).NextBytes(from);Buffer.BlockCopy(from,0,to,0,from.Length);
            long copies=0;watch.Restart();while(watch.Elapsed.TotalSeconds<0.5){Buffer.BlockCopy(from,0,to,0,from.Length);copies++;}
            report.MemoryGiBs=copies*(double)from.Length/watch.Elapsed.TotalSeconds/1024/1024/1024;
            if(to[17]!=from[17]||to[^1]!=from[^1])throw new IOException("Memory copy validation failed.");
            var path=Path.Combine(Path.GetTempPath(),"Burmalk-benchmark-"+Guid.NewGuid().ToString("N")+".tmp");report.DiskPath=Path.GetPathRoot(path);
            try
            {
                watch.Restart();using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024,FileOptions.WriteThrough)){file.Write(from,0,from.Length);file.Flush(true);}
                report.DiskWriteMiBs=from.Length/watch.Elapsed.TotalSeconds/1024/1024;
                watch.Restart();using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.SequentialScan)){file.ReadExactly(to);}
                report.DiskReadMiBs=from.Length/watch.Elapsed.TotalSeconds/1024/1024;
                if(!from.AsSpan().SequenceEqual(to))throw new IOException("Disk read-back validation failed.");
            }
            finally{if(File.Exists(path))File.Delete(path);}
            report.Seconds=total.Elapsed.TotalSeconds;
            if(!double.IsFinite(report.CpuMillionOps)||report.CpuMillionOps<=0||!double.IsFinite(report.MemoryGiBs)||report.MemoryGiBs<=0||!double.IsFinite(report.DiskWriteMiBs)||report.DiskWriteMiBs<=0||!double.IsFinite(report.DiskReadMiBs)||report.DiskReadMiBs<=0)throw new IOException("Invalid benchmark measurements.");
            return report;
        }
    }
}
