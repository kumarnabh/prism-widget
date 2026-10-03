namespace Prism;

public static class ForecastChecks
{
    public static double BenchmarkMilliseconds {get;private set;}
    public static bool Verify(string scratch)
    {
        var now=new DateTimeOffset(2026,10,3,12,0,0,TimeSpan.Zero);string scope=new('a',64);long reset=now.AddHours(3).ToUnixTimeSeconds();
        var points=Enumerable.Range(0,13).Select(i=>new WindowSample("codex",scope,"session",now.AddMinutes((i-12)*5).ToUnixTimeSeconds(),80-i,reset)).ToList();
        var window=new QuotaWindow("Session",68,now.AddHours(3),"session");var reading=new QuotaSnapshot("Live","test","",now,new[]{window},scope);
        CapacityEstimate Calculate(IEnumerable<WindowSample>? samples=null,QuotaSnapshot? value=null,QuotaWindow? quota=null)=>CapacityForecast.Calculate(samples??points,"codex",value??reading,quota??window,now);
        void Check(bool pass,string name){if(!pass)throw new InvalidOperationException("Forecast: "+name);}
        var estimate=Calculate();Check(estimate.Reliable&&Math.Abs(estimate.RecentRate!.Value-12)<.001&&estimate.ProjectedRemaining==32&&estimate.Exhaustion is null,"steady pace");
        Check(Calculate(points.Take(3)).State=="Insufficient history","short history");
        Check(Calculate(value:reading with{Status="Stale"}).State=="Stale","stale rejected");
        Check(Calculate(value:reading with{Captured=now.AddSeconds(1)}).State=="Stale","future rejected");
        Check(Calculate(value:reading with{Scope=""}).RecentRate is null,"legacy scope rejected");
        Check(Calculate(value:reading with{Scope=new string('b',64)}).RecentRate is null,"account isolation");
        Check(Calculate(quota:window with{Reset=now.AddHours(4)}).RecentRate is null,"reset isolation");
        Check(Calculate(quota:window with{Rolling=true}).State=="Rolling allowance","rolling allowance conservative");
        var quiet=points.Select(p=>p with{Remaining=68}).ToArray();Check(Calculate(quiet).RecentRate==0&&Calculate(quiet).Exhaustion is null,"quiet periods");
        var correction=points.Select((p,i)=>i>=10?p with{Remaining=95}:p);Check(Calculate(correction).RecentRate is null,"upward correction");
        var drop=points.Select((p,i)=>i>=10?p with{Remaining=10}:p);Check(Calculate(drop).RecentRate is null,"abrupt depletion requires new segment");
        var burst=points.Select((p,i)=>p with{Remaining=i<8?80:80-(i-7)*4});Check(Calculate(burst,quota:window with{Remaining=60}).State=="Highly variable usage","variable pace");
        var gaps=points.Select((p,i)=>i<10?p with{At=p.At-7200}:p);Check(Calculate(gaps).RecentRate is null,"long gap");
        var unknown=points.Select(p=>p with{Reset=null});Check(Calculate(unknown,quota:window with{Reset=null}).State=="Unknown reset","unknown reset no projection");
        var near=points.Select(p=>p with{Reset=now.AddMinutes(3).ToUnixTimeSeconds()});Check(Calculate(near,quota:window with{Reset=now.AddMinutes(3)}).SafeRate is null,"near-reset suppression");
        Check(Calculate(quota:window with{Remaining=double.NaN}).RecentRate is null,"invalid value");
        var gate=new NotificationState();Check(gate.Available(now),"first notification");gate.Mark(now);Check(!gate.Available(now.AddSeconds(119))&&gate.Available(now.AddSeconds(120)),"global cooldown");
        var pending=new Dictionary<string,long>();var risk=new CapacityEstimate("Stable estimate",Exhaustion:now.AddHours(1));
        Check(!CapacityForecast.Confirm(pending,"fixture",reading,window,risk,now)&&!CapacityForecast.Confirm(pending,"fixture",reading,window,risk,now),"same capture cannot confirm alert");
        Check(CapacityForecast.Confirm(pending,"fixture",reading with{Captured=now.AddMinutes(5)},window,risk,now.AddMinutes(5)),"two captures confirm alert");
        Check(!CapacityForecast.Confirm(pending,"fixture",reading with{Status="Stale"},window,risk,now)&&pending.Count==0,"stale cancels candidate");
        Check(!CapacityForecast.Confirm(pending,"fixture",reading,window,risk with{State="Highly variable usage"},now),"variable alerts suppressed");
        Check(Calculate(quota:window with{Remaining=90}).RecentRate is null,"unrecorded correction");
        var delayed=points.Select(p=>p with{At=p.At-600});Check(Calculate(delayed,reading with{Captured=now.AddMinutes(-10)}).ProjectedRemaining==30,"projection anchored to capture");
        var fast=points.Select((p,i)=>p with{At=p.At-600,Remaining=80-i*5});Check(Calculate(fast,reading with{Captured=now.AddMinutes(-10)},window with{Remaining=20}).Exhaustion==now.AddMinutes(10),"exhaustion anchored to capture");
        var history=new UsageHistory();Check(history.Record("codex",reading,now),"first sample");
        Check(history.Record("codex",reading with{Scope=new string('b',64)},now)&&history.Samples.Count==2,"same-capture account change persists");
        Check(!history.Record("codex",reading,now),"duplicate capture ignored");
        var low=new LowQuotaAlerts();var lowReading=reading with{Windows=new[]{window with{Remaining=10,Reset=null}}};
        Check(low.Due(new[]{new KeyValuePair<string,QuotaSnapshot>("codex",lowReading)},now,20).Count==1&&low.Due(new[]{new KeyValuePair<string,QuotaSnapshot>("codex",lowReading with{Scope=new string('b',64)})},now,20).Count==1,"low alert account isolation");
        var reminders=new ResetReminders();var notice=new ResetNotice("codex","Session",50,now.AddMinutes(20),scope,"session");reminders.Mark(new[]{notice},now);
        Check(reminders.Unseen(new[]{notice}).Count==0&&reminders.Unseen(new[]{notice with{Scope=new string('b',64)}}).Count==1,"reset alert account isolation");
        System.IO.Directory.CreateDirectory(scratch);string path=System.IO.Path.Combine(scratch,"prism-forecast-"+Guid.NewGuid().ToString("N")+".json");
        try{history.Save(path);Check(UsageHistory.Load(path,now).Samples.Count==2,"window history round trip");
            System.IO.File.WriteAllText(path,"{\"Version\":3}");var future=UsageHistory.Load(path,now);future.Save(path);Check(future.ReadOnly&&System.IO.File.ReadAllText(path)=="{\"Version\":3}","future schema preserved");
            gate.Forecasts["fixture"]=now.ToUnixTimeSeconds();LocalJson.Save(path,gate);var restored=LocalJson.Load<NotificationState>(path)!;Check(restored.Forecasts.ContainsKey("fixture")&&!restored.Available(now),"persisted notification gate");
        }finally{if(System.IO.File.Exists(path))System.IO.File.Delete(path);}
        var large=Enumerable.Range(0,39987).Select(i=>new WindowSample("codex",scope,"other-"+(i%32),now.AddMinutes(-30).ToUnixTimeSeconds(),60,reset)).Concat(points).ToArray();
        var watch=System.Diagnostics.Stopwatch.StartNew();for(int i=0;i<100;i++)Check(Calculate(large).Reliable,"bounded history calculation");BenchmarkMilliseconds=watch.Elapsed.TotalMilliseconds/100;
        return true;
    }
}
