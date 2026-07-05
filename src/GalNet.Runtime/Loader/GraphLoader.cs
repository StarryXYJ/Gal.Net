using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Graph;

namespace GalNet.Runtime.Loader;

/// <summary>
/// 从 graph.json 加载图结构。
/// </summary>
public static class GraphLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new NodeJsonConverter() }
    };

    /// <summary>从文件路径加载图。</summary>
    public static Graph LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return LoadFromJson(json);
    }

    /// <summary>从 JSON 字符串加载图。</summary>
    public static Graph LoadFromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<GraphDto>(json, Options)
                  ?? throw new InvalidOperationException("Failed to deserialize graph.json");

        var graph = new Graph
        {
            Name = dto.Name,
            RootNodeId = dto.RootNodeId,
            Nodes = dto.Nodes?.Select(ConvertNode).ToList() ?? [],
            Edges = dto.Edges?.Select(e => new Edge(e.FromNodeId, e.FromOutlet, e.ToNodeId)).ToList() ?? []
        };

        return graph;
    }

    private static Node ConvertNode(NodeDto dto) => dto.Type switch
    {
        "Group" => new Group
        {
            Id = dto.Id,
            Name = dto.Name ?? "",
            Entries = [] // entries loaded separately from .galgroup
        },
        "Branch" => new Branch
        {
            Id = dto.Id,
            Name = dto.Name ?? "",
            BranchType = Enum.TryParse<BranchType>(dto.BranchType, out var bt) ? bt : BranchType.Condition,
            Options = dto.Options?.Select(o => new BranchOption
            {
                Text = o.Text ?? "",
                Condition = o.Condition ?? ""
            }).ToList() ?? [],
            Conditions = dto.Conditions?.Select(c => new BranchCondition
            {
                Expression = c.Expression ?? ""
            }).ToList() ?? []
        },
        _ => throw new NotSupportedException($"Unknown node type: {dto.Type}")
    };

    // ── DTO types ──

    private sealed class GraphDto
    {
        public string Name { get; set; } = "";
        public string RootNodeId { get; set; } = "";
        public List<NodeDto>? Nodes { get; set; }
        public List<EdgeDto>? Edges { get; set; }
    }

    private sealed class NodeDto
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string? Name { get; set; }
        public string? BranchType { get; set; }
        public List<OptionDto>? Options { get; set; }
        public List<ConditionDto>? Conditions { get; set; }
    }

    private sealed class EdgeDto
    {
        public string FromNodeId { get; set; } = "";
        public int FromOutlet { get; set; }
        public string ToNodeId { get; set; } = "";
    }

    private sealed class OptionDto
    {
        public string? Text { get; set; }
        public string? Condition { get; set; }
    }

    private sealed class ConditionDto
    {
        public string? Expression { get; set; }
    }

    /// <summary>
    /// Node 多态反序列化器。根据 "type" 字段分发。
    /// </summary>
    private sealed class NodeJsonConverter : JsonConverter<NodeDto>
    {
        public override NodeDto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var element = doc.RootElement;

            var dto = new NodeDto
            {
                Id = element.GetProperty("id").GetString() ?? "",
                Type = element.GetProperty("type").GetString() ?? "",
            };

            if (element.TryGetProperty("name", out var nameProp))
                dto.Name = nameProp.GetString();

            if (element.TryGetProperty("branchType", out var btProp))
                dto.BranchType = btProp.GetString();

            if (element.TryGetProperty("options", out var optsProp))
            {
                dto.Options = [];
                foreach (var opt in optsProp.EnumerateArray())
                {
                    dto.Options.Add(new OptionDto
                    {
                        Text = opt.TryGetProperty("text", out var t) ? t.GetString() : "",
                        Condition = opt.TryGetProperty("condition", out var c) ? c.GetString() : ""
                    });
                }
            }

            if (element.TryGetProperty("conditions", out var condsProp))
            {
                dto.Conditions = [];
                foreach (var cond in condsProp.EnumerateArray())
                {
                    dto.Conditions.Add(new ConditionDto
                    {
                        Expression = cond.TryGetProperty("expression", out var e) ? e.GetString() : ""
                    });
                }
            }

            return dto;
        }

        public override void Write(Utf8JsonWriter writer, NodeDto value, JsonSerializerOptions options)
            => throw new NotSupportedException();
    }
}
