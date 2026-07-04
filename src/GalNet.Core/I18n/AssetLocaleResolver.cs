namespace GalNet.Core.I18n;

/// <summary>
/// 资源国际化解析器 —— 按目录 fallback 规则查找本地化资源文件。
/// 规则：先查 /Assets/{Type}/{locale}/{fileName}，不存在则回退到 /Assets/{Type}/{fileName}。
/// </summary>
public sealed class AssetLocaleResolver
{
    /// <summary>资源根目录（如项目 Assets 路径）</summary>
    public string BasePath { get; }

    public AssetLocaleResolver(string basePath)
    {
        BasePath = basePath;
    }

    /// <summary>
    /// 解析资源的实际文件路径。
    /// </summary>
    /// <param name="assetType">资源类型（"Layer", "Audio", "Video" 等）</param>
    /// <param name="fileName">文件名（含扩展名）</param>
    /// <param name="locale">目标语言</param>
    /// <returns>存在的文件路径，若都不存在则返回默认路径</returns>
    public string ResolveAssetPath(string assetType, string fileName, I18nLocale locale)
    {
        // 先查本地化版本
        var localizedPath = Path.Combine(BasePath, assetType, locale.Code, fileName);
        if (File.Exists(localizedPath))
            return localizedPath;

        // 回退默认
        return Path.Combine(BasePath, assetType, fileName);
    }

    /// <summary>检查指定语言版本是否存在</summary>
    public bool HasLocaleVariant(string assetType, string fileName, I18nLocale locale)
    {
        var localizedPath = Path.Combine(BasePath, assetType, locale.Code, fileName);
        return File.Exists(localizedPath);
    }
}
