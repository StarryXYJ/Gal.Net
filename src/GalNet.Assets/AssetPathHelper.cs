namespace GalNet.Assets;

/// <summary>
/// 资源路径规范化工具 —— 统一斜杠、去首/，小写。
/// 在 AssetManager、Archive、LocalFileProvider 中共用，消除重复实现。
/// </summary>
internal static class AssetPathHelper
{
    /// <summary>将任意路径规范为小写、正斜杠、无首/的形式。</summary>
    public static string Normalize(string path) =>
        path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
}
