# 执行记录

## 2026-10-29：P0 内容/环境检查与 P1 导出改造

### 实现

- Editor 导出面板仅保留 galpak 与内容文件夹；不再发现源码仓库或启动 powershell.exe 发布应用，相关旧选项、配置字段和四语文案删除。
- 目录模式在所选输出目录新建 pak，已存在时拒绝覆盖；两种模式均经过当前项目保存与作者快照捕获，编译在后台完成。
- 唯一 galpak 导出包含本体、localization.pak、game.pak 和全部剧情分片，不生成旁置剧情；目录导出与其逐文件一致。
- installation.json v4 覆盖全部声明载荷。安装器固定 pak/game.pak，拒绝旧索引、缺失/损坏剧情、重复/越界/未声明条目；不保留旧导出/安装分支。
- 重复安装保留已有内容和外置翻译，但仍验证本次源归档 hash。维护者 Publish.ps1 不再从 galpak 邻近目录隐式复制剧情或资源补丁。
- 已同步 content-packaging、file-formats 和 release-packaging 中此次已验证的事实。
- 未改动用户初始化的 CrossPlatform 工程；未安装或升级 WSL 软件。

### 验证

| 检查 | 结果/证据 |
| --- | --- |
| Integration Release build | 通过，0 错误；out/cross-platform-p1-integration-build.log |
| Editor Release build | 通过，0 错误；out/cross-platform-p1-editor-build.log |
| Integration 全量 | 181 项通过；另有 3 项 Explicit 原生/发行测试未执行；out/cross-platform-p1-results/integration-all.trx |
| 新增 Editor 挂载测试 | 压缩包和目录两例通过，覆盖真实面板、仓库外项目、实际导出/安装及目录冲突不覆盖；out/cross-platform-p1-results/editor-content-export.trx |
| Editor 全量 | 354 通过、2 失败，未声明全绿；out/cross-platform-p1-results/editor-all.trx |
| 失败隔离 | 不运行新增导出测试，仅运行两个失败用例仍重现；out/cross-platform-p1-results/editor-existing-failures.trx |
| 自包含 CLI 发布 | Windows win-x64，自包含、关闭 R2R 的候选通过；out/cross-platform-p1-tools、out/cross-platform-p1-cli-publish.log |
| SDK/工具发现隔离 | PATH 仅保留候选工具目录，DOTNET_ROOT/DOTNET_ROOT_X64 指向其自包含运行时，关闭 multilevel lookup；不存在可发现 dotnet 命令时创建项目、export、project export-directory、galpak install 全部通过；out/cross-platform-p1-nosdk-24eae9dd |
| 隔离导出一致性 | 上述独立 CLI 的归档安装与目录导出逐文件 SHA-256 一致；真实剧情分包内容另由 StoryPackageExportTests 覆盖 |
| 文案/脚本 | 四种语言 Export.* 键集合一致；Publish.ps1 PowerShell AST 解析通过；导出 VM/Service 无进程、脚本或 SDK 调用依赖 |
| 文本检查 | git diff --check 通过；Git 提示的 LF/CRLF 转换不是空白错误 |

构建首次遇到 Avalonia telemetry 写入 AppData 权限错误，按已有 lesson 设置 AVALONIA_TELEMETRY_OPTOUT=1 后通过，未改产品代码绕过。初次更新测试夹具时设置默认 JSON 不完整，以及新旧设置字节实际相同，已分别修正为有效配置及真正不同的配置后重新运行集成全量并通过。

### Editor 全量的两处失败

1. FoundationViewsHaveNoUnlistedLiteralUserInterfaceText：ProjectSettingsPanelView.axaml 的 Content="…" 不在测试允许集合中；该按钮存在于 HEAD，此次未修改该页面或对应测试。
2. GeneratedCheckboxIsCenteredInTheEditorColumnAtWideAndNarrowWidths：既有字段复选框中心位置断言失败（预期 258，实际 8）；相关控件与测试此次未修改，独立进程不运行新增导出测试时仍失败。

本次不将这些问题隐藏或改为跳过，保留失败证据供对应 UI 工作处理。

### 状态与待办

- P0：in-progress。内容闭包与本机环境检查完成，见 [环境矩阵](environment-matrix.md)；Ubuntu 24.04 基线、macOS、Android/iOS 与 SDL 探针尚未完成。
- P1：in-progress。实现和相关自动化验证完成，但尚未在干净无 SDK 机器上通过分发 Editor 原生 UI 操作完成导出。自包含 CLI 工具发现隔离与 headless 面板测试不能冒充此原生端到端烟测。
- P2–P8：planned；未实现独立 compose/build-compose、三平台完整媒体发行或五平台模板运行。
- 后续优先补 P1 分发烟测与 P2 布局/无 SDK 合包切片；跨平台项目仍等桌面阶段稳定后在 P6 集中接入。
