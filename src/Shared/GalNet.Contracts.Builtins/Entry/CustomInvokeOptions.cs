using GalNet.Core.Parameters;

namespace GalNet.Contracts.Builtins;

/// <summary>Opaque project action arguments; the shared contract does not interpret the payload.</summary>
[GenerateFieldSchema]
public partial class CustomInvokeOptions
{
    // Constructor values initialize a DTO, without declaring schema defaults for required fields.
    public CustomInvokeOptions() { Action = ""; Argument = ""; }

    [Field(0, Required = true, AllowNull = false, EditorId = ValueEditorIds.Text)]
    public string Action { get; set; }

    [Field(1, Required = true, AllowNull = false, EditorId = "entry.multilinetext")]
    public string Argument { get; set; }
}
