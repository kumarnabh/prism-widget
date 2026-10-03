namespace Prism;

public sealed record CapacityEstimate(string State,double? RecentRate=null,double? BaselineRate=null,double? SafeRate=null,double? ProjectedRemaining=null,DateTimeOffset? Exhaustion=null,double? Consumed=null)
{
    public bool Reliable=>State=="Stable estimate";
    public string Describe()=>string.Join("\n",new[]{L.T(State),RecentRate is double r?L.F("Recent pace: ~{0:0.#}% / hour",r):null,SafeRate is double safe?L.F("Safe pace: ~{0:0.#}% / hour",safe):null,ProjectedRemaining is double left?L.F("Projected at reset: ~{0:0.#}% remaining",left):null,Exhaustion is DateTimeOffset end?L.F("Estimated exhaustion: {0}",end.LocalDateTime.ToString("g")):null}.Where(s=>s is not null));
}

public static class CapacityForecast
{
    public static bool Confirm(Dictionary<string,long> pending,string key,QuotaSnapshot reading,QuotaWindow window,CapacityEstimate estimate,DateTimeOffset now)
    {
        foreach(var expired in pending.Where(p=>p.Value<now.AddHours(-6).ToUnixTimeSeconds()).Select(p=>p.Key).ToArray())pending.Remove(expired);
        if(!UsageHistory.Fresh(reading,now)||!estimate.Reliable||estimate.Exhaustion is not DateTimeOffset end||window.Reset is not DateTimeOffset reset||reset-end<TimeSpan.FromMinutes(15)){pending.Remove(key);return false;}
        long capture=reading.Captured!.Value.ToUnixTimeSeconds();
        if(!pending.TryGetValue(key,out long first)){if(pending.Count>=256)pending.Remove(pending.MinBy(p=>p.Value).Key);pending[key]=capture;return false;}
        return capture-first>=300;
    }
    static double Median(IEnumerable<double> values){var sorted=values.OrderBy(x=>x).ToArray();return sorted.Length%2==1?sorted[sorted.Length/2]:(sorted[sorted.Length/2-1]+sorted[sorted.Length/2])/2;}
    public static CapacityEstimate Calculate(IEnumerable<WindowSample> history,string provider,QuotaSnapshot reading,QuotaWindow window,DateTimeOffset now)
    {
        if(!UsageHistory.Fresh(reading,now))return new("Stale");
        if(!double.IsFinite(window.Remaining)||window.Remaining<0||window.Remaining>100||window.Reset<=now)return new("Unavailable");
        double? hours=window.Reset is DateTimeOffset reset?(reset-now).TotalHours:null;
        double? safe=hours>=1d/12?window.Remaining/hours:null;
        if(reading.Scope.Length!=64)return new("Account scope unavailable",SafeRate:safe);
        if(window.Rolling)return new("Rolling allowance",SafeRate:null);
        string id=string.IsNullOrEmpty(window.Id)?window.Label:window.Id;
        var points=history.Where(p=>p is not null&&p.Provider==provider&&p.Scope==reading.Scope&&p.Window==id&&p.Reset==window.Reset?.ToUnixTimeSeconds()&&!p.Rolling&&p.At>=now.AddHours(-6).ToUnixTimeSeconds()&&UsageHistory.SampleValid(p,now)).OrderBy(p=>p.At).DistinctBy(p=>p.At).ToList();
        if(points.Count==0||points[^1].At!=reading.Captured!.Value.ToUnixTimeSeconds()||Math.Abs(points[^1].Remaining-window.Remaining)>.2)return new("Insufficient history",SafeRate:safe);
        // Stop at any reset/replenishment, large correction or missed interval. Do
        // not discard an abrupt decrease and then extrapolate the older trend.
        var cadence=points.Zip(points.Skip(1),(a,b)=>(double)(b.At-a.At)).Where(d=>d>0).ToArray();
        double gap=cadence.Length==0?900:Math.Clamp(Median(cadence)*3,900,3600);
        int start=0;
        for(int i=1;i<points.Count;i++)if(points[i].At-points[i-1].At>gap||points[i].Remaining>points[i-1].Remaining+.2||points[i-1].Remaining-points[i].Remaining>25)start=i;
        points=points.Skip(start).ToList();
        if(points.Count<6||points[^1].At-points[0].At<1800||now.ToUnixTimeSeconds()-points[^1].At>600)return new("Insufficient history",SafeRate:safe);
        double Rate(IReadOnlyList<WindowSample> series)=>(series[0].Remaining-series[^1].Remaining)*3600/(series[^1].At-series[0].At);
        var recent=points.Where(p=>p.At>=points[^1].At-5400).ToList();
        if(recent.Count<6||recent[^1].At-recent[0].At<1800)return new("Insufficient history",SafeRate:safe);
        // Time-weighted net depletion includes quiet intervals. Interval dispersion
        // and half-window agreement qualify the estimate instead of fitting a line.
        double rate=Math.Max(0,Rate(recent)),baseline=Math.Max(0,Rate(points));
        var rates=recent.Zip(recent.Skip(1),(a,b)=>Math.Max(0,a.Remaining-b.Remaining)*3600/(b.At-a.At)).ToArray();
        double median=Median(rates),mad=Median(rates.Select(r=>Math.Abs(r-median)));
        int split=recent.Count/2;double first=Rate(recent.Take(split+1).ToList()),second=Rate(recent.Skip(split).ToList());
        bool variable=mad>Math.Max(2,rate*.65)||Math.Abs(first-second)>Math.Max(3,rate*.75)||rates.Max()>Math.Max(5,rate*4);
        if(variable)return new("Highly variable usage",rate,baseline,safe,Consumed:Math.Max(0,recent[0].Remaining-recent[^1].Remaining));
        if(hours is null)return new("Unknown reset",rate,baseline,null,Consumed:Math.Max(0,recent[0].Remaining-recent[^1].Remaining));
        if(hours<1d/12)return new("Reset imminent",rate,baseline);
        var captured=reading.Captured!.Value;double horizon=(window.Reset!.Value-captured).TotalHours;
        double projected=Math.Clamp(window.Remaining-rate*horizon,0,100);
        var exhaust=rate>.05&&window.Remaining/rate<=Math.Min(horizon,24*30)?captured.AddHours(window.Remaining/rate):(DateTimeOffset?)null;
        return new("Stable estimate",rate,baseline,safe,projected,exhaust,Math.Max(0,recent[0].Remaining-recent[^1].Remaining));
    }
}
