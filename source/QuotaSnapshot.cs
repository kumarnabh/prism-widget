using System.Text.Json;

namespace Prism;

public sealed record QuotaWindow(string Label,double Remaining,DateTimeOffset? Reset);
public sealed record QuotaSnapshot(string Status,string Source,string Detail,DateTimeOffset? Captured,IReadOnlyList<QuotaWindow> Windows)
{
    public double? Remaining=>Windows.Count==0?null:Windows.Min(w=>w.Remaining);
    public static QuotaSnapshot Read(JsonElement data,DateTimeOffset now)
    {
        string Text(string key,string fallback="")=>data.ValueKind==JsonValueKind.Object&&data.TryGetProperty(key,out var field)&&field.ValueKind==JsonValueKind.String?field.GetString()??fallback:fallback;
        DateTimeOffset? captured=null;var windows=new List<QuotaWindow>();bool malformed=false;
        if(data.ValueKind==JsonValueKind.Object&&data.TryGetProperty("at",out var at)&&Timestamp(at) is DateTimeOffset stamp&&stamp<=now.AddMinutes(5))captured=stamp;
        if(captured.HasValue&&data.TryGetProperty("windows",out var list)&&list.ValueKind==JsonValueKind.Array&&list.GetArrayLength()<=32){
            foreach(var item in list.EnumerateArray().Take(32)){
                if(item.ValueKind!=JsonValueKind.Object||!item.TryGetProperty("remaining",out var raw)||!raw.TryGetDoubleSafe(out double remaining)||remaining<0||remaining>100){malformed=true;break;}
                if(!item.TryGetProperty("label",out var label)||label.ValueKind!=JsonValueKind.String){malformed=true;break;}
                DateTimeOffset? reset=null;
                if(item.TryGetProperty("reset",out var rawReset)&&rawReset.ValueKind!=JsonValueKind.Null){reset=Timestamp(rawReset);if(!reset.HasValue){malformed=true;break;}if(reset<=now)continue;}
                windows.Add(new(label.GetString()??"Quota",remaining,reset));
            }
        }
        if(malformed)windows.Clear();
        return new(Text("status","Unavailable"),Text("source"),Text("detail","Connect this source to see its allowance."),captured,windows);
    }
    static DateTimeOffset? Timestamp(JsonElement value)
    {
        if(!value.TryGetDoubleSafe(out double seconds)||seconds<=0||seconds>253402300799)return null;
        return DateTimeOffset.FromUnixTimeSeconds((long)seconds);
    }
    public string ResetSummary(DateTimeOffset now)
    {
        var limiting=Windows.OrderBy(w=>w.Remaining).FirstOrDefault();
        if(limiting?.Reset is not DateTimeOffset reset)return "";
        var span=reset-now;
        return span.TotalDays>=1?$"Resets in {Math.Ceiling(span.TotalDays):0}d":span.TotalHours>=1?$"Resets in {Math.Ceiling(span.TotalHours):0}h":$"Resets in {Math.Max(1,Math.Ceiling(span.TotalMinutes)):0}m";
    }
}
static class JsonNumbers
{
    public static bool TryGetDoubleSafe(this JsonElement value,out double number){number=0;return value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out number)&&double.IsFinite(number);}
}
