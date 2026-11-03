# 运行 Sample

目标：打开桌面窗口并运行自带的 `GameTestCase`。先满足[环境要求](requirements.md)，打开 PowerShell 并切换到仓库根目录；无需先创建项目。首次构建和还原可能需要一些时间。

## 从源码运行桌面示例

```powershell
pwsh -NoProfile -File scripts/samples/run-cross-platform-sample.ps1
```

执行成功后会打开 Sample 窗口。脚本构建并部署示例契约到 `GameTestCase/Contracts/`，通过 Headless CLI 编译内容到 `GameTestCase/Output/`，然后以 Debug 配置启动桌面 Sample。玩家数据写入 `GameTestCase/.galnet/`。进入窗口后完成首次语言选择，在标题页开始游戏。

无需 GUI 的逻辑示例：

```powershell
pwsh -NoProfile -File scripts/samples/run-headless-sample.ps1
```

Headless 用于终端交互和后端验证，不展示完整 Avalonia 画面，也不能代替音视频设备验收。

可用 `-Project` 指定作者项目，`-BuildOutput` 指定编译内容目录，`-Profile` 指定玩家数据目录；`-SkipBuild` 跳过契约部署和内容编译。跳过编译前，确认契约和内容与当前玩家构建匹配；自定义项目不会自动获得 GameTestCase 的契约。

## 使用发行包

如果 Release 提供匹配平台的整合 GameTestCase 包，完整解压后运行应用入口。纯 Sample 包没有游戏内容，需要先导出匹配契约的内容放到应用同级 `pak/`，或使用[游戏合包](../manual/publishing/game-distribution.md)。不要从压缩包内直接运行，也不要单独移动 EXE。

玩家发行版默认关闭内容路径参数和外部调试；开发版才能按其编译能力使用参数。详细说明见[启动与内容路径](../manual/sample/startup-and-content.md)。

## 启动失败时

- SDK 不匹配：检查 `global.json` 和当前目录下选中的 SDK。
- 原生音频构建失败：检查目标架构 C++ 工具链或目标动态库。
- 内容/契约错误：重新部署当前契约并编译；不要直接修改清单 hash 绕过检查。
- 窗口报告内容缺失：确认使用整合包，或已在正确的应用根部署 `pak/`。

接下来可以[创建自己的项目](first-project.md)，或阅读 [Sample 使用手册](../manual/sample/README.md)。
