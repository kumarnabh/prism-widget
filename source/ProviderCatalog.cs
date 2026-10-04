using System.Reflection;
using System.Text.Json;

namespace Prism;

public sealed record ProviderDefinition(string Id,string Name,string Icon,string Color,string Adapter,string Source,string Authentication,int Interval,bool Enabled,string[] Capabilities);
public static class ProviderCatalog
{
    sealed record Manifest(int Version,ProviderDefinition[] Providers);
    public static readonly ProviderDefinition[] All=Load();
    static ProviderDefinition[] Load()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Prism.Providers.json")!;
        var manifest=JsonSerializer.Deserialize<Manifest>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        if(manifest.Version!=1||manifest.Providers.Length is <1 or >16||manifest.Providers.Select(p=>p.Id).Distinct().Count()!=manifest.Providers.Length||manifest.Providers.Any(p=>!System.Text.RegularExpressions.Regex.IsMatch(p.Id,"^[a-z][a-z0-9-]{0,31}$")||p.Interval is <60 or >3600))throw new InvalidOperationException("Invalid built-in provider registry");
        return manifest.Providers;
    }
    public static ProviderDefinition Get(string id)=>All.Single(p=>p.Id==id);
}
