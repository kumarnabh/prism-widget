using System.IO;
using System.Text.Json;

namespace Prism;

public sealed record ClockPreferences(bool Enabled=false,string FirstZone="Eastern Standard Time",string SecondZone="Pacific Standard Time",string FirstLabel="US Eastern",string SecondLabel="US Pacific")
{
    public static ClockPreferences Load(string path)
    {
        try{
            var value=JsonSerializer.Deserialize<ClockPreferences>(File.ReadAllText(path));
            if(value is null||!WorldClocks.Valid(value.FirstZone)||!WorldClocks.Valid(value.SecondZone))return new();
            return value with{FirstLabel=WorldClocks.Label(value.FirstLabel,value.FirstZone),SecondLabel=WorldClocks.Label(value.SecondLabel,value.SecondZone)};
        }catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException){return new();}
    }
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(this));File.Move(temp,path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}

public static class WorldClocks
{
    public static bool Valid(string? id)
    {
        if(string.IsNullOrWhiteSpace(id))return false;
        try{TimeZoneInfo.FindSystemTimeZoneById(id);return true;}catch(TimeZoneNotFoundException){return false;}catch(InvalidTimeZoneException){return false;}
    }
    public static string Label(string? label,string zone)
    {
        string value=string.IsNullOrWhiteSpace(label)?zone.Replace(" Standard Time",""):label.Trim();
        return value.Length>24?value[..24]:value;
    }
    public static DateTimeOffset At(DateTimeOffset instant,string zone)=>TimeZoneInfo.ConvertTime(instant,TimeZoneInfo.FindSystemTimeZoneById(zone));
    public static string Description(DateTimeOffset instant,string zone,string label)
    {
        var time=At(instant,zone);var delta=time.Date-TimeZoneInfo.ConvertTime(instant,TimeZoneInfo.Local).Date;
        string day=delta.Days==0?"today":delta.Days>0?$"+{delta.Days} day":$"{delta.Days} day";
        string offset=$"{(time.Offset<TimeSpan.Zero?"−":"+")}{time.Offset.Duration():hh\\:mm}";
        return $"{label} · {time:ddd, MMM d · HH:mm} · UTC{offset} · {day}\n{TimeZoneInfo.FindSystemTimeZoneById(zone).DisplayName}";
    }
    public static bool Verify()
    {
        var winter=new DateTimeOffset(2026,1,15,12,0,0,TimeSpan.Zero);
        var summer=new DateTimeOffset(2026,7,15,12,0,0,TimeSpan.Zero);
        bool seasonal=At(winter,"GMT Standard Time").Hour==12&&At(summer,"GMT Standard Time").Hour==13
            &&At(winter,"Eastern Standard Time").Hour==7&&At(summer,"Eastern Standard Time").Hour==8;
        bool pacific=At(winter,"Pacific Standard Time").Hour==4&&At(summer,"Pacific Standard Time").Hour==5;
        bool halfHour=At(winter,"India Standard Time").TimeOfDay==new TimeSpan(17,30,0);
        bool rollover=At(new DateTimeOffset(2026,1,15,23,30,0,TimeSpan.Zero),"Tokyo Standard Time").Day==16;
        bool transition=At(new DateTimeOffset(2026,3,8,6,59,0,TimeSpan.Zero),"Eastern Standard Time").Hour==1
            &&At(new DateTimeOffset(2026,3,8,7,0,0,TimeSpan.Zero),"Eastern Standard Time").Hour==3;
        return seasonal&&pacific&&halfHour&&rollover&&transition&&!Valid("not-a-timezone");
    }
}
