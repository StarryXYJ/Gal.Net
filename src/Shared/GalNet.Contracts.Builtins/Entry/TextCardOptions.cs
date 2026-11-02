using System.Text.Json.Serialization;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Parameters;

namespace GalNet.Contracts.Builtins;

[GenerateFieldSchema]
public partial class TextCardOptions
{
    [Common, Field(0, Required = true, AllowNull = false, EditorId = "entry.multilinetext"), LocalizedText] public string Content { get; set; } = "";
    [Common, Field(1, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringEnumValueConverter<TextCardPosition>))] public TextCardPosition Position { get; set; } = TextCardPosition.BottomLeft;
    [Common, Field(2)] public string StyleName { get; set; } = "default";
    [Field(3, AllowNull = true, Minimum = 0, Maximum = 3600)] public float? HoldSeconds { get; set; }
    [Field(4)] public bool Blocking { get; set; }
}
