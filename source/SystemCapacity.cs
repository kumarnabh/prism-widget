using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Net.NetworkInformation;

namespace Prism;

// Values are immutable snapshots. Unsupported, failed and aged values are never zero-filled.
public sealed record SystemMetric(string Id,string Label,double? Value,string Unit,double? Percent,DateTimeOffset At,string Status="Current",string Details="")
{
    public bool Current(DateTimeOffset now)=>Status=="Current"&&Value is double v&&double.IsFinite(v)&&v>=0&&(!Percent.HasValue||double.IsFinite(Percent.Value)&&Percent.Value is >=0 and <=100)&&now>=At&&now-At<=TimeSpan.FromSeconds(20);
    public string Display(DateTimeOffset now)=>Current(now)?$"{Value:0.#}{Unit}":"—";
    public string State(DateTimeOffset now)=>Status=="Current"&&!Current(now)?"Stale":Status;
}
public sealed record GpuAdapter(string Id,string Name,ulong DedicatedBytes);
public sealed record DiskCapacity(string Name,long Free,long Total,DateTimeOffset At=default);
public sealed record HardwareSnapshot(SystemMetric[] Metrics,GpuAdapter[] Adapters,DiskCapacity[] Disks,DateTimeOffset At,double SampleMilliseconds)
{
    public static readonly HardwareSnapshot Empty=new(Array.Empty<SystemMetric>(),Array.Empty<GpuAdapter>(),Array.Empty<DiskCapacity>(),DateTimeOffset.MinValue,0);
}
public sealed class SystemCapacity:IDisposable
{
    readonly CancellationTokenSource stop=new();
    HardwareSnapshot latest=HardwareSnapshot.Empty;
    ulong idle,kernel,user;long received,sent;DateTimeOffset networkAt,cpuAt;
    public HardwareSnapshot Latest=>Volatile.Read(ref latest);
    public SystemCapacity()=>_ = Task.Run(Loop);
    async Task Loop()
    {
        PdhCounters? counters=null;DateTimeOffset slowAt=default,inventoryAt=default,powerAt=default,retryAt=default;
        var adapters=Array.Empty<GpuAdapter>();var disks=Array.Empty<DiskCapacity>();var slow=new List<SystemMetric>();var powerMetrics=new List<SystemMetric>();
        try{while(!stop.IsCancellationRequested){
            var watch=Stopwatch.StartNew();var now=DateTimeOffset.UtcNow;var metrics=new List<SystemMetric>();
            metrics.AddRange(Basic(now));
            if(now-inventoryAt>=TimeSpan.FromSeconds(30)){
                inventoryAt=now;try{adapters=GpuInventory.Read();}catch{adapters=Array.Empty<GpuAdapter>();}
                try{disks=DriveInfo.GetDrives().Where(d=>d.DriveType==DriveType.Fixed).Select(d=>{try{return d.IsReady&&d.TotalSize>0?new DiskCapacity(d.Name,d.AvailableFreeSpace,d.TotalSize,now):null;}catch{return null;}}).OfType<DiskCapacity>().ToArray();}catch{disks=Array.Empty<DiskCapacity>();}
            }
            if(now-slowAt>=TimeSpan.FromSeconds(5)){
            if(slowAt!=default&&now-slowAt>TimeSpan.FromSeconds(20)){counters?.Dispose();counters=null;retryAt=default;}
            slowAt=now;slow.Clear();
            // Retry unavailable counters periodically; each query is owned by this one worker.
            if(now>=retryAt&&(counters is null||counters.Incomplete)){try{if(counters is null)counters=new();else counters.AddMissing();}catch{counters?.Dispose();counters=null;}retryAt=now.AddMinutes(1);}
            Dictionary<string,double> engines=new(),memory=new(),reads=new(),writes=new();
            try{if(counters is not null){counters.Collect();engines=counters.Read(0);memory=counters.Read(1);reads=counters.Read(2);writes=counters.Read(3);}}catch{counters?.Dispose();counters=null;}
            foreach(var adapter in adapters){
                double? utilization=GpuUsage(engines,adapter.Id);
                slow.Add(new("gpu:"+adapter.Id,adapter.Name,utilization,"%",utilization,now,utilization.HasValue?"Current":"Unavailable"));
                var matches=memory.Where(p=>p.Key.Contains(adapter.Id,StringComparison.OrdinalIgnoreCase)).ToArray();
                double? used=matches.Length==1&&adapter.DedicatedBytes>0&&matches[0].Value<=adapter.DedicatedBytes?matches[0].Value:null;
                slow.Add(new("vram:"+adapter.Id,"Dedicated VRAM",used/1073741824d," GB",used.HasValue?used/adapter.DedicatedBytes*100:null,now,used.HasValue?"Current":"Unavailable",adapter.DedicatedBytes>0?$"{adapter.DedicatedBytes/1073741824d:0.#} GB total"+(used.HasValue?$" · {(adapter.DedicatedBytes-used.Value)/1073741824d:0.#} GB free":""):""));
            }
            slow.Add(AggregateGpu(slow,now));
            var largest=adapters.OrderByDescending(a=>a.DedicatedBytes).FirstOrDefault();
            var vram=largest is null?null:slow.FirstOrDefault(m=>m.Id=="vram:"+largest.Id);
            slow.Add(vram is null?new("vram","Dedicated VRAM",null," GB",null,now,"Unavailable"):vram with{Id="vram",Details=largest!.Name+" · "+vram.Details});
            foreach(var (id,label,values) in new[]{("read","Disk read",reads),("write","Disk write",writes)}){
                double? value=values.TryGetValue("_Total",out var total)?total/1048576d:null;
                slow.Add(new(id,label,value," MB/s",null,now,value.HasValue?"Current":"Unavailable"));
            }
            }
            metrics.AddRange(slow);
            if(now-powerAt>=TimeSpan.FromSeconds(10)){
            powerAt=now;powerMetrics.Clear();
            try{if(GetSystemPowerStatus(out var power))powerMetrics.AddRange(Battery(power,now));else powerMetrics.Add(new("battery","Battery",null,"%",null,now,"Unavailable"));}catch{}
            try{
                int size=Marshal.SizeOf<ProcessorPower>();var buffer=Marshal.AllocHGlobal(size*Environment.ProcessorCount);
                try{if(CallNtPowerInformation(11,IntPtr.Zero,0,buffer,(uint)(size*Environment.ProcessorCount))==0){var frequencies=Enumerable.Range(0,Environment.ProcessorCount).Select(i=>Marshal.PtrToStructure<ProcessorPower>(buffer+i*size).CurrentMhz).Where(v=>v>0&&v<20000).ToArray();if(frequencies.Length>0)powerMetrics.Add(new("frequency","CPU frequency",frequencies.Average(v=>(double)v)/1000," GHz",null,now,Details:"Windows-reported clock; not instantaneous turbo frequency"));}}finally{Marshal.FreeHGlobal(buffer);}
            }catch{}
            if(!powerMetrics.Any(m=>m.Id=="frequency"))powerMetrics.Add(new("frequency","CPU frequency",null," GHz",null,now,"Unavailable"));
            }
            metrics.AddRange(powerMetrics);
            Volatile.Write(ref latest,new(metrics.ToArray(),adapters,disks,now,watch.Elapsed.TotalMilliseconds));
            await Task.Delay(2000,stop.Token);
        }}catch(OperationCanceledException){}finally{counters?.Dispose();}
    }
    IEnumerable<SystemMetric> Basic(DateTimeOffset now)
    {
        double? cpu=null,ram=null,disk=null,down=null,up=null;double? diskPercent=null;string ramDetail="";
        try{if(GetSystemTimes(out var nextIdle,out var nextKernel,out var nextUser)){
            if(kernel!=0&&now-cpuAt<=TimeSpan.FromSeconds(20)&&nextKernel>=kernel&&nextUser>=user&&nextIdle>=idle){double all=(nextKernel-kernel)+(nextUser-user);if(all>0)cpu=Math.Clamp(100*(1-(nextIdle-idle)/all),0,100);}
            idle=nextIdle;kernel=nextKernel;user=nextUser;cpuAt=now;
        }else kernel=0;}catch{kernel=0;}
        var memory=new MemoryStatus{Length=(uint)Marshal.SizeOf<MemoryStatus>()};
        try{if(GlobalMemoryStatusEx(ref memory)&&memory.Total>0&&memory.Available<=memory.Total&&memory.Load<=100){ram=memory.Load;ramDetail=$"{(memory.Total-memory.Available)/1073741824d:0.0} / {memory.Total/1073741824d:0.0} GB";}}catch{}
        try{var drive=new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);if(drive.TotalSize>0){disk=drive.AvailableFreeSpace/1073741824d;diskPercent=100d*drive.AvailableFreeSpace/drive.TotalSize;}}catch{}
        try{long rx=0,tx=0;foreach(var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up&&n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)){var stats=nic.GetIPv4Statistics();rx+=stats.BytesReceived;tx+=stats.BytesSent;}
            double seconds=(now-networkAt).TotalSeconds;if(networkAt!=default&&seconds>0&&seconds<20&&rx>=received&&tx>=sent){down=(rx-received)/seconds;up=(tx-sent)/seconds;}received=rx;sent=tx;networkAt=now;
        }catch{networkAt=default;}
        return new[]{new SystemMetric("cpu","CPU",cpu,"%",cpu,now,cpu.HasValue?"Current":"Unavailable"),new("ram","Memory",ram,"%",ram,now,ram.HasValue?"Current":"Unavailable",ramDetail),new("disk","Free disk",disk," GB",diskPercent,now,disk.HasValue?"Current":"Unavailable"),new("net","Network",down," B/s",null,now,down.HasValue&&up.HasValue?"Current":"Unavailable",up?.ToString("R",System.Globalization.CultureInfo.InvariantCulture)??"")};
    }
    public static double? GpuUsage(Dictionary<string,double> values,string adapter)
    {
        var groups=values.Where(p=>p.Key.Contains(adapter,StringComparison.OrdinalIgnoreCase)).Select(p=>(match:Regex.Match(p.Key,@"_phys_\d+_eng_\d+_"),p.Value)).Where(p=>p.match.Success).GroupBy(p=>p.match.Value).Select(g=>g.Sum(p=>p.Value)).ToArray();
        return groups.Length==0?null:Math.Clamp(groups.Max(),0,100);
    }
    public static SystemMetric AggregateGpu(IEnumerable<SystemMetric> metrics,DateTimeOffset now)
    {
        var gpu=metrics.Where(m=>m.Id.StartsWith("gpu:")).ToArray();var valid=gpu.Where(m=>m.Current(now)).ToArray();
        double? value=valid.Length==gpu.Length&&valid.Length>0?valid.Max(m=>m.Value):null;
        return new("gpu","GPU",value,"%",value,now,value.HasValue?"Current":"Unavailable","Busiest GPU engine");
    }
    public static SystemMetric[] Battery(PowerStatus power,DateTimeOffset now)
    {
        if(power.Flags!=255&&(power.Flags&128)!=0)return Array.Empty<SystemMetric>();
        double? value=power.Percent<=100?power.Percent:null;
        string state=power.Flags==255?"Unknown":(power.Flags&8)!=0?"Charging":power.Ac==1?"Plugged in":power.Ac==0?"Discharging":"Unknown";
        string detail=state;
        if(power.Ac==0&&power.Lifetime!=uint.MaxValue)detail+=$" · {TimeSpan.FromSeconds(power.Lifetime).TotalHours:0.#} h";
        return new[]{new SystemMetric("battery","Battery",value,"%",value,now,value.HasValue?"Current":"Unavailable",detail)};
    }
    public void Dispose()=>stop.Cancel(); // No blocking join on the UI/shutdown path.
    [StructLayout(LayoutKind.Sequential)]public struct PowerStatus{public byte Ac,Flags,Percent,Reserved;public uint Lifetime,FullLifetime;}
    [StructLayout(LayoutKind.Sequential)]struct ProcessorPower{public uint Number,MaxMhz,CurrentMhz,Limit,MaxIdle,CurrentIdle;}
    [DllImport("kernel32.dll")]static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("powrprof.dll")]static extern uint CallNtPowerInformation(int level,IntPtr input,uint inputSize,IntPtr output,uint outputSize);
    [DllImport("kernel32.dll")]static extern bool GetSystemTimes(out ulong idle,out ulong kernel,out ulong user);
    [StructLayout(LayoutKind.Sequential)]struct MemoryStatus{public uint Length,Load;public ulong Total,Available,TotalPage,AvailablePage,TotalVirtual,AvailableVirtual,Extended;}
    [DllImport("kernel32.dll")]static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}

// DXGI and PDH are Windows components; no drivers, elevated privileges or vendor tools.
static class GpuInventory
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Description{
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Name;
        public uint Vendor,Device,Subsystem,Revision;public UIntPtr Dedicated,System,Shared;public uint Low;public int High;public uint Flags;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int Enumerate(IntPtr self,uint index,out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int Describe(IntPtr self,out Description description);
    [DllImport("dxgi.dll")]static extern int CreateDXGIFactory1(ref Guid iid,out IntPtr factory);
    static T Method<T>(IntPtr instance,int slot) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance),slot*IntPtr.Size));
    public static GpuAdapter[] Read(){var result=new List<GpuAdapter>();Guid iid=new("770aae78-f26f-4dba-a829-253c83d1b387");if(CreateDXGIFactory1(ref iid,out var factory)!=0)return result.ToArray();
        try{var enumerate=Method<Enumerate>(factory,12);for(uint i=0;i<16;i++){if(enumerate(factory,i,out var adapter)!=0)break;try{if(Method<Describe>(adapter,10)(adapter,out var desc)==0&&(desc.Flags&2)==0)result.Add(new($"luid_0x{unchecked((uint)desc.High):x8}_0x{desc.Low:x8}",desc.Name.Trim(),desc.Dedicated.ToUInt64()));}finally{Marshal.Release(adapter);}}}finally{Marshal.Release(factory);}return result.ToArray();
    }
}
sealed class PdhCounters:IDisposable
{
    IntPtr query;readonly IntPtr[] counters=new IntPtr[4];
    static readonly string[] Paths={@"\GPU Engine(*)\Utilization Percentage",@"\GPU Adapter Memory(*)\Dedicated Usage",@"\PhysicalDisk(*)\Disk Read Bytes/sec",@"\PhysicalDisk(*)\Disk Write Bytes/sec"};
    public bool Incomplete=>counters.Any(c=>c==IntPtr.Zero);
    public PdhCounters(){if(PdhOpenQuery(null,IntPtr.Zero,out query)!=0)throw new InvalidOperationException();AddMissing();}
    public void AddMissing(){for(int i=0;i<Paths.Length;i++)if(counters[i]==IntPtr.Zero&&PdhAddEnglishCounter(query,Paths[i],IntPtr.Zero,out counters[i])!=0)counters[i]=IntPtr.Zero;}
    public void Collect(){if(PdhCollectQueryData(query)!=0)throw new InvalidOperationException();}
    public Dictionary<string,double> Read(int index){var result=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);if(counters[index]==IntPtr.Zero)return result;uint bytes=0,count=0;uint status=PdhGetFormattedCounterArray(counters[index],0x200,ref bytes,ref count,IntPtr.Zero);if(status!=0x800007D2||bytes>16_000_000||bytes==0)return result;
        var buffer=Marshal.AllocHGlobal((int)bytes);try{if(PdhGetFormattedCounterArray(counters[index],0x200,ref bytes,ref count,buffer)!=0)return result;int size=Marshal.SizeOf<Item>();if(count>bytes/size)return result;for(int i=0;i<count;i++){var item=Marshal.PtrToStructure<Item>(buffer+i*size);if(item.Status<=1&&double.IsFinite(item.Value)&&item.Value>=0){string? name=Marshal.PtrToStringUni(item.Name);if(name is not null)result[name]=item.Value;}}}finally{Marshal.FreeHGlobal(buffer);}return result;}
    [StructLayout(LayoutKind.Explicit,Size=24)]struct Item{[FieldOffset(0)]public IntPtr Name;[FieldOffset(8)]public uint Status;[FieldOffset(16)]public double Value;}
    public void Dispose(){if(query!=IntPtr.Zero){PdhCloseQuery(query);query=IntPtr.Zero;}}
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)]static extern uint PdhOpenQuery(string? source,IntPtr user,out IntPtr query);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)]static extern uint PdhAddEnglishCounter(IntPtr query,string path,IntPtr user,out IntPtr counter);
    [DllImport("pdh.dll")]static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)]static extern uint PdhGetFormattedCounterArray(IntPtr counter,uint format,ref uint bytes,ref uint count,IntPtr buffer);
    [DllImport("pdh.dll")]static extern uint PdhCloseQuery(IntPtr query);
}

public sealed record ProcessCapacity(int Id,string Name,long Started,double CpuMilliseconds,long Memory,double CpuPercent=0);
public static class ResourceConsumers
{
    public static ProcessCapacity[] Snapshot(){var rows=new List<ProcessCapacity>();foreach(var p in Process.GetProcesses())using(p)try{rows.Add(new(p.Id,p.ProcessName,p.StartTime.ToUniversalTime().Ticks,p.TotalProcessorTime.TotalMilliseconds,p.WorkingSet64));}catch{}return rows.ToArray();}
    public static ProcessCapacity[] Compare(ProcessCapacity[] before,ProcessCapacity[] after,double milliseconds,int cores){if(milliseconds<=0||cores<=0)return Array.Empty<ProcessCapacity>();var map=before.ToDictionary(p=>(p.Id,p.Started));return after.Where(p=>map.ContainsKey((p.Id,p.Started))).Select(p=>p with{CpuPercent=Math.Clamp((p.CpuMilliseconds-map[(p.Id,p.Started)].CpuMilliseconds)/milliseconds/cores*100,0,100)}).ToArray();}
    public static async Task<ProcessCapacity[]> Read(CancellationToken token){var before=await Task.Run(Snapshot,token);var timer=Stopwatch.StartNew();await Task.Delay(1000,token);var after=await Task.Run(Snapshot,token);return Compare(before,after,timer.Elapsed.TotalMilliseconds,Environment.ProcessorCount);}
}
