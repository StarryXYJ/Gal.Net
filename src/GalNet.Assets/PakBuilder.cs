using GalNet.Core.Assets;

namespace GalNet.Assets;

/// <summary>
/// .pak 文件打包工具 —— 将一组资源文件打包为 .pak 归档。
/// 供编辑器导出功能使用。
/// </summary>
public static class PakBuilder
{
    /// <summary>
    /// 将一组 IGameFile 打包为 .pak 格式的字节数组。
    /// </summary>
    /// <param name="archiveName">归档名称（仅用于日志）</param>
    /// <param name="files">资源文件列表</param>
    /// <param name="compression">压缩模式（默认 Brotli）</param>
    /// <returns>.pak 文件字节数组</returns>
    public static byte[] Build(string archiveName, IReadOnlyList<IGameFile> files,
        CompressionMode compression = CompressionMode.Brotli)
    {
        return Archive.Serialize(archiveName, files, compression);
    }

    /// <summary>
    /// 将一组资源文件打包并写入磁盘。
    /// </summary>
    public static void BuildToFile(string pakPath, IReadOnlyList<IGameFile> files,
        CompressionMode compression = CompressionMode.Brotli)
    {
        var data = Build(Path.GetFileNameWithoutExtension(pakPath), files, compression);
        File.WriteAllBytes(pakPath, data);
    }
}
