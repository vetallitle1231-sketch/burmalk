using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Burmalk.Services
{
    public class LargeFile
    {
        public string Path { get; set; }
        public double SizeMiB { get; set; }
    }
    public class StorageAnalysis
    {
        public List<LargeFile> Largest;
        public int Files,Skipped;
        public bool Limited;
        public double TotalGiB;
    }
    public static class StorageAnalyzer
    {
        public static StorageAnalysis Scan(string path,CancellationToken token)
        {
            var result=new StorageAnalysis{Largest=new List<LargeFile>()};
            var pending=new Stack<string>();pending.Push(System.IO.Path.GetFullPath(path));long bytes=0;
            while(pending.Count>0)
            {
                token.ThrowIfCancellationRequested();string folder=pending.Pop();
                try
                {
                    foreach(string file in Directory.EnumerateFiles(folder))
                    {
                        token.ThrowIfCancellationRequested();
                        try { var info=new FileInfo(file);if((info.Attributes&FileAttributes.ReparsePoint)!=0)continue;
                            bytes+=info.Length;result.Files++;result.Largest.Add(new LargeFile{Path=file,SizeMiB=info.Length/1024.0/1024});
                            if(result.Largest.Count>200)result.Largest=result.Largest.OrderByDescending(f=>f.SizeMiB).Take(100).ToList();
                        }catch{result.Skipped++;}
                        if(result.Files>=100000){result.Limited=true;break;}
                    }
                    if(result.Limited)break;
                    foreach(string dir in Directory.EnumerateDirectories(folder))
                        try{if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)==0)pending.Push(dir);}catch{result.Skipped++;}
                }catch(UnauthorizedAccessException){result.Skipped++;}catch(IOException){result.Skipped++;}
            }
            result.TotalGiB=bytes/1024.0/1024/1024;result.Largest=result.Largest.OrderByDescending(f=>f.SizeMiB).Take(100).ToList();return result;
        }
    }
}

