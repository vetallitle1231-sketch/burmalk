using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;

namespace Burmalk.Services
{
    public class NetworkSampler : IDisposable
    {
        private readonly object _gate=new object();
        private Dictionary<string,(long Down,long Up,long Time)> _previous=new Dictionary<string,(long,long,long)>();
        public bool Initialized {get;private set;}
        public void Init(){lock(_gate){Initialized=true;Sample();}}
        public NetSample Sample()
        {
            lock(_gate)
            {
                var s=new NetSample();if(!Initialized)return s;
                var next=new Dictionary<string,(long,long,long)>();long now=Stopwatch.GetTimestamp();double down=0,up=0;bool measured=false;
                try
                {
                    foreach(var ni in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if(ni.OperationalStatus!=OperationalStatus.Up||ni.NetworkInterfaceType==NetworkInterfaceType.Loopback||ni.NetworkInterfaceType==NetworkInterfaceType.Tunnel)continue;
                        try
                        {
                            var stats=ni.GetIPStatistics();next[ni.Id]=(stats.BytesReceived,stats.BytesSent,now);
                            if(_previous.TryGetValue(ni.Id,out var old))
                            {
                                double elapsed=(now-old.Time)/(double)Stopwatch.Frequency;
                                if(elapsed<=0.05)continue;
                                long d=stats.BytesReceived-old.Down,u=stats.BytesSent-old.Up;
                                if(d<0||u<0)continue;
                                down+=d/elapsed;up+=u/elapsed;measured=true;
                            }
                        }catch{}
                    }
                }catch{}
                _previous=next;
                if(measured){s.DownKBps=down/1024;s.UpKBps=up/1024;}
                return s;
            }
        }
        public IEnumerable<string> AdapterNames {get{try{return NetworkInterface.GetAllNetworkInterfaces().Where(a=>a.OperationalStatus==OperationalStatus.Up).Select(a=>a.Name).ToList();}catch{return Array.Empty<string>();}}}
        public void Dispose(){lock(_gate){Initialized=false;_previous.Clear();}}
    }
    public class NetSample
    {
        public double DownKBps=double.NaN;
        public double UpKBps=double.NaN;
    }
}

