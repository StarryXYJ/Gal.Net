namespace GalNet.Core.Configuration;

/// <summary>Author-owned independent resource replacement package.</summary>
public sealed class ResourceOverrideDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Priority { get; set; }
    private Dictionary<string, string> _mappings = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Mappings
    {
        get => _mappings;
        set => _mappings = value is null ? null! : new(value, StringComparer.OrdinalIgnoreCase);
    }
    public List<string> IncludeResources { get; set; } = [];
    public List<string> DeclaredCompleteResources { get; set; } = [];
}
