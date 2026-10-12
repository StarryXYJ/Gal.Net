# Sample 项目契约

这个项目只依赖 Core 和内置契约，游戏通过 ProjectReference 引用它；工具独立加载构建产物。入口为 `GalNet.Project.Contracts.ProjectSchemas.Modules`，类型为 `public static IReadOnlyList<IEntryModule> { get; }`。程序集名称必须是 `GalNet.Project.Contracts`。

从仓库根目录部署到内容项目：

```powershell
./scripts/build-project-contracts.ps1 -ContractsProject ./src/Samples/GalNet.Sample.Contracts/GalNet.Sample.Contracts.csproj -ProjectDirectory <内容项目目录>
```

脚本先完成独立构建，再替换内容项目的 `Contracts` 目录及依赖，发布失败则恢复旧目录。游戏发布仍走正常程序集依赖；不要把工具契约目录当作资源或额外打包进 galpak。

自定义项目可以将 `Modules` 初始化为 `Array.AsReadOnly<IEntryModule>([BuiltinSchemas.CreateDialogueModule(), new MyModule()])`，或展开 `BuiltinSchemas.CreateAllModules()` 后加入自定义模块。列表应稳定且只包含声明支持的能力，不持有游戏会话或平台视图。重复模块 ID 和 entry typeId 都会被工具拒绝。

工具的 `ProjectSchemaContext.Load(projectDirectory)` 加载该项目自己的依赖，共享 Core 与内置契约程序集身份，并严格检查程序集版本。契约入口/依赖缺失或不兼容时明确报错，不回退全量内置。契约为可执行 C# 代码，应部署自己项目的可信构建产物。加载器不是代码沙箱。

上下文 `Dispose()` 后不可再取得 catalog；持有自定义参数 Type、validator 或 Composite delegate 的消费者也必须结束项目生命周期。流式加载不锁项目 DLL；关闭后重新打开才能使用新构建，不支持热重载。
