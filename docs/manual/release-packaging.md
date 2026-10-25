# 发布 Editor 与 Sample

发布流程由 PowerShell 编排，内容编译、`.galpak` 校验与展开使用 Headless C# 工具。产物都写到仓库的 `out/release/<version>/`，不会建立单独的 artifact 目录。

## 本地打包

```powershell
pwsh -File scripts/release/Publish.ps1 -Package All -Version 1.2.3
```

`All` 会生成三种 Windows x64 ZIP：

- `GalNet-Editor-1.2.3-win-x64.zip`：Editor。
- `GalNet-Sample-1.2.3-win-x64.zip`：纯 Sample 运行时，不带游戏内容；`Run-Sample.cmd` 接收一个展开后的游戏目录。
- `GalNet-Sample-GameTestCase-1.2.3-win-x64.zip`：Sample 与已展开的 `GameTestCase/`。运行 `Run-GameTestCase.cmd` 即可开始，不会在用户机器上先解压一个中间 `.galpak`。

每个 ZIP 附有 `.sha256` 校验文件，ZIP 内的 `release-manifest.json` 列出所有文件的 SHA-256、包版本和构建 commit。发布使用 `win-x64`、Self-contained 和 ReadyToRun；压缩包解压后不要求玩家另装 .NET Runtime。契约 DLL 仍按 ReadyToRun 配置排除转换，保持严格字节身份。

可单独生成一种包：

```powershell
pwsh -File scripts/release/Publish.ps1 -Package Editor -Version 1.2.3
pwsh -File scripts/release/Publish.ps1 -Package Sample -Version 1.2.3
pwsh -File scripts/release/Publish.ps1 -Package SampleIntegrated -Version 1.2.3 -ProjectDirectory GameTestCase
pwsh -File scripts/release/Publish.ps1 -Package SampleIntegrated -Version 1.2.3 -Galpak out/GameTestCase.galpak
```

整合源码时脚本只复制 `Assets/`、`Graph/`、`I18n/` 与 `settings.json` 到临时工作区，然后用项目契约编译内容并直接生成最终展开目录。不会先生成 `.galpak`。传入 `.galpak` 时，Headless 的 `galpak install` 校验清单、路径和每个 payload 的长度及 SHA-256 后，直接展开到 Sample 包里的 `GameTestCase/`。

PowerShell 发布脚本也可从 Editor 的“导出游戏”窗口运行。选择 Editor、纯 Sample 或整合 Sample；整合 Sample 默认取当前打开项目，或选择一个 `.galpak` 作为输入。Editor 项目必须从 GalNet 仓库检出目录打开，才能找到本地发布脚本。原来的 Galpak 游戏包导出仍可从同一窗口选择。

## GitHub Release 与版本

Release workflow 由推送版本 tag 触发。稳定版本使用 `vMAJOR.MINOR.PATCH`，预览版本使用 `vMAJOR.MINOR.PATCH-preview.N`：

```powershell
git tag v1.2.3
git push origin v1.2.3

git tag v1.3.0-preview.1
git push origin v1.3.0-preview.1
```

GitHub Actions 检查 tag 格式，构建三种 ZIP 和校验文件，并为该 tag 创建 Release。`preview` tag 会标记为预发布版。版本写入文件名和 `release-manifest.json`，不改程序集身份或契约 DLL 字节。

本地未带版本 tag 时默认版本为 `0.1.0-dev`；可通过 `-Version` 显式指定。可用 `-OutputRoot` 改变输出位置，默认仍是 `out/release`。
