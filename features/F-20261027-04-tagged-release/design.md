# Tag 驱动的 NuGet 与 Windows Release 设计

日期：2026-10-27。状态：提案；本文中的新命令、文件和接口尚未实现。

## 需求与边界

先让本地在无发布凭据时产出完整可验收文件，随后让 CI 在推送 tag 时调用相同脚本。Editor 仅提供 R2R；Sample 提供 R2R 与 NativeAOT，两者均有纯玩家和整合 GameTestCase 两种 ZIP。独立发布给 Editor 使用的 Sample Contracts IL DLL。

首期 RID 建议为 win-x64。只限制应用与原生二进制的交付平台，不给纯托管 NuGet 包人为加 Windows 限制。NuGet 第一批保持现有 net10.0；SchemaGenerator 保持 netstandard2.0。不在本功能中承诺 ARM64、Linux/macOS、移动端、单文件发行或安装器。

## 已核实的现状

- `scripts/release/Publish.ps1` 已生成 Editor / Sample / Sample-GameTestCase 三种 ZIP、每包 manifest 和 SHA-256，固定 Windows x64、Self-contained、R2R。
- `.github/workflows/release.yml` 已监听 v* tag，接受正式版与 preview.N，调用该脚本再创建 Release；尚无 NuGet 发布步骤。
- 现有脚本无精确 tag 时用最近的历史 tag 推导版本，有把新提交误标为旧正式版的风险；manifest 中版本尚未统一注入包与应用元数据。
- `Directory.Build.targets` 对 Core / Contracts.Builtins / GalNet.Project.Contracts 稳定 Git 元数据，并排除三者的 R2R 转换，维护严格契约字节身份。
- Sample Contracts 工程输出名是 `GalNet.Project.Contracts.dll`。Editor.DefaultContracts 也使用这个程序集名，但语义不同，不能互相替换。
- `export-sample-contracts.ps1` 当前会重新构建桌面 Sample，并读取普通 bin，不能直接用于保证隔离发布图的产物一致性。
- Windows x64 Headless NativeAOT 已有自检；桌面 Avalonia/Skia、LibVLC、真实音频设备和桌面 UI 闭包尚未完成 AOT 验收。
- 源生成器通过 ProjectReference 进入源码构建，但还需设计 NuGet analyzer 资产。

## NuGet：按消费边界聚合为 5 个包

根据用户反馈，取消一程序集一包的方案。保留现有 csproj、程序集名、命名空间与源码依赖边界，在打包层把多个项目的 DLL 放进同一个 nupkg；不做程序集物理合并，也不把 Sample 或 Editor 可执行程序当库发布。

| PackageId | 包含现有项目（下列项目均以 GalNet. 开头） | 包间依赖 |
| --- | --- | --- |
| GalNet.Contracts | Core、Contracts.Builtins | 无其他 GalNet 运行时包 |
| GalNet.Engine | Runtime、Primitives.Builtins、Presentation.Abstractions、GameHost、Assets、Debugging、Diagnostics | Contracts |
| GalNet.Desktop | Storage.FileSystem、Media.Audio、Media.Miniaudio、Media.LibVlc | Engine |
| GalNet.Avalonia | Avalonia.Controls、Avalonia.Rendering、Avalonia.GameView、Avalonia.Themes、Avalonia.GameHost | Desktop（间接获得 Engine / Contracts） |
| GalNet.SchemaGenerator | SchemaGenerator，作为编译器 analyzer | 不添加 GalNet 运行时依赖 |

合计 5 个统一版本的包，收纳 19 个项目。Editor 不发布 SDK，修改 Editor 直接克隆源码；Editor.Abstraction、Editor.Shared 和 Editor.DefaultContracts 仅参与源码构建及应用发行。包内含多个真实 DLL，不是仍然依赖二十多个小包的元包。

使用路径：自定义 Contracts 工程引用 GalNet.Contracts + GalNet.SchemaGenerator，后者设置 PrivateAssets=all；普通 Avalonia 游戏引用 GalNet.Avalonia，间接获得默认后端与 Engine；自备存储/媒体/展示的 Headless 或其他 UI 宿主引用 GalNet.Engine，需要默认后端时再加 GalNet.Desktop。Sample 自身的 GalNet.Project.Contracts.dll 仍为独立 Release 资产，不发布通用 NuGet。

Engine 聚合剧情执行、默认指令、内容读取抽象、宿主会话和展示端口，不带文件存储、设备音频、LibVLC 或 Avalonia 实现。Debugging/Diagnostics 两个小型托管库随 Engine 分发，便于消费者接入协议/诊断，不代表玩家启用调试或基准功能。

Contracts 单独分包的依据是实际消费需求：作者契约项目只需要类型和 schema，而不需要 Runtime 的 NCalc/Serilog 等依赖。当前 Core 包含的类型不全是狭义 DTO，因此 Contracts 的定义是“扩展与内容契约基础”，不承诺纯 DTO 程序集；先通过包装现有两个 DLL 实现，不为包名重构源码。

Desktop 表示默认文件/媒体后端组合；其中 Storage.FileSystem、Media.Audio 等托管代码本身不被宣称仅支持桌面，但默认 Miniaudio 原生资产首期只有 win-x64。聚合的代价是只需要 PCM 的消费者也会取得该后端包的依赖；等存在独立音频/移动宿主消费需求再拆包。Avalonia 保留五个相关项目的聚合，包含默认页面与主题，减少首次接入选择。

SchemaGenerator 保留独立包，明确其只作用于编译期；单独引用它不会为契约项目引入 Engine、Desktop 或 Avalonia。

### 对照其他框架后的取舍

- MonoGame 的官方平台文档提供 WindowsDX / DesktopGL 等后端包入口，体现按真实平台选择分发；不要求把 GalNet 每个内部模块都变成包。
- Stride 的官方包列出 Core、Engine、Audio、Rendering 等模块，Engine 同时通过依赖组合多个功能；表明 Engine 名称不等于一个无依赖的纯执行器，也不代表其粒度应直接照搬。
- Avalonia 将 Desktop 入口与 Fluent/Simple 主题作为独立分发选择。GalNet 首期把默认页面/主题聚合在 Avalonia 包内；出现独立主题消费需求后再拆。
- 本方案从 GalNet 真实用户路径出发：写契约、自备宿主、复用默认后端、使用默认 Avalonia 玩家。由此保留 4 个运行时包和 1 个生成器包；无需独立发布 Editor SDK。

`Sample.Contracts`、`Editor.DefaultContracts`、Sample/Headless/Editor/CLI 应用、测试与 fixture 显式不打 NuGet。未来 CLI 可以独立作为 dotnet tool；不计入本次包数。

### 包的关键要求

1. 普通 NuGet 包发布 IL，R2R / AOT 属于宿主发布模式，不建立 `.R2R` / `.Aot` 包族。
2. 通过显式白名单描述 5 个 PackageId、各自拥有的程序集、第三方依赖与验证入口，禁止对整个 solution 盲目 pack。每个 GalNet DLL 只归属一个包；上层包只声明依赖，不能再复制下层包的 DLL。
3. SchemaGenerator 放在 `analyzers/dotnet/cs`，编译器依赖不作为玩家运行时依赖。外部项目显式 PackageReference 并设 PrivateAssets=all；验证从包消费时真实生成代码。不能默认 ProjectReference 的 analyzer 语义会自动转成正确 NuGet 资产。
4. GalNet.Desktop 随包携带 Media.Miniaudio 的预构建 `runtimes/win-x64/native/galnet_miniaudio.dll`，由 NuGet 选择资产；消费方不应安装 MSVC 或编译仓库 native 源码。首期该原生资产只支持 win-x64，使用默认 Miniaudio 后端的其他 RID 明确报不支持。未来增加 RID 资产，而非新增包名。
5. Media.LibVlc 是托管适配；桌面宿主按平台引用 LibVLC 原生分发依赖，保留当前宿主组合边界。
6. 同族包依赖约束首期建议精确版本 `[V]`，保持快速演进阶段的契约/生成器一致；第三方依赖沿用集中版本。打包检查实际 nuspec，不仅检查 csproj。
7. 包元数据包含许可证、README、仓库地址、commit、XML 文档；符号与 SourceLink 按程序集身份约束配置，不能覆盖现有契约闭包的稳定元数据规则。

### 聚合包实现方式

增加专用打包定义（例如 `packaging/` 下的 packaging csproj 或 nuspec），输入统一发布构建的文件清单。普通 DLL/XML 放 `lib/net10.0`，生成器放 analyzer 目录，原生文件放 runtimes；不对所有 bin 文件做递归通配复制。

显式将内部 ProjectReference 映射为“同包 DLL”或上述 5 个包之一，不能让 pack 自动生成不存在的 GalNet.Core / GalNet.Runtime 等包依赖。聚合同包各项目的第三方 PackageReference，保留版本和 compile/runtime/build/analyzer 资产语义，排除编译器私有依赖。不要仅 SuppressDependenciesWhenPacking 后遗漏第三方依赖，也不要把第三方 DLL 全塞进包以绕开 NuGet。

包消费验收必须离开源码工程：检查 nuspec 仅引用真实存在的聚合包与第三方依赖；验证 Contracts+生成器不带入 Engine、Engine 不带入默认后端/图形依赖，以及 Desktop、Avalonia 的完整依赖链；验证同时引用上下层包无重复程序集/版本冲突；验证生成器、资源与 native 资产真实进入所需构建/发布输出。

## Windows Release 产物

| 文件名模板 | 用途 | 发布条件 |
| --- | --- | --- |
| GalNet-Editor-V-win-x64-r2r.zip | Editor，自包含 | 默认 |
| GalNet-Sample-V-win-x64-r2r.zip | 纯玩家，接收内容目录 | 默认 |
| GalNet-Sample-GameTestCase-V-win-x64-r2r.zip | 玩家 + 已编译展开的 GameTestCase | 默认 |
| GalNet-Sample-V-win-x64-aot.zip | 固定 Sample 契约的纯玩家 | 桌面 AOT 完整验收后 |
| GalNet-Sample-GameTestCase-V-win-x64-aot.zip | 同一 AOT 玩家 + GameTestCase | 桌面 AOT 完整验收后 |
| GalNet-Sample-Contracts-V.zip | Editor 使用的 Sample 契约 IL 依赖闭包 | 默认 |

最终为 6 个 ZIP；AOT 验收前为 4 个 ZIP。Contracts 是 IL 资产，不加 RID 或 r2r/aot 后缀；仅在跨模式身份一致验收通过后，由两种玩家共享。若实际闭包无法统一，必须显式区分契约包及指纹，不能错误标为通用。

每个 ZIP 包含 README、许可证/第三方声明、release-manifest.json；顶层生成统一 SHA256SUMS 与 release-index.json。默认玩家关闭 EnableGameDebugging，不携带 PDB；运行日志保留。原生 DLL 仍可为外部依赖，不把 AOT 等同于单文件。

Editor 附带或内嵌独立构建的 Headless CLI 与项目创作 skill，具体由运行入口决定；CLI 依赖闭包须完整，不能只复制单个 exe。打包后在无仓库环境执行内容检查/导出验收，避免发行版调用源码路径。Editor 的默认契约与 Sample 契约使用不同目录。

纯 Sample ZIP 不含 GameTestCase；整合包复用该模式的相同玩家文件，额外加入编译后的内容与启动脚本。内容只编译一次，再进入两个模式的整合包，前提是契约指纹一致。不分发中间 galpak 代替最终可运行目录。

## AOT 与契约身份

Editor 仍走 CoreCLR + R2R，继续动态加载项目契约。Sample AOT 在组合根静态引用项目契约、指令与 JSON 元数据；纯 Sample 的“纯”表示无游戏内容，不表示支持随意加载其他项目 DLL。替换剧情/资源须遵守同一契约，替换自定义 C# 扩展需重新编译玩家。R2R Sample 也应明确现有静态 Sample 契约边界，不能默认其是任意插件宿主。

契约身份以未变换的 IL 为源：

1. 在发布专用、固定配置的构建图生成 Core、Contracts.Builtins、Project.Contracts 的 IL，并计算每文件哈希，连同第三方必要依赖形成契约清单。
2. Contracts ZIP、Editor 内容编译、R2R 玩家与 AOT 编译输入均来源于该构建产物。必须证明实际消费相同 IL；重复构建时进行字节比对，而非仅比较版本号。
3. R2R 继续排除契约身份闭包的转换。AOT 将类型链接入可执行文件，同时嵌入源 IL 的契约清单，在需要身份验证时使用这份清单；不能依赖 AOT 的 Assembly.Location 或散落 DLL 代表已链接的代码。
4. 给 Editor 使用的 DLL 只承担作者 schema 加载；它的存在不能给 AOT 增加动态扩展能力。玩家内容入口按契约清单校验，不关闭原有身份验证解决构建不一致。
5. 所有模式统一受控的 Version/AssemblyVersion/FileVersion/InformationalVersion 策略：PackageVersion 与发布版本一致；AssemblyVersion 建议按 major.minor 固定，FileVersion 为 major.minor.patch.0；契约闭包禁用 commit 自动注入，commit 留在 manifest。升级 SDK、生成器、依赖或编译选项都可能改变字节身份。

桌面 AOT 作为独立可交付阶段，覆盖 Avalonia 编译绑定、Ursa/Semi/DynamicLocalization、JSON/DI、Skia/shader、LibVLC 与原生音频。最终链接产生的 ILLink/ILC 警告逐项处理；不通过全局压制警告或关闭功能得到名义上的成功。明确请求 AOT 时失败必须返回非零，不能静默降级 R2R。

## 本地脚本接口与流水线

保留 `Publish.ps1` 及现有 Editor 调用兼容，在其上增加总入口 `Release.ps1`，把“构建文件”和“发布到服务”分开。以下命令为拟议接口：

```powershell
# 本地第一阶段：真实构建、pack、ZIP 和验收，不上传
pwsh -File scripts/release/Release.ps1 -Version 0.1.0-dev.1 -Target All -SampleMode R2R

# 桌面 AOT 验收完成后的完整本地候选
pwsh -File scripts/release/Release.ps1 -Version 0.1.0-preview.1 -Target All -SampleMode Both

# 正式 tag：必须精确指向 HEAD 且工作区干净
pwsh -File scripts/release/Release.ps1 -Tag v0.1.0 -Target All -SampleMode Both

# 只上传已经完成验证的文件，不重建
pwsh -File scripts/release/Push.ps1 -Index out/release/0.1.0/release-index.json -Destination NuGet,GitHub
```

阶段：ResolveVersion → ValidateEnvironment → Restore → BuildContracts → PackNuGet → PublishHosts → CompileContent → Assemble → Verify → SealIndex。Push 只消费封存索引，校验哈希、commit 和验证结果后上传。NuGet 和主机输出分开，禁止通配上传整个版本目录。

`Target` 支持 NuGet / Apps / Contracts / All；`SampleMode` 支持 R2R / Aot / Both。Editor 始终只构建 R2R，Contracts 只生成一份。构建计划写入索引，验证期望文件清单，不能继续使用“至少六个文件”判断产物完整。

工作目录按版本、RID、模式和唯一 runId 隔离；最终文件放 `out/release/V/nuget`、`out/release/V/assets`，日志、obj/bin/staging 位于同版本的专用工作区。索引只在全部验收通过后封存。不要混入上次运行文件，不删除目录外文件，不并发写同一套 obj/bin。

RID/R2R/AOT/无调试开关影响 restore 图：每种图固定 SDK 和 locked restore，采用受版本控制的独立锁文件；更新锁是显式维护操作。编译期 netstandard 生成器剥离宿主 RID/AOT 属性。原生编译检查 MSVC 完整工具链；普通 NuGet 消费不触发这些工具。

## 版本、tag 与 CI

- 正式版 `v1.2.3`，预览版 `v1.3.0-preview.1`。版本 tag 标记一个已经提交的 commit，tag 不代替 commit。
- Tag 模式解析精确 tag，验证其指向 HEAD；禁止取最近祖先 tag。正式发布要求干净工作区。
- 本地无 tag 使用显式版本，或 `0.0.0-dev.<提交数>+<短SHA>`；dirty 状态写入清单。本地同一版本的多轮候选使用独立缓存，NuGet 消费验收采用独立 NUGET_PACKAGES，避免读到旧缓存。
- tag 版本统一写入 NuGet、应用元数据、文件名和 manifest。PackageVersion 不带仅用于本地溯源的 build metadata；commit 始终在索引独立记录。
- CI checkout 精确 tag、恢复锁定 SDK/原生工具链，调用 Release.ps1，再上传不可变流水线产物。第一次接入可提供 workflow_dispatch 仅构建验收，不发布。
- 发布任务下载这些文件并调用 Push.ps1；不另写 CI 专用打包逻辑，也不重新编译。Release 先创建 draft，上传全部资产、推送全部 NuGet 并核实可见性后，最终发布 draft；preview 标记 prerelease。
- NuGet 与 GitHub 不存在跨服务事务。NuGet 按依赖顺序逐包上传，记录每包状态；中断后从原索引恢复，已有版本须核实同一产物身份。不能用 skip-duplicate 掩盖版本冲突，也不覆盖已公开版本；必要时新发补丁版。
- 同一 tag 的发布加并发互斥。NuGet.org 凭据建议用 Trusted Publishing；本地第一阶段不需要账号凭据。

## 验收与推进

1. **本地 R2R + Contracts + NuGet**：产出 4 ZIP 和 5 个聚合 NuGet 包；从空目录与隔离包缓存创建外部消费工程，仅用本地 feed 引用，检查依赖、源生成与 native 资产，验证 R2R publish 和真实音频。解压 Editor 后打开 GameTestCase，纯 Sample 指定内容启动，整合 Sample 双击启动；验证编译后的内容可加载、契约哈希一致、无源码路径依赖。
2. **桌面 Sample AOT**：先独立发布实际 Sample，修复/验证完整桌面闭包；验证页面、语言、场景/图层/shader、声音、视频、选项、存读档和退出。补纯 AOT/整合 AOT 两个 ZIP，验证 RuntimeFeature.IsDynamicCodeSupported=false、源 IL 指纹一致、同一内容在两种模式可运行。AOT 能力未完成前只发布明确的 R2R 产物集。
3. **CI 仅构建**：在干净 Windows runner 上调用相同入口，验证 SDK、锁文件、原生构建、内容与目录完备。原生 UI/音视频完整本地验收与 CI 可自动执行的 smoke 分别记录。
4. **公开发布**：接 NuGet 认证与 GitHub 发布，测试 preview tag、失败重试、部分包上传恢复、正式 tag；成功后才更新正式发布手册。

## 取舍与风险

- 5 个聚合包减少分发与版本管理成本，保留源码模块划分与契约/引擎/后端/UI 的消费边界；代价是包级可选粒度更粗，且打包层需维护程序集所有权和第三方依赖映射。通过白名单与外部消费验收控制风险，不通过额外发布二十多个小包解决。
- 统一版本简化契约兼容和用户升级，代价是小改动也发布整族；待公共 API 稳定后再考虑独立版本。
- 桌面 AOT 是实际工程工作，不能从 Headless 自检推断。若依赖阻塞，记录具体阻塞并保留 R2R 交付，不给 Editor 增加 AOT 工作。
- GitHub 自动源码 ZIP 不包含生成后的可运行 Sample；可执行包与 Contracts 必须作为显式 release assets。
- GameTestCase 和字体/音视频等第三方素材发布前核实再分发许可，应用包保留相应声明。

## 依据

仓库现状：`docs/manual/release-packaging.md`、`docs/manual/native-aot.md`、`docs/manual/cross-platform-backends.md`、`Directory.Build.targets`、`scripts/release/Publish.ps1`、`scripts/contracts/export-sample-contracts.ps1`、`.github/workflows/release.yml`。

外部约束：[NativeAOT 限制](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)、[NuGet analyzer 约定](https://learn.microsoft.com/en-us/nuget/guides/analyzers-conventions)、[NuGet 原生资产](https://learn.microsoft.com/en-us/nuget/create-packages/native-files-in-net-packages)、[NuGet 多程序集选择](https://learn.microsoft.com/en-us/nuget/create-packages/select-assemblies-referenced-by-projects)、[NuGet pack 规则](https://learn.microsoft.com/en-us/nuget/reference/msbuild-targets)、[NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)。

同类分发参考：[MonoGame 平台与包](https://docs.monogame.net/articles/getting_started/platforms.html)、[Stride.Engine 包依赖](https://www.nuget.org/packages/Stride.Engine)、[Avalonia 官方仓库](https://github.com/AvaloniaUI/Avalonia)、[Avalonia 主题包](https://docs.avaloniaui.net/docs/styling/themes)。
