# 稳定契约构建身份

日期：2026-10-23。状态：verified。

## 问题

项目与导出目录 DLL 一致，重新构建的 Sample DLL 却再次不匹配。旧文件的 AssemblyInformationalVersion 包含 Git commit 285e8a0，新文件包含 3be07c2；两个 14848 字节 DLL 的反编译 IL 文本 SHA256 完全相同。当前协议按整个 DLL 的 SHA256 比较，版本元数据和 PDB 内 SourceLink URL/校验和也参与文件身份。

只去除项目契约自己的 InformationalVersion 后，不同 SourceRevisionId 的真实重建仍产生不同 SHA256：契约编译的 Core/Builtins 引用元数据也会随提交变化，不能仅靠单个属性断言已修复。

## 修正

- `Directory.Build.targets` 对 GalNet.Project.Contracts、Core 和 Contracts.Builtins 稳定 Git 相关 InformationalVersion 和 SourceLink 元数据；保留 PDB 与本地源码调试。其他 App 程序集仍可携带真实提交身份。
- 保留严格二进制匹配，不引入“相同版本号即兼容”或绕过真实契约检查。
- Sample 不匹配时报告预期和实际 SHA256，明确需要部署并重开项目。
- 增加实际两次 Rebuild 的 SourceRevisionId 稳定性检查脚本；契约源码、依赖 API、生成器或编译配置变化仍可能需要重导出。不同构建路径/SDK/配置间不承诺相同哈希。
- 本修正会使现有带提交信息的契约产物一次性变化，需重新导出/部署并重开项目。

## 验证

- `scripts/test-contract-reproducibility.ps1` 成功：SourceRevisionId 为 40 个 1 和 40 个 2 的两次真实 Rebuild 均产生 `C5D6C3E62379955CFF5E943976EF5A15CAAC1BB1B06754D9B176263B0D338AFA`。
- 正常构建并运行导出脚本成功，0 错误；导出目录与实际 Sample App 契约同为该 SHA256。PDB 保留，新增错误同时包含实际/预期哈希。
- 复用刚构建引用运行 Integration 的实际契约身份检查与真实失败子进程回归，两项通过；未放宽协议身份比较。
- 构建有既有分析/XAML 警告及 NuGet 漏洞查询网络警告；不声明全解决方案测试通过。项目中已加载的旧契约不会自动替换，需要用户关闭项目、部署导出 DLL 后重开。
