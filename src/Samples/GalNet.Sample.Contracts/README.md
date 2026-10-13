# Sample 项目契约

这个项目只依赖 Core 和内置契约，游戏通过 ProjectReference 引用它；工具独立加载构建产物。入口为 `GalNet.Project.Contracts.ProjectSchemas.Modules`，类型为 `public static IReadOnlyList<IEntryModule> { get; }`。程序集名称必须是 `GalNet.Project.Contracts`。

从仓库根目录部署到内容项目：

```powershell
./scripts/build-project-contracts.ps1 -ContractsProject ./src/Samples/GalNet.Sample.Contracts/GalNet.Sample.Contracts.csproj -ProjectDirectory <内容项目目录>
```

脚本先完成独立构建，再替换内容项目的 `Contracts` 目录及依赖，发布失败则恢复旧目录。游戏发布仍走正常程序集依赖；不要把工具契约目录当作资源或额外打包进 galpak。

自定义项目可以将 `Modules` 初始化为 `Array.AsReadOnly<IEntryModule>([BuiltinSchemas.CreateDialogueModule(), new MyModule()])`，或展开 `BuiltinSchemas.CreateAllModules()` 后加入自定义模块。列表应稳定且只包含声明支持的能力，不持有游戏会话或平台视图。模块 ID 可重复，只有重复的完整 entry typeId 会被拒绝。

工具的 `ProjectSchemaContext.Load(projectDirectory)` 加载该项目自己的依赖，共享 Core 与内置契约程序集身份，并严格检查程序集版本。契约入口/依赖缺失或不兼容时明确报错，不回退全量内置。契约为可执行 C# 代码，应部署自己项目的可信构建产物。加载器不是代码沙箱。

上下文 `Dispose()` 后不可再取得 catalog；持有自定义参数 Type、validator 或 Composite delegate 的消费者也必须结束项目生命周期。流式加载不锁项目 DLL；关闭后重新打开才能使用新构建，不支持热重载。

## 单平台截图 entry

Sample 独立模块声明 `sample.screenshot(fileName?)`，不属于 BuiltinSchemas.CreateAllModules。契约只引用 Core；Sample.Avalonia 的 SampleScreenshotModule 直接借用 GamePage，UI 线程捕获当前场景，并异步写 PNG。宿主目录是 Pictures/<Graph 显示名>，entry 无权指定目录。参数省略时生成带时间及 GUID 的 PNG 名；指定时必须是单个安全的 .png 文件名（最多 120 字符），不允许路径、设备名或覆盖现有文件。

正常和 Quick 都执行截图，blocking=true、skippable=false；CatchUp 只完成，不读取页面或写文件。场景必须已经应用主题且完成布局。写入使用独立临时文件，取消/失败清理临时文件；PresentationCompletion 在清理之后结束，错误经普通引擎 Faulted 状态和游戏日志暴露。模块异步释放取消并等待它创建的截图任务，再由宿主释放页面资源。

[Examples/screenshot.rawgalgroup](Examples/screenshot.rawgalgroup) 是独立 Group 示例，可复制到已有项目的 Graph/groups/ 后使用 group validate/group compile --file（孤立文件编译需要 --output）。在已有背景或角色显示后放入截图 entry 即可捕获当时场景；示例未加入双方共享的默认剧情。Headless 可以使用同一契约校验/编译，但运行含截图的内容会明确报告缺少平台执行。
