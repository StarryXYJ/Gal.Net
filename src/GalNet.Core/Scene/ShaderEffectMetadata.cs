using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GalNet.Core.Scene;

/// <summary>
/// A platform-neutral reference to an effect program resource. The resource may contain
/// a SkSL implementation on Avalonia, but Core never interprets that implementation.
/// </summary>
public readonly record struct EffectProgramResource
{
    public EffectProgramResource(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Effect program resource cannot be empty.", nameof(value));
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

/// <summary>Value kinds declared by an <c>@param</c> or <c>@texture</c> metadata entry.</summary>
public enum ShaderEffectParameterKind { Float, Integer, Boolean, Color, Vector2, Vector4, Texture, Enum }

/// <summary>Editor and validation metadata parsed from a shader resource comment block.</summary>
public sealed record ShaderEffectParameterDescriptor(
    string Name,
    string Uniform,
    ShaderEffectParameterKind Kind,
    string? DefaultValue = null,
    double? Minimum = null,
    double? Maximum = null,
    double? Step = null,
    bool Animatable = false,
    bool Required = false,
    IReadOnlyList<string>? Options = null,
    string? DisplayName = null,
    string? Group = null,
    string? Tooltip = null);

/// <summary>
/// The platform-independent surface of a shader effect resource. The renderer is responsible
/// for compiling and reflecting the backend-specific shader, then validating it against this descriptor.
/// </summary>
public sealed record ShaderEffectDescriptor(
    EffectProgramResource Resource,
    int Version,
    string SourceInput,
    IReadOnlySet<EffectStage> SupportedStages,
    IReadOnlyList<ShaderEffectParameterDescriptor> Parameters)
{
    public bool Supports(EffectStage stage) => SupportedStages.Contains(stage);
}

/// <summary>One parser diagnostic with a one-based source line where available.</summary>
public sealed record ShaderEffectMetadataDiagnostic(int? Line, string Message)
{
    public override string ToString() => Line is { } line ? $"Line {line}: {Message}" : Message;
}

/// <summary>Result of parsing the shader-local <c>@gal.effect</c> metadata block.</summary>
public sealed record ShaderEffectMetadataParseResult(
    ShaderEffectDescriptor? Descriptor,
    IReadOnlyList<ShaderEffectMetadataDiagnostic> Diagnostics)
{
    public bool Success => Descriptor is not null && Diagnostics.Count == 0;
}

/// <summary>
/// Resolves only metadata for a program resource. This is intentionally a resource lookup,
/// not an effect manager: it has no live instances, shader objects, or render lifecycle.
/// </summary>
public interface IShaderEffectMetadataResolver
{
    bool TryGetDescriptor(EffectProgramResource resource, out ShaderEffectDescriptor descriptor);
}

/// <summary>Where a generic shader attachment receives its input texture.</summary>
public abstract record ShaderEffectTarget
{
    private ShaderEffectTarget() { }

    public sealed record Layer : ShaderEffectTarget
    {
        public Layer(string layerHandleId)
        {
            if (string.IsNullOrWhiteSpace(layerHandleId)) throw new ArgumentException("Layer target requires a handle.", nameof(layerHandleId));
            LayerHandleId = layerHandleId;
        }

        public string LayerHandleId { get; }
    }

    public sealed record ScenePost : ShaderEffectTarget;

    public EffectStage Stage => this is Layer ? EffectStage.Layer : EffectStage.ScenePost;
}

/// <summary>
/// A live generic effect attachment. Its handle is Runtime-owned and is deliberately not an author-facing identifier.
/// </summary>
public sealed class ShaderEffectAttachment
{
    private readonly Dictionary<string, JsonElement> _parameters;

    public ShaderEffectAttachment(
        string runtimeHandleId,
        ShaderEffectTarget target,
        EffectProgramResource program,
        int order,
        IReadOnlyDictionary<string, JsonElement>? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(runtimeHandleId)) throw new ArgumentException("Runtime effect handle cannot be empty.", nameof(runtimeHandleId));
        RuntimeHandleId = runtimeHandleId;
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Program = program;
        Order = order;
        _parameters = parameters is null
            ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            : parameters.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
    }

    public string RuntimeHandleId { get; }
    public ShaderEffectTarget Target { get; }
    public EffectStage Stage => Target.Stage;
    public EffectProgramResource Program { get; }
    public int Order { get; }
    public IReadOnlyDictionary<string, JsonElement> Parameters => _parameters;

    /// <summary>Returns the program itself and every statically supplied texture resource parameter.</summary>
    public IEnumerable<EffectProgramResource> EnumerateResourceDependencies(ShaderEffectDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        yield return Program;

        foreach (var parameter in descriptor.Parameters.Where(parameter => parameter.Kind == ShaderEffectParameterKind.Texture))
        {
            if (!_parameters.TryGetValue(parameter.Name, out var value) || value.ValueKind != JsonValueKind.String) continue;
            var resource = value.GetString();
            if (!string.IsNullOrWhiteSpace(resource)) yield return new EffectProgramResource(resource);
        }
    }
}

/// <summary>
/// Parses only the metadata comment block. It deliberately does not parse or compile the shader language,
/// so the same Core contract can be used by different rendering backends.
/// </summary>
public static class ShaderEffectMetadataParser
{
    private static readonly Regex CommentBlock = new(@"/\*(?<body>[\s\S]*?)\*/", RegexOptions.Compiled);

    public static ShaderEffectMetadataParseResult Parse(EffectProgramResource resource, string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var diagnostics = new List<ShaderEffectMetadataDiagnostic>();
        var block = CommentBlock.Matches(source).Cast<Match>()
            .FirstOrDefault(match => match.Groups["body"].Value.Contains("@gal.effect", StringComparison.Ordinal));
        if (block is null)
            return new ShaderEffectMetadataParseResult(null, [new(null, "Shader resource does not contain an @gal.effect metadata block.")]);

        var bodyStartLine = CountLines(source, block.Index) + 1;
        var lines = block.Groups["body"].Value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var parameters = new List<MutableParameter>();
        var parameterNames = new HashSet<string>(StringComparer.Ordinal);
        var stages = new HashSet<EffectStage>();
        MutableParameter? current = null;
        string? input = null;
        int? version = null;

        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = bodyStartLine + index;
            var line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;

            if (line.StartsWith('@'))
            {
                var separator = line.IndexOfAny([' ', '\t']);
                var directive = separator < 0 ? line : line[..separator];
                var value = separator < 0 ? "" : line[(separator + 1)..].Trim();
                switch (directive)
                {
                    case "@gal.effect":
                        if (version is not null) { diagnostics.Add(new(lineNumber, "@gal.effect may appear only once.")); break; }
                        version = ParseVersion(value, lineNumber, diagnostics);
                        break;
                    case "@input":
                        if (input is not null) diagnostics.Add(new(lineNumber, "@input may appear only once."));
                        else input = value;
                        break;
                    case "@targets":
                        ParseStages(value, stages, lineNumber, diagnostics);
                        break;
                    case "@param":
                    case "@texture":
                        if (string.IsNullOrWhiteSpace(value)) { diagnostics.Add(new(lineNumber, $"{directive} requires a name.")); break; }
                        if (!parameterNames.Add(value)) { diagnostics.Add(new(lineNumber, $"Parameter '{value}' is declared more than once.")); break; }
                        current = new MutableParameter(value, lineNumber, directive == "@texture" ? ShaderEffectParameterKind.Texture : null);
                        parameters.Add(current);
                        break;
                    default:
                        diagnostics.Add(new(lineNumber, $"Unknown metadata directive '{directive}'."));
                        break;
                }
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                diagnostics.Add(new(lineNumber, "Expected a metadata directive or key: value pair."));
                continue;
            }
            if (current is null)
            {
                diagnostics.Add(new(lineNumber, "Parameter metadata must follow @param or @texture."));
                continue;
            }
            current.Set(line[..colon].Trim(), line[(colon + 1)..].Trim(), lineNumber, diagnostics);
        }

        if (version is null) diagnostics.Add(new(null, "Missing @gal.effect v=1 declaration."));
        else if (version != 1) diagnostics.Add(new(null, $"Unsupported @gal.effect version '{version}'."));
        if (!string.Equals(input, "source", StringComparison.Ordinal)) diagnostics.Add(new(null, "The metadata must declare '@input source'."));
        if (stages.Count == 0) diagnostics.Add(new(null, "The metadata must declare at least one @targets stage."));

        var descriptors = parameters.Select(parameter => parameter.Build(diagnostics)).Where(parameter => parameter is not null).Cast<ShaderEffectParameterDescriptor>().ToArray();
        if (diagnostics.Count > 0) return new ShaderEffectMetadataParseResult(null, diagnostics);
        return new ShaderEffectMetadataParseResult(new ShaderEffectDescriptor(resource, version!.Value, input!, stages, descriptors), diagnostics);
    }

    private static int? ParseVersion(string value, int line, List<ShaderEffectMetadataDiagnostic> diagnostics)
    {
        const string prefix = "v=";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || !int.TryParse(value[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var version))
        {
            diagnostics.Add(new(line, "@gal.effect must use the form '@gal.effect v=1'."));
            return null;
        }
        return version;
    }

    private static void ParseStages(string value, HashSet<EffectStage> stages, int line, List<ShaderEffectMetadataDiagnostic> diagnostics)
    {
        foreach (var item in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(item, "layer", StringComparison.OrdinalIgnoreCase)) stages.Add(EffectStage.Layer);
            else if (string.Equals(item, "scenePost", StringComparison.OrdinalIgnoreCase)) stages.Add(EffectStage.ScenePost);
            else diagnostics.Add(new(line, $"Unknown effect target stage '{item}'."));
        }
    }

    private static int CountLines(string value, int endExclusive)
    {
        var lines = 1;
        for (var index = 0; index < endExclusive; index++) if (value[index] == '\n') lines++;
        return lines;
    }

    private sealed class MutableParameter(string name, int line, ShaderEffectParameterKind? fixedKind)
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
        public void Set(string key, string value, int lineNumber, List<ShaderEffectMetadataDiagnostic> diagnostics)
        {
            if (!_values.TryAdd(key, value)) diagnostics.Add(new(lineNumber, $"Parameter '{name}' declares '{key}' more than once."));
        }

        public ShaderEffectParameterDescriptor? Build(List<ShaderEffectMetadataDiagnostic> diagnostics)
        {
            if (!_values.TryGetValue("uniform", out var uniform) || string.IsNullOrWhiteSpace(uniform))
            {
                diagnostics.Add(new(line, $"Parameter '{name}' requires a uniform binding."));
                return null;
            }

            var kind = fixedKind;
            if (_values.TryGetValue("type", out var type))
            {
                var parsedKind = ParseKind(type);
                if (parsedKind is null) diagnostics.Add(new(line, $"Parameter '{name}' has an unsupported type '{type}'."));
                else kind = parsedKind;
            }
            if (kind is null)
            {
                diagnostics.Add(new(line, $"Parameter '{name}' requires a type."));
                return null;
            }
            if (fixedKind is not null && kind != fixedKind)
            {
                diagnostics.Add(new(line, $"@texture '{name}' cannot declare a non-texture type."));
                return null;
            }

            var minimum = ParseNumber("min", diagnostics);
            var maximum = ParseNumber("max", diagnostics);
            if (_values.TryGetValue("range", out var range)) (minimum, maximum) = ParseRange(range, line, diagnostics);
            var step = ParseNumber("step", diagnostics) ?? DefaultStep(kind.Value);
            if (minimum is { } min && maximum is { } max && min > max) diagnostics.Add(new(line, $"Parameter '{name}' has a minimum greater than its maximum."));
            var animatable = ParseBoolean("animatable", IsAnimatableByDefault(kind.Value), diagnostics);
            var required = ParseBoolean("required", false, diagnostics);
            var options = _values.TryGetValue("options", out var rawOptions)
                ? rawOptions.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : [];
            if (kind == ShaderEffectParameterKind.Enum && options.Length == 0)
                diagnostics.Add(new(line, $"Enum parameter '{name}' requires options."));

            var defaultValue = _values.TryGetValue("default", out var explicitDefault)
                ? explicitDefault
                : DefaultValue(kind.Value, options);
            var displayName = _values.TryGetValue("displayName", out var explicitDisplayName) ? explicitDisplayName : name;
            _values.TryGetValue("group", out var group);
            _values.TryGetValue("tooltip", out var tooltip);
            return new ShaderEffectParameterDescriptor(name, uniform, kind.Value, defaultValue, minimum, maximum, step, animatable, required, options, displayName, group, tooltip);
        }

        private double? ParseNumber(string key, List<ShaderEffectMetadataDiagnostic> diagnostics)
        {
            if (!_values.TryGetValue(key, out var value)) return null;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)) return number;
            diagnostics.Add(new(line, $"Parameter '{name}' has an invalid {key} value '{value}'."));
            return null;
        }

        private (double? Minimum, double? Maximum) ParseRange(string value, int lineNumber, List<ShaderEffectMetadataDiagnostic> diagnostics)
        {
            var separator = value.IndexOf("..", StringComparison.Ordinal);
            if (separator <= 0 || separator == value.Length - 2 ||
                !double.TryParse(value[..separator], NumberStyles.Float, CultureInfo.InvariantCulture, out var minimum) ||
                !double.TryParse(value[(separator + 2)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var maximum) ||
                !double.IsFinite(minimum) || !double.IsFinite(maximum))
            {
                diagnostics.Add(new(lineNumber, $"Parameter '{name}' has an invalid range '{value}'."));
                return (null, null);
            }
            return (minimum, maximum);
        }

        private bool ParseBoolean(string key, bool defaultValue, List<ShaderEffectMetadataDiagnostic> diagnostics)
        {
            if (!_values.TryGetValue(key, out var value)) return defaultValue;
            if (bool.TryParse(value, out var parsed)) return parsed;
            diagnostics.Add(new(line, $"Parameter '{name}' has an invalid {key} value '{value}'."));
            return defaultValue;
        }

        private static double? DefaultStep(ShaderEffectParameterKind kind) => kind switch
        {
            ShaderEffectParameterKind.Float => 0.01,
            ShaderEffectParameterKind.Integer => 1,
            _ => null
        };

        private static bool IsAnimatableByDefault(ShaderEffectParameterKind kind) => kind is
            ShaderEffectParameterKind.Float or
            ShaderEffectParameterKind.Integer or
            ShaderEffectParameterKind.Color or
            ShaderEffectParameterKind.Vector2 or
            ShaderEffectParameterKind.Vector4;

        private static string? DefaultValue(ShaderEffectParameterKind kind, string[] options) => kind switch
        {
            ShaderEffectParameterKind.Float or ShaderEffectParameterKind.Integer => "0",
            ShaderEffectParameterKind.Boolean => "false",
            ShaderEffectParameterKind.Color => "#00000000",
            ShaderEffectParameterKind.Vector2 => "[0,0]",
            ShaderEffectParameterKind.Vector4 => "[0,0,0,0]",
            ShaderEffectParameterKind.Texture => null,
            ShaderEffectParameterKind.Enum => options.Length > 0 ? options[0] : null,
            _ => null
        };

        private static ShaderEffectParameterKind? ParseKind(string type) => type.ToLowerInvariant() switch
        {
            "float" => ShaderEffectParameterKind.Float,
            "int" or "integer" => ShaderEffectParameterKind.Integer,
            "bool" or "boolean" => ShaderEffectParameterKind.Boolean,
            "color" => ShaderEffectParameterKind.Color,
            "vec2" or "vector2" => ShaderEffectParameterKind.Vector2,
            "vec4" or "vector4" => ShaderEffectParameterKind.Vector4,
            "texture" => ShaderEffectParameterKind.Texture,
            "enum" => ShaderEffectParameterKind.Enum,
            _ => null
        };
    }
}
