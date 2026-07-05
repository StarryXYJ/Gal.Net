using GalNet.Core.Entry;

namespace GalNet.Runtime.Loader;

/// <summary>
/// 通用复杂条目 —— 编译为单个 SimpleEntry。
/// 大多数条目类型使用此类，仅少数需要多条目展开的覆写 Compile()。
/// </summary>
public sealed class GenericComplexEntry : ComplexEntry
{
    public override IReadOnlyList<SimpleEntry> Compile()
    {
        return new[]
        {
            new SimpleEntry
            {
                Id = $"{Id}",
                SourceId = Id,
                Type = Type,
                Condition = Condition,
                Params = new Dictionary<string, string>(Params)
            }
        };
    }
}
