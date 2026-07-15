using GalNet.Core.Handler;

namespace GalNet.Runtime.Handlers;

public sealed class VariableHandler : EntryHandler
{
    public override string EntryType => "variable";
    public override bool IsBlocking => false;

    public VariableHandler()
    {
        On("set", ctx =>
        {
            var rawValue = ctx.GetString("value", "");
            var type = ctx.GetString("type", "string");
            ctx.Runtime.SetVariable(ctx.GetString("target", ""), ParseValue(rawValue, type));
        });
        On("eval", ctx =>
        {
            var expression = ctx.GetString("expression", "");
            if (!string.IsNullOrEmpty(expression))
            {
                var result = ctx.Runtime.EvaluateExpression(expression);
                if (result != null)
                    ctx.Runtime.SetVariable(ctx.GetString("target", ""), result);
            }
        });
    }

    public override void Start(EntryContext ctx) => Dispatch(ctx, defaultCommand: "set");

    private static object ParseValue(string raw, string type) => type switch
    {
        "bool" => bool.TryParse(raw, out var b) && b,
        "int" => int.TryParse(raw, out var i) ? i : 0,
        "float" => float.TryParse(raw, out var f) ? f : 0f,
        _ => raw
    };
}
