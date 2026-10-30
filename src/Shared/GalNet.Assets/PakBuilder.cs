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
        IResourceTypeCatalog resourceTypes, CompressionMode compression = CompressionMode.Brotli, PakDescriptor? descriptor = null)
    {
        return Archive.Serialize(archiveName, files, resourceTypes, compression, descriptor);
    }

    /// <summary>
    /// 将一组资源文件打包并写入磁盘。
    /// </summary>
    public static void BuildToFile(string pakPath, IReadOnlyList<IGameFile> files,
        IResourceTypeCatalog resourceTypes, CompressionMode compression = CompressionMode.Brotli, PakDescriptor? descriptor = null)
    {
        var prepared = Archive.PrepareSerialization(files, resourceTypes, compression, descriptor);
        using var output = new FileStream(pakPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        prepared.WriteTo(output);
    }
}
