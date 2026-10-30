# 异步构建入口可能先同步阻塞 UI

状态：candidate
来源 Feature：F-20261021-03-external-game-debugging

## 现象、根因与证据

Editor 调试准备直接从 UI 命令调用 `ProjectContentBuilder.BuildAsync`。该方法在首个 `await` 前执行输出路径检查、项目完整校验、资源与本地化扫描及 Raw 文件枚举；之后作者映射还同步逐组编译。即使方法返回 `Task`，这些同步 CPU/文件工作仍先占用 Avalonia UI 线程。

调用顺序和首个异步边界由源码确认，尚待大项目运行复现其界面影响。

## 正确做法

保持需要 UI 一致性的文档快照捕获在 UI 线程；把纯文件校验、内容构建和作者源码映射等可脱离 UI 的 CPU/IO 工作放到后台执行。状态栏报告当前阶段与耗时，避免把“仍在准备”呈现为无响应。

## 适用范围与禁止做法

适用于 Avalonia 命令调用 `async Task` 构建/编译 API 的场景。不能仅凭返回类型是 `Task` 就认为同步前缀不会阻塞 UI；也不要把访问可变 UI 文档对象的操作未经快照就整体搬到后台。

## 关联文档

- `features/F-20261021-03-external-game-debugging/phase-liveness-diagnostics.md`
- `docs/manual/editor/preview-and-debugging.md`
