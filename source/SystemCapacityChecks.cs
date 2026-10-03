namespace Prism;

public static class SystemCapacityChecks
{
    public static bool Verify()
    {
        void Require(bool value,string label){if(!value)throw new InvalidOperationException("System capacity check: "+label);}
        var now=DateTimeOffset.UtcNow;
        var good=new SystemMetric("gpu","GPU",25,"%",25,now);
        Require(good.Current(now)&&good.Display(now)!="—","valid metric");
        foreach(var bad in new[]{good with{Value=double.NaN},good with{Value=-1},good with{Percent=double.NaN},good with{Percent=101},good with{At=now.AddSeconds(1)},good with{At=now.AddSeconds(-21)},good with{Status="Unavailable"},good with{Value=null}})Require(!bad.Current(now)&&bad.Display(now)=="—","invalid/stale unknown");
        string adapter="luid_0x00000000_0x00001234";
        var engines=new Dictionary<string,double>{{"pid_1_"+adapter+"_phys_0_eng_0_engtype_3D",20},{"pid_2_"+adapter+"_phys_0_eng_0_engtype_3D",30},{"pid_1_"+adapter+"_phys_0_eng_1_engtype_Copy",40},{"pid_1_luid_0x00000000_0x00009999_phys_0_eng_0_engtype_3D",90}};
        Require(SystemCapacity.GpuUsage(engines,adapter)==50,"busiest engine aggregates processes, isolates adapters");
        Require(SystemCapacity.GpuUsage(new(),adapter)==null,"no GPU never zero");
        Require(SystemCapacity.AggregateGpu(Array.Empty<SystemMetric>(),now).Value==null,"missing GPU");
        Require(SystemCapacity.AggregateGpu(new[]{good with{Id="gpu:a"},good with{Id="gpu:b",Value=null}},now).Value==null,"partial multi GPU unknown");
        Require(SystemCapacity.AggregateGpu(new[]{good with{Id="gpu:a"},good with{Id="gpu:b",Value=65}},now).Value==65,"multi GPU maximum");
        Require(SystemCapacity.Battery(new(){Flags=128,Percent=255,Lifetime=uint.MaxValue},now).Length==0,"desktop no battery");
        var battery=SystemCapacity.Battery(new(){Flags=0,Ac=0,Percent=64,Lifetime=7200},now).Single();Require(battery.Value==64&&battery.Details.Contains("2 h"),"laptop Windows runtime");
        Require(!SystemCapacity.Battery(new(){Flags=255,Ac=255,Percent=255,Lifetime=uint.MaxValue},now).Single().Current(now),"unknown battery");
        Require(!SystemCapacity.Battery(new(){Flags=8,Ac=1,Percent=50,Lifetime=7200},now).Single().Details.Contains(" h"),"no charging runtime guess");
        var before=new[]{new ProcessCapacity(1,"A",100,100,10),new ProcessCapacity(2,"B",100,100,20)};
        var after=new[]{new ProcessCapacity(1,"A",100,500,20),new ProcessCapacity(2,"C",200,600,30)};
        Require(ResourceConsumers.Compare(before,after,1000,4).Single().CpuPercent==10,"CPU normalizes cores and excludes reused PID");
        Require(ResourceConsumers.Compare(before,after,0,4).Length==0,"invalid sampling duration");
        Require(LiveTaskbarPreview.BitmapSize(true,100,64,340,220)==(100,64),"thumbnail respects requested maxima");
        Require(LiveTaskbarPreview.BitmapSize(false,0,0,340,220)==(340,220),"compact Peek respects client bounds");
        Require(LiveTaskbarPreview.BitmapSize(false,0,0,1200,120)==(640,120),"ribbon Peek respects client height");
        Require(LiveTaskbarPreview.BitmapSize(true,65535,65535,340,220)==(640,480),"native buffer dimensions capped");
        var pixels=LiveTaskbarPreview.Render(Array.Empty<Widget.BarMetric>(),20,10);Require(pixels.PixelWidth==20&&pixels.PixelHeight==10,"tiny thumbnail exact dimensions");
        var preferences=new WidgetPreferences{VisibleMetrics=new(),MetricOrder=new(){"gpu","cpu"},ActiveProvidersOnly=true,MinimizeToTray=false};preferences.Normalize();Require(preferences.VisibleMetrics.Count==0&&preferences.MetricOrder.First()=="gpu"&&preferences.Copy().ActiveProvidersOnly&&!preferences.Copy().MinimizeToTray,"empty selection and new preferences migrate");
        return true;
    }
}
