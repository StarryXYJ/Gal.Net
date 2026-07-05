using GalNet.Core.Handler;
using GalNet.Core.Variable;

namespace GalNet.Runtime.Handlers;

/// <summary>
/// 变量操作 —— 非阻塞。通过 Runtime 直接读写变量。
/// 参数：action（set/mod/eval）、target、value、type、expression
/// </summary>
public sealed class VariableHandler : EntryHandler
{
    public override string EntryType => "variable";
    public override bool IsBlocking => false;

    public override void Start(EntryContext ctx)
    {
        var route = new VariableRoute(ctx.GetString("target", ""));
        var action = ctx.GetString("action", "set");

        switch (action)
        {
            case "set":
            {
                var rawValue = ctx.GetString("value", "");
                var type = ctx.GetString("type", "string");
                ctx.Runtime.SetVariable(route, ParseValue(rawValue, type));
                break;
            }
            case "eval":
            {
                var expression = ctx.GetString("expression", "");
                if (!string.IsNullOrEmpty(expression))
                {
                    var result = ctx.Runtime.EvaluateExpression(expression);
                    if (result != null)
                        ctx.Runtime.SetVariable(route, result);
                }
                break;
            }
        }
    }

    private static object ParseValue(string raw, string type) => type switch
    {
        "bool" => bool.TryParse(raw, out var b) && b,
        "int" => int.TryParse(raw, out var i) ? i : 0,
        "float" => float.TryParse(raw, out var f) ? f : 0f,
        _ => raw
    };
}
