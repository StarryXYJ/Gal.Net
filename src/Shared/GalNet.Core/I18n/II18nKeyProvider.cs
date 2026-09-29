namespace GalNet.Core.I18n;

/// <summary>
/// I18n 键提供器 —— 数据模型类实现此接口以提供自身的 i18n 键路径前缀。
/// 前缀由类的类型和内部 ID 决定（如 "Game.Node.group_intro"），
/// 调用方拼接后缀字段名（如 ".Text"）生成完整键。
/// </summary>
public interface II18nKeyProvider
{
    /// <summary>返回此对象的 i18n 键路径前缀。</summary>
    string GetI18nKey();
}
