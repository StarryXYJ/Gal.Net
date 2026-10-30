# 原生 NuGet 内容不一定遵守发布 RID 与符号开关

- 状态：confirmed
- 来源：F-20261027-04-tagged-release，用户追问发行体积。
- 现象：win-x64 自包含发布仍带 VLC win-x86 / win-arm64，合计约 178 MiB；Editor 的 Skia/HarfBuzz 原生 PDB 约 100 MiB。
- 根因：原生依赖通过 NuGet 内容/构建规则复制多架构和符号文件，不能从 `-r win-x64` 或托管符号复制设置推导最终产物只含目标架构、没有 PDB。
- 发现：对真实 ZIP 解压后的文件按路径、扩展名和字节统计，而非仅检查 csproj 属性。
- 做法：在最终应用载荷生成 manifest/hash 之前过滤 PDB 与明确不支持的 VLC 架构；发布验收同时拒绝多余文件并确认 x64 VLC 核心/codec 仍在。禁止按 DLL 扩展名一律删除或在 hash 封存后修改 ZIP。
- 边界：只调整应用发行载荷，构建目录可继续保留符号；x64 插件仍需按实际音视频能力验收后才能进一步裁减。
- 文档：docs/manual/publishing/engine-release.md。
