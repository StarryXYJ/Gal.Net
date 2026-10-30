# 本地 CI 与发布

图形示例统一维护 `GalNet.Sample.CrossPlatform`，桌面使用其 Desktop 入口，移动端使用对应平台入口。旧 Avalonia 桌面 Sample 及专属工具仅保留在本机 Git 忽略的 `local-only/`，不参与构建、同步或后续维护。

## 本地生成与验证

安装 `global.json` 固定的 SDK、目标平台的 native 工具链和所需 workload。NuGet 构建及仓库外消费者校验：

```powershell
pwsh -NoProfile -File scripts/release/Release.ps1 -Target NuGet -Version 0.1.0-dev.1
```

应用使用 [跨平台发布矩阵](../../../scripts/ci/README-applications.md) 的 `publish-applications.py` 与 `cross-platform-release.py`；正式流水线见 `.github/workflows/release-build.yml`。分别保留每个宿主/部署方式的锁定还原、发布和封装证据。桌面包采用普通 IL 发布；Editor 的独立 ReadyToRun 配置见 [ReadyToRun](ready-to-run.md)。

应用根包含入口程序、`lib/`、`docs/` 与 `pak/`。契约来自实际目标发布载荷，内容必须使用字节相同的 Core、Builtins、项目契约闭包导出。玩家数据按 projectId 摘要存储在用户 profile，不依赖安装目录或 buildId。

## tag、CI 与公开上传

版本 tag 指向已提交 commit：`v1.2.3` 或 `v1.3.0-preview.1`。CI 自动发布同时要求 tag 指向的提交已经包含在主分支 `origin/master` 的历史中；允许为主分支历史提交发版，不要求它仍是最新 HEAD。支持先合并再推 tag，也支持先推 tag 再合并：未合并 tag 暂不构建，master 更新时自动发现这次新合入历史的版本 tag，包含源分支提交上的 tag。没有新版本 tag 的普通合并不发布。

先 tag 后合并应使用保留源提交身份的 merge/fast-forward；squash/rebase 会产生新提交，原 tag 不会自动移动，这种方式应在最终 master 提交上打 tag。CI 不移动或重建版本 tag。

精确 tag 构建要求工作区干净、tag 指向 HEAD；不会使用最近祖先 tag 给新提交命名。

```powershell
pwsh -File scripts/release/Release.ps1 -Target NuGet -Tag v1.2.3
```

Release workflow 监听 master 与版本 tag push；发现候选后调用 `release-build.yml`，同一 tag 串行，已公开版本直接跳过。checkout 固定为 tag commit，并校验精确 tag 与 master 归属。NuGet 消费者校验、Windows/Linux 桌面发布、Android Mono AOT 发布并行执行；每个桌面主机顺序构建自包含与 FrameworkDependent 两种部署，复用原生音频构建和 Linux 私有媒体闭包。发行不重复跑完整 CI 测试，也不再调用旧 Windows Sample 的应用打包链路。

公开应用矩阵为：Windows/Linux Editor 各两种部署；Windows/Linux CrossPlatform Sample 的 Developer、Player、Bound 三种模式各两种部署；Android 仅 Bound ARM64 Mono AOT APK。另有每个桌面 RID 一份 Sample 契约 ZIP，共 19 个应用/契约附件。跨平台桌面包当前使用普通 IL 自包含/框架依赖发布，不以旧文件名宣称 R2R。Windows 使用 ZIP，Linux 使用保留权限的 tar.gz。NuGet 仍独立构建并通过外部消费者校验。

`cross-platform-release.py` 校验原始载荷、内容/宿主契约身份与归档内容；最终按来源 commit、版本、完整平台/部署矩阵和文件哈希封存 schemaVersion 2 索引，附件缺失或混入其他版本时拒绝发布。publish 下载同一候选的全部平台产物后封存并上传，不重新编译；ZIP/tar.gz/APK/nupkg artifact 不再重复压缩。`workflow_dispatch` 也完成完整矩阵封存，但只上传候选 artifact，不公开发布。

公开上传需要 GitHub 权限和名为 `NUGET_API_KEY` 的 NuGet 发布凭据；可传入 Trusted Publishing 获取的短期 key。当前 workflow 从同名 GitHub secret 读取，首次发布前需配置。在本地也可上传已经验收的精确 tag 索引：

```powershell
pwsh -File scripts/release/Push.ps1 -Index out/release/1.2.3/release-index.json
```

Push 先检查索引、全部文件哈希、版本和消费者验收状态；GitHub 目标在创建 draft 前检查 schemaVersion 2 的 19 个跨平台附件名称与数量。旧本地 Windows schemaVersion 1 索引仍使用七 ZIP 门禁。随后创建 GitHub draft，按依赖顺序上传 NuGet、上传附件，再公开 draft，preview 标记 prerelease。每个成功步骤记录在 `push-state.json`，失败时使用原文件和原 journal 恢复；不跳过未知重复版本，不覆盖公开包。NuGet/GitHub 之间没有事务，上传中断可能留下部分公开 NuGet 包。上传脚本本身不会创建或推送 tag。

### 可复用的玩家编译模式和游戏绑定

`GalNetPlayerMode` 有三种值：`Developer` 开启 `GALNET_GAME_DEBUG` 和 `GALNET_LAUNCH_ARGS`，可加载外部内容；`Player` 只开启 `GALNET_LAUNCH_ARGS`；`Bound` 两者均关闭，只加载包内游戏。不要再把允许加载外部游戏等同于允许调试。调试服务/包引用仍须按模式有条件引入，模式本身不会自动引用调试 UI。

仓库 Sample 从共享 props/targets 使用这些规则，`GalNet.Desktop` NuGet 通过 buildTransitive 向其他项目提供同一编译规则。外部宿主按 `GalNetPlayerMode` 条件编写启动参数与调试入口；共享 `AvaloniaGameSession` 会在加载内容前执行 `GameLaunchPolicy`。默认从入口程序集读取编译元数据，移动平台或共享 UI 程序集可在 `AvaloniaGameHostOptions.LaunchPolicy` 中显式传入 `GameLaunchPolicy.FromAssembly(typeof(YourApp).Assembly)`；若该类型在类库中，还应在该类库设置 `GalNetPlayerAssembly=true`，让编译目标在该程序集写入模式与绑定元数据。宿主不得另开绕过这一策略的游戏加载入口。

Bound 构建需要 `GalNetGamePackage` 和从该包校验后提取的 `GalNetBoundProjectId`。绑定 `projectId`，不绑定每次内容更新都会变化的 buildId；它用于选择正确项目，不是防篡改或 DRM。打包工具必须同时检查游戏契约与目标宿主字节身份。

其他项目可运行 Editor 随附的 `Tools/bind-game.ps1`：

```powershell
pwsh -File Tools/bind-game.ps1 -Package /path/to/YourGame.galpak -OutputDirectory /path/to/new-binding
```

工具用同发行版 CLI 完整校验/安装指定游戏包，再读取自己的项目身份，生成 `GameBinding.props` 与已验证的 `pak/`。在项目的 `Directory.Build.props` 中导入生成的 props，或把相同属性传给 `dotnet publish`；将该 `pak/` 部署到应用同级。该配置包含本机包路径，维护者应按自己的项目路径重新生成。正式 Bound Sample 使用 GameTestCase 自身导出包身份，其他项目使用它们自己的包；共享运行时不写死 GameTestCase。

## 维护者本地应用入口

使用 `scripts/ci/publish-applications.py` 构建 CrossPlatform 各模式原始载荷，再使用 `scripts/release/cross-platform-release.py` 封装与验证。参数和前置条件见 [应用发布矩阵](../../../scripts/ci/README-applications.md)，完整步骤见 `.github/workflows/release-build.yml`。旧 Avalonia Sample 的 Windows R2R 应用脚本已退役，不再作为维护入口。

### SDL3 桌面输入部署

`GalNet.Input.Sdl3` 使用自己的窄源生成 C ABI 绑定，原生资产由固定 `SDL3-CS.Windows/Linux/MacOS` 3.4.18.1 包提供（SDL 3.4.18），不引用其托管主循环。依赖锁定内容哈希；`ThirdParty/SDL3/native-runtime.json` 记录 native 版本、包来源/提交和各 RID 原生 SHA256。该清单是资产来源记录，不能用所列 RID 推断已经完成平台或物理设备验收。

普通 SDK 输出将私有库放在运行目录或 `runtimes/<rid>/native`；数据源只搜索这两个应用私有位置，不回退到系统 SDL。维护者 Windows/Linux 封装及源码 `build-compose` 将第三方清单和两份许可移动至 `docs/ThirdParty/SDL3/`；对应库仍在 lib。GalNet.Desktop/Avalonia 聚合包通过传递 native 依赖和 buildTransitive notice 内容保留同样信息，分发时应一起交付。

GameHost 绑定窗口时才初始化 SDL，解绑/关闭释放自有租约。设备输入只启用 gamepad/joystick/events，不创建 SDL 游戏窗口。缺库、版本错误或外部 input owner 冲突会记录原因并使手柄不可用，键鼠仍可用。定制宿主应在 UI/main 线程创建、更新、释放 `IStandardGamepadSource`，并将快照交给 GameView 的 `GamepadInputSession`；其他 SDL 输入消费者需要显式协调所有权，不能同时独立消费同一输入事件范围。

Windows/WSLg 已验证正式适配的虚拟设备、焦点、断连、生命周期及缺库降级。物理标准手柄、原生 Linux 桌面、macOS 和移动初始化继续保留未验收；不会以包内存在 dylib 或虚拟设备成功代替这些验收。
