# 仓库物理目录与开发者文档整理总结

## 实现结果

- 将 21 个生产项目归入 `src/Shared`、`src/Presentation`、`src/Infrastructure`、`src/Editor`、`src/Samples` 和 `src/Launcher`，物理目录与 solution 分组一致。
- 扁平化 Launcher 的重复容器层，保留项目、程序集、命名空间和公共 API 名称不变。
- 更新主 solution、Launcher solution、全部 `ProjectReference`、CI、sample 脚本和 GameTestCase 命令到新路径。
- 删除没有 Git 追踪文件的旧 `src/GalNet.Storage.Abstractions` 本地目录及其历史构建产物。
- 为根 README 增加环境要求、仓库地图、验证命令、sample 入口、平台限制和 feature 工作流；在架构 spec 中记录稳定的物理分组规则。

## 分阶段提交

- `cee4b5f docs: plan repository physical layout`
- `d37f63d refactor: align shared project directories`
- `05a613f refactor: align host project directories`
- Phase 3 文档与收尾在本 feature 的最终提交中完成。

## 关键决定

- 物理目录只表达项目职责和查找入口，不改变依赖图、程序集名、根命名空间或运行时行为。
- 历史 feature/design 保留当时事实；只更新仍作为开发入口的代码、脚本和正式文档。
- 全仓测试继续顺序执行，避免多个 MSBuild 进程争用共享生产项目的 `obj`。

## 验证证据

- 所有 `ProjectReference` 静态解析成功；主 solution 列出 31 个项目，Launcher solution 列出 5 个项目。
- `dotnet restore GalNet.slnx --locked-mode` 成功。
- 10 个测试项目顺序执行，329/329 通过：Architecture 14、Core 55、Runtime 32、Builtins 40、Assets 32、Storage 12、Editor.Shared 16、Editor 28、Presentation 69、Integration 31。
- 完整 `GalNet.slnx` Release build 成功，覆盖 Desktop、Browser、Android 和 iOS Launcher，结果为 70 个既有告警、0 个错误。
- Headless sample 完成内容编译、对话、动画、选择、cross-fade、图层和正常退出的交互冒烟；Avalonia sample 脚本通过语法解析且对应项目完成 Release build。
- 活动文件无旧项目路径，旧宿主目录没有被 restore 重建，`git diff --check` 通过。

## 偏差与限制

- 标准完整构建首次因 Avalonia BuildServices 无法写用户级 `buildtasks.log` 失败；设置 `AVALONIA_TELEMETRY_OPTOUT=1`、禁用共享编译并使用单 MSBuild worker 后成功。这是已有的受限环境约束，不是源码或目录迁移失败。
- 构建仍报告既有代码分析、Avalonia XAML 和 Browser native-reference 告警；本 feature 不包含无关告警清理。
- Avalonia sample 未在自动化中长期保持 GUI 窗口，只验证脚本语法、项目构建和同一内容管线的 Headless 交互。

## 后续工作

维护性路线图下一步是 Phase 7：独立审核依赖图、公共 API、测试隔离、文档和迁移残留，并完成路线图总总结。

## 文档同步

- [根 README](../../README.md)
- [架构 spec](../../docs/spec/architecture.md)
- [维护性路线图](../F-20260929-03-maintainability-roadmap/phase-plan.md)
