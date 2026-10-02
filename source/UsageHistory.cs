namespace Prism;

public sealed record UsagePoint(string Provider,long At,double Remaining);
public sealed class UsageHistory
{
    public List<UsagePoint> Points {get;set;}=new();
    static bool Valid(UsagePoint p,DateTimeOffset now)=>p is not null&&WidgetPreferences.ProviderIds.Contains(p.Provider)&&double.IsFinite(p.Remaining)&&p.Remaining>=0&&p.Remaining<=100&&p.At>=now.AddDays(-30).ToUnixTimeSeconds()&&p.At<=now.ToUnixTimeSeconds();
    public static UsageHistory Load(string path,DateTimeOffset now)
    {
        var history=LocalJson.Load<UsageHistory>(path)??new();
        history.Points=(history.Points??new()).Where(p=>Valid(p,now)).OrderBy(p=>p.At).TakeLast(40_000).ToList();return history;
    }
    public bool Record(string provider,QuotaSnapshot reading,DateTimeOffset now)
    {
        if(!Fresh(reading,now)||reading.Remaining is not double remaining||reading.Captured is not DateTimeOffset at)return false;
        var point=new UsagePoint(provider,at.ToUnixTimeSeconds(),remaining);if(!Valid(point,now))return false;
        int index=Points.FindLastIndex(p=>p.Provider==provider&&p.At/300==point.At/300);
        if(index>=0){if(Points[index].At>=point.At)return false;Points[index]=point;}else Points.Add(point);
        Points=Points.Where(p=>Valid(p,now)).OrderBy(p=>p.At).TakeLast(40_000).ToList();return true;
    }
    public static bool Fresh(QuotaSnapshot reading,DateTimeOffset now)=>reading.Status is "Live" or "Recent" or "CLI" or "Browser" or "Feed"
        &&reading.Captured is DateTimeOffset at&&at<=now&&now-at<=TimeSpan.FromMinutes(10);
    public void Save(string path)=>LocalJson.Save(path,this);
}

public sealed class LowQuotaAlerts
{
    public Dictionary<string,long> Notified {get;set;}=new();
    public static LowQuotaAlerts Load(string path)=>LocalJson.Load<LowQuotaAlerts>(path,200_000)??new();
    public List<string> Due(IEnumerable<KeyValuePair<string,QuotaSnapshot>> readings,DateTimeOffset now,int threshold)
    {
        Notified??=new();Notified=Notified.Where(p=>p.Key.Length<256&&p.Value>=now.AddDays(-30).ToUnixTimeSeconds()).Take(256).ToDictionary(p=>p.Key,p=>p.Value);
        var result=new List<string>();
        foreach(var (provider,reading) in readings){
            if(!UsageHistory.Fresh(reading,now))continue;
            foreach(var window in reading.Windows){
                string key=System.Text.Json.JsonSerializer.Serialize(new[]{provider,window.Label,window.Reset?.ToUnixTimeSeconds().ToString()??"unknown"});
                if(window.Remaining>threshold){Notified.Remove(key);continue;}
                if(!double.IsFinite(window.Remaining)||window.Remaining<0||window.Reset<=now||Notified.ContainsKey(key))continue;
                result.Add($"{provider} · {window.Label}: {window.Remaining:0.#}%");Notified[key]=now.ToUnixTimeSeconds();
            }
        }
        return result;
    }
    public void Save(string path)=>LocalJson.Save(path,this);
}
