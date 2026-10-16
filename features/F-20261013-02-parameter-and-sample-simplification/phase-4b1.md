# Phase 4b1：简单异步原语与模块任务 scope

日期：2026-10-15。状态：verified（下述完整集成限制仍保留）；属于 Phase 4b 的第一个独立验证批次。

## 范围和实现

- `AsyncPrimitiveInstance` 共用单次异步执行、稳定的 PresentationCompletion、错误/取消发布和一次完成通知。默认非阻塞、不可跳过；blocking 可构造配置，IsBlocking/IsSkippable 仍可重载。
- `OnStarting` 保留同步状态更新及同步失败路径；`ExecuteAsync` 写业务操作，`HandleExecutionFailure` 显式选择处理失败还是传播。Quick 和 CatchUp 默认沿用 PrimitiveInstance 的分发规则；具体实例可重载模式行为，不推断业务。
- `PrimitiveImplementation.Async<T>` 解码唯一 DTO，自动传递 BatchId 与执行 scope token。普通异步业务可用回调而无需实例类；需要额外状态/skip/清理的业务使用 For<T> 和自定义实例。
- Builtins 的窄范围默认实现只适用于非阻塞展示：CatchUp 执行状态更新、不调用 presenter；scope 取消不记录警告，其他失败记录警告后完成。Effect 两个和 Particle 三个实例迁移，未改变视频/动画/文本的运行规则。
- 截图继承公共异步机制，文件验证、UI dispatch、临时文件清理与原子发布仍在 Sample。Normal/Quick 保存，CatchUp 无操作，错误传播给 Engine。
- `PrimitiveTaskScope` 只在 Run 时登记运行任务，完成后观察并移除；错误单独保留，Dispose 取消，DisposeAsync 等待所有已登记操作并聚合错误。重复释放复用一个任务，停止后拒绝新操作。
- 截图模块保留“释放时记录错误”的原策略。只创建未分发的实例不会成为释放等待项，也不会创建 linked CTS；模块停止后拒绝创建新实例，已经创建的实例迟来执行仍发布取消。

## 必须保留的顺序

业务异步操作和清理完成 → PresentationCompletion 发布结果/错误 → TryComplete 发出逻辑完成通知。Engine 的 ActivePrimitiveRegistry 在完成通知推进时读取 PresentationCompletion.IsFaulted；错误必须先发布，不能先通知再填充完成任务。稳定任务避免调用者在 Dispatch 前拿到临时 Task.CompletedTask。

PresentationCompletion 表示操作/清理的结果，逻辑完成通知在它之后；需要观察通知的调用者应等待 Completed 事件，不把两个阶段视为同一个同步点。

## 验证

最终源码顺序验证：

| 测试项目 | 通过 |
| --- | ---: |
| Core | 104 |
| Builtins | 106 |
| Presentation | 181 |
| Runtime | 80 |
| Integration（明确排除下述两项） | 99 |
| Architecture | 37 |
| 不重复用例合计 | 607 |

TRX 位于 `Temp/F-20261013-02/phase4b1-final-*.trx`，集成采用 `phase4b1-final-integration-excluding-ui-flush.trx`。Core 覆盖默认三种模式、稳定任务、清理等待、失败处理/传播、同步状态失败、取消 token、通知中的错误可见性。Presentation 覆盖 typed Async 工厂默认/批次/scope/模式、100 个完成任务移除、错误移除后仍保留、并发停止、重复释放、取消回调失败仍等待其他操作。Builtin 覆盖五个实例的三种模式、状态更新、后台失败和取消。Runtime 失败 fixture 实际继承新默认实例，验证同步完成和延迟完成的 Engine Faulted。

截图定向 15/15 通过，最终集成 99 项也包括这 15 项，不重复计数；覆盖真实 PNG、Normal/Quick、CatchUp、Engine 失败、排队取消/模块等待、未分发实例释放和停止后的执行拒绝。

本批修改文件已运行 IDE0005/import 与 whitespace 整理，未格式化其他任务的脏文件。全解决方案独立输出编译通过，最后一轮 0 错误、25 警告（NETSDK1194 输出目录、既有 analyzer/Avalonia、NU1900 审计源不可达等；完整日志 `Temp/F-20261013-02/phase4b1-build.log`）。命令为 `dotnet build GalNet.slnx --no-restore --output Temp/F-20261013-02/phase4b1-solution -m:1 -p:UseSharedCompilation=false --verbosity quiet`，共享输出仅作为编译验证，不作为发布物。

### 已有工作区改动造成的验证限制

完整集成测试首次未结束，主动终止本轮测试进程；加入 hang timeout 后分别定位到：

- `SampleDisplaySettingsTests.FullscreenDisablesWindowSizeAndPreferencesRestoreWithoutWritingAgain`
- `SampleLocalizationTests.SettingsPersistAndRestoreWithoutLosingOtherPlayerVariables`

两项均在非 Headless/UI pump 测试中 await settings.FlushAsync。其他任务的未提交改动把 SamplePlayerSettingsService.FlushAsync 改为 AvaloniaUiDispatcher.InvokeAsync；不在 UI 线程时会等待 Dispatcher.Post，而这两个测试没有运行 UI pump。两份 Sequence XML 和未完成 TRX 已保留在 Temp/F-20261013-02；测试主机由 hang collector 终止，并非把产品断言失败重试成通过。仅排除这两个已定位的用例后，其余 99 项集成通过。本批不修改该任务的设置实现/测试，不能宣称完整集成回归通过。

第一轮 Core 103、Builtins 106、Presentation 181、Runtime 80 只作为过程记录，不替代最终源码证据。

## 成本与收益

生产源码含注释/空行按文件行数计，不计生成物/测试/文档，也不计工作区其他任务改动：

| 文件/机制 | 迁移前 | 迁移后 | 差值 |
| --- | ---: | ---: | ---: |
| Effect 两个实例 | 112 | 22 | -90 |
| Particle 三个实例 | 88 | 31 | -57 |
| 截图模块/实例 | 147 | 130 | -17 |
| AsyncPrimitiveInstance | 0 | 61 | +61 |
| BuiltinPresentationPrimitiveInstance | 0 | 18 | +18 |
| PrimitiveTaskScope | 0 | 93 | +93 |
| PrimitiveImplementation 异步工厂增量 | — | — | +21 |

消费端净减 164 行，公共机制净增 193 行，本批生产总量净增 29 行。该批收益是五个实例只维护业务操作、普通异步原语无需实例类、截图完成任务稳定和模块生命周期正确；不能宣称本批总体代码量已减少。任务 scope 的并发停止/错误保留成本独立存在。后续文本复用须继续验证实际净收益。

## 后续边界

Phase 4b 仍未整体完成。Session 的失败后继续清理和 Dialogue/NVL 共用推进状态机属于后续独立批次；不在本批修改已有 Dialogue 的其他任务改动。视频、动画、快照和平台资源所有权继续保留专门实现。
