# GalNet

GalNet 是一个基于 .NET 的视觉小说（Galgame）引擎与编辑器项目。它将剧情运行时、资源与内容存储、呈现抽象以及 Avalonia 编辑器分层，以支持可扩展的叙事游戏制作与发布。

项目正处于快速迭代阶段：公开 API、项目格式和编辑器工作流都可能调整。从[快速开始](docs/quick-start/README.md)运行示例或创建项目；[文档首页](docs/README.md)按 Editor 使用、Sample 使用、项目扩展、发布和技术规范分类。文档以对应源码版本为准。

## 环境要求

- .NET SDK `10.0.401`，以 [`global.json`](global.json) 为准。
- Windows 是主要开发和普通 CI 环境；其他平台前提与实际验证范围见[平台支持](docs/manual/sample/platform-support.md)。
- 原生音频源码构建需要目标平台工具链；移动与 Browser 工程需要对应 workload。见[环境与依赖](docs/quick-start/requirements.md)。

## 仓库地图

```text
src/
  Shared/          Core、Runtime、Assets、契约、执行模块与游戏宿主
  Presentation/    Avalonia 控件、渲染、页面、主题与宿主
  Infrastructure/  文件存储、音视频及输入适配
  Editor/          编辑器协议、共享逻辑、GUI 与 Headless CLI
  Samples/         共享 Player、Avalonia、Headless 与 CrossPlatform 宿主
test/              按生产边界拆分的测试与 fixture
docs/              快速开始、操作/扩展手册、技术规范与术语
features/          按 feature 保存的需求、设计、阶段计划与总结
scripts/           构建、验证、发行、维护与文档同步入口
GameTestCase/      示例工程与 smoke test 内容
```

使用 Editor 制作内容见[Editor 手册](docs/manual/editor/README.md)，使用默认玩家见[Sample 手册](docs/manual/sample/README.md)，定制示例见[扩展手册](docs/manual/extensions/README.md)。程序集边界和测试职责见[架构规范](docs/spec/architecture/README.md)。

## 构建与测试

从仓库根目录执行：

```powershell
pwsh -NoProfile -File scripts/ci/Invoke-CI.ps1 -Stage BuildTest
```

该入口生成桌面过滤器，执行匹配配置的锁定还原、构建与串行测试。首次只构建指定应用见[源码构建](docs/quick-start/build-from-source.md)；格式检查、覆盖率和完整开发说明见[构建与测试](docs/manual/development/build-and-test.md)。格式检查是显式操作，不是默认 BuildTest 的门禁。

## 运行示例

以下脚本默认编译并运行 [`GameTestCase`](GameTestCase/README.md)：

```powershell
.\scripts\samples\run-headless-sample.ps1
.\scripts\samples\run-avalonia-sample.ps1
```

Headless 示例在终端中交互；Avalonia 示例会启动桌面窗口。两个脚本都支持通过 `-Project`、`-BuildOutput` 和 `-SkipBuild` 指定已有内容。

构建仓库自带的 `GameTestCase` 时，脚本会先构建并部署 Sample.Contracts，再编译剧情；`-SkipBuild` 跳过这两步。通过 `-Project` 指向其他内容项目时，需先按 [Sample.Contracts 说明](src/Samples/GalNet.Sample.Contracts/README.md) 部署与玩家匹配的项目契约。

统计项目代码量可以运行：

```powershell
.\scripts\maintenance\count-code-lines.ps1
.\scripts\maintenance\count-code-lines.ps1 -Json
```

脚本按编辑器、测试、文档和其他源码分组，显示文件数、物理行数和非空行数。默认识别 `.cs`、`.xaml`、`.axaml` 及常见源码扩展，并跳过构建产物、依赖、缓存和 agent 工具目录。其他脚本按 contracts、samples、performance、maintenance、release 分类，见 [scripts/README.md](scripts/README.md)。

## 开发流程

较大的功能、架构调整和跨阶段重构使用 `features/F-YYYYMMDD-NN-short-slug/` 记录需求、设计、实施计划和验证总结。当前可用的模块事实同步到 `docs/spec/`，使用与扩展步骤归 `docs/manual/`；feature 目录保留过程与历史，不替代规范。正式文档只在 docs 维护，Wiki 的完整正文与分类导航由[同步工具](docs/manual/development/documentation.md)生成。

GalNet 采用 [MIT License](LICENSE) 发布，允许用于商业和非商业项目。
