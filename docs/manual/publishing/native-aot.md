# NativeAOT 发布与扩展契约

## AOT 当前状态

Editor 仅发布 R2R。桌面 Sample NativeAOT 尚未通过严格链接和运行验收；`-SampleMode Aot/Both` 会明确失败，不回退为 R2R。可复现试编译：

```powershell
pwsh -File scripts/release/Test-DesktopAot.ps1
```

该诊断构建隔离 RID/AOT 恢复图，不改日常锁文件，最终 ILC/ILLink 警告按错误处理。目前已发现 JSON、反射绑定、主题资源、DI 与第三方裁剪问题。Headless 的 AOT 自检仍见 [NativeAOT 手册](native-aot.md)，不能据此宣称桌面玩家已支持。

NativeAOT 是可执行宿主的发布选项，不需要独立 AOT 项目。当前复用 `GalNet.Sample.Headless` 验证引擎后端；普通构建仍使用 JIT。SDK 在 NativeAOT 发布时默认关闭反射 JSON。

## 发布与自检

安装目标平台 NativeAOT 工具链后运行：

```powershell
./scripts/test-native-aot.ps1 -RuntimeIdentifier win-x64
```

脚本用标准 `-p:PublishAot=true` 发布现有 Headless Sample，在 `.galnet/native-aot-acceptance/<rid>` 隔离恢复和编译产物，然后执行 `--aot-self-test --expect-aot`。单独发布的核心命令为：

```powershell
dotnet publish src/Samples/GalNet.Sample.Headless/GalNet.Sample.Headless.csproj -c Release -r win-x64 -p:PublishAot=true
```

仓库默认锁定依赖；RID 和 AOT 编译器会改变恢复图。日常验收优先使用脚本，它将该恢复图与普通开发的 lock 文件隔离。源码生成器是运行在编译器里的 netstandard 工具，其项目显式忽略玩家的 AOT、SelfContained 和 RID 属性。

成功输出 `AOT_ACCEPTANCE_OK dynamicCode=False reflectionJson=False`。自检覆盖 Graph/编译后 Group 加载、剧情推进、NCalc 解释求值、自定义状态恢复、内置指令和状态、生成字段 codec/递归 schema、配置、资源 metadata/未知字段、PAK acquire、文件存档、玩家变量与已读进度。脚本将最终链接的 ILLink/ILC 警告视为错误；库源码分析仍可能报告未进入玩家闭包的作者工具或显式反射回退路径。

## 自定义 DTO 的 JSON 元数据

现有 `GalNet.SchemaGenerator` 继续生成字段读写、默认值和 schema。复杂 DTO 的 JSON 元数据使用 .NET 的 `System.Text.Json` 源生成器；两个生成器职责不同。请在真实源文件中声明 context，不能期待一个生成器消费另一个生成器在同一轮产生的类型声明。

```csharp
[assembly: FieldSchemaJsonContext(typeof(ProjectJsonContext))]

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ProjectOptions))]
[JsonSerializable(typeof(ProjectSettings))]
[JsonSerializable(typeof(ProjectState[]))]
[JsonSerializable(typeof(ProjectMeta))]
internal partial class ProjectJsonContext : JsonSerializerContext;
```

根类型要包含运行时需要的 DTO 和状态数组；嵌套类型由 JSON 生成器跟踪。枚举采用实际 wire 约定，字段上的 converter 继续生效。程序集标记让字段生成器将 resolver 注入 `ValueDescriptor`，同时生成 CLR/JSON 成员身份和延迟子 schema 委托；递归类型不会在 schema 初始化时无限展开。手写描述可通过 `jsonOptions`、`WithMember` 和 `WithSchemaProvider` 显式提供相同信息。

扩展边界分别注册元数据：

```csharp
var stateOptions = JsonMetadata.CreateOptions(GameState.JsonOptions, ProjectJsonContext.Default);
var stateInfo = JsonMetadata.GetTypeInfo<ProjectState[]>(stateOptions);
states.Register<ProjectState>("project.state", item => item.Id, jsonTypeInfo: stateInfo);
// IPrimitiveModule 内也可用 registry.State(stateInfo, "project.state", item => item.Id)。

var section = new ConfigurationSection<ProjectSettings>("project.settings",
    ProjectSettings.Schema, jsonResolver: ProjectJsonContext.Default);

resources.Add<ProjectMeta>("project.data", ProjectJsonContext.Default, ".custom");
```

资源 DTO 需要公开无参构造函数；目录通过泛型工厂创建默认实例。`GameState.JsonOptions` 是冻结的存档设置，扩展应在其副本上组合 resolver，保持命名、未知字段拒绝和枚举约定一致。通用编码使用 `JsonCodec` 或接收 `JsonTypeInfo` 的框架重载；反射关闭且缺少元数据时明确报错。普通桌面 JIT 保留反射兼容入口。

## 验收边界

Sample 共享 UI 已完成一轮 AOT 准备：输入配置使用源生成 JSON 元数据，数值约束和共享表单 JSON 操作使用显式元数据，GamePage 内容绑定使用类型化 CompiledBinding；内置视觉效果按项目 shader 资源显式创建，不扫描程序集发现工厂。输入配置 wire 格式保持兼容。这些改进不等于桌面或 iOS Sample 已支持 NativeAOT，也尚无量化性能或包体收益数据。

可独立验证这部分在 JSON 反射关闭时的行为：

```powershell
dotnet test test/GalNet.Presentation.Tests/GalNet.Presentation.Tests.csproj -c Release `
  --artifacts-path out/sample-aot-tests `
  -p:JsonSerializerIsReflectionEnabledByDefault=false `
  --filter FullyQualifiedName~SampleAotPreparationTests
```

上述筛选测试不代替普通完整回归；普通测试使用默认反射设置。桌面最终链接仍通过 `scripts/release/Test-DesktopAot.ps1` 严格检查，不压制 ILLink/ILC 警告；本轮实现及具体剩余阻塞见 [Sample AOT 准备记录](../../../features/F-20261027-04-tagged-release/sample-aot-preparation-20261028.md)。iOS 还需选择对应 .NET/Avalonia 发布路线，并在 Mac/Xcode 和真机上验证。

本地已验证 Windows x64 NativeAOT 的 Headless 引擎闭包；CI 配置覆盖 Windows/Linux。测试工程本身是否支持 NativeAOT 取决于 test runner，普通 `dotnet test` 不等于运行了 NativeAOT。

自检不验证 Avalonia/Skia 图形、LibVLC 视频、原生音频设备、动态加载项目程序集或 Editor/CLI 作者编译器。NativeAOT 玩家应在组合根静态引用项目契约。其他桌面 RID、Android/iOS 打包、系统媒体和移动端实机仍需在对应环境验证，不能由桌面自检推导已经支持。
