# 仓库物理布局设计

## 目标布局

```text
src/
  Shared/          Core、Runtime、Assets、Presentation.Abstractions、Primitives.Builtins
  Presentation/    Avalonia.Controls、Avalonia.Rendering、Avalonia.GameView、Presentation.Defaults
  Infrastructure/  Storage.FileSystem
  Editor/          Editor.Abstraction、Editor.Shared、Editor、Editor.Headless
  Samples/         Sample.Avalonia、Sample.Headless
  Launcher/        Launcher 共享项目与 Android/Browser/Desktop/iOS 宿主
```

项目目录仍以完整程序集名命名。Launcher 当前的 `src/Launcher/GalNet.Launcher/<projects>` 会扁平化为 `src/Launcher/<projects>`，其局部 `GalNet.Launcher.slnx` 移到 `src/Launcher/GalNet.Launcher.slnx`。

## 移动策略

第一批先移动 Shared、Presentation 和 Infrastructure。它们被多数外层项目引用，因此同一批更新全部生产/测试 `ProjectReference` 与主 solution，随后运行架构、Core、Runtime、Assets、Storage 和 Presentation 测试。

第二批移动 Editor、Samples 和 Launcher，更新其相互引用、CI、sample 脚本、GameTestCase 命令和 Launcher 局部 solution。该批验证 Editor/Shared/Integration 测试、两个 sample 脚本及宿主构建。

最后同步 README、架构 spec、路线图和 feature 总结，清理仅包含忽略产物的旧 Storage.Abstractions 目录，并执行全量测试与完整 solution 构建。

## 取舍

- 使用物理分组目录而不是继续只依赖 solution 虚拟分组，使文件浏览、脚本路径和所有 IDE 表达同一结构。
- 不引入 `Directory.Build.props` 路径别名或 MSBuild 变量；相对 `ProjectReference` 保持直观，可由架构测试解析。
- 不移动 `test`，因为测试项目已经按生产边界平铺且 solution 只有一个 `/test/` 分组。
- 历史 feature 文档保留当时执行命令；活动 README、CI、脚本、spec 和仍作为入口的链接必须使用新路径。

## 风险与验证

- 路径层级变化可能遗漏项目引用：使用 `dotnet build` 和架构测试验证，并搜索旧 `src/GalNet.*` 路径。
- Launcher 包含平台 workload 和局部 solution：在完整 solution 外单独验证局部 solution 路径与 Desktop 项目。
- 旧目录可能只剩 `bin/obj`：删除前确认没有 tracked 或非构建文件，删除范围限制在明确的旧目录。
- Git rename 识别不影响正确性，但每批提交保持纯移动与路径修复，便于审阅。
