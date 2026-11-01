# 已加载构建任务与不完整 NuGet 缓存

状态：confirmed
来源：2026-11-01 Sample 开场后续修复（独立实现，未创建 feature）。

## 现象与根因

NuGet.targets/RestoreEx 在安装 Avalonia.BuildServices 11.3.2 时 RemoveDirectoryRecursive 报 Avalonia.BuildServices.dll Access denied。全局版本目录只剩 tools/netstandard2.0 下的 DLL，没有 .nupkg.metadata、targets 或归档。读可用，ReadWrite 打开探针返回文件占用；Windows Restart Manager 精确定位为 Visual Studio。dotnet build-server shutdown 不能卸载 VS 进程内的构建任务，目录重命名也失败。不能将这个状态简单归因为项目源代码或 ACL。

## 修正与验证

保留 VS 运行。工作区已有同版本完整 NuGet 缓存；先验证归档原始 SHA512 与 .nupkg.sha512 一致、.nupkg.metadata 的 contentHash 与提交锁文件一致、被占用 DLL 的 SHA256 与完整包中的 DLL 一致，再只补回缺失文件，最后发布 .nupkg.metadata；未覆盖 DLL、未改权限、未终止 VS。随后完整 locked restore 返回 0。

签名包的 NuGet contentHash 与原始归档 .nupkg.sha512 不一定相同，本次分别为 qHDT… 与 Iz+G…，不要拿原始归档 SHA512 直接与锁文件 contentHash 比较后断言损坏。不能仅复制一份未经核验的 metadata 来掩盖缺失文件。

## 适用范围

Windows 全局 NuGet 缓存部分清理或安装中断，且已加载构建任务阻止删除时。先确认实际锁文件所有者；不要清空整个全局缓存、广泛修改 ACL 或强制结束用户 IDE。没有可信同版本恢复源或 DLL 不一致时，不擅自替换已加载文件。
