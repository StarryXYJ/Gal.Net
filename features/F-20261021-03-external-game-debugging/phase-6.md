# P6：跨进程验收、回归与收尾

状态：verified-with-follow-up。核心用户范围已交付；以下既有失败、优化延期及未人工检查项不宣称完成。

## 实际跨进程与 UI

- 显式测试启动独立 Avalonia Sample 原生进程，标题页握手、可选开始、Player 修改、定位 false 目标、单步、停止、重开、旧 runId 拒绝及正常关闭均通过，退出码 0；停止后重开/最终文件读取仍为手改的 Player 值。
- 邀请独立校验项目、构建内容和可选契约 DLL SHA256。Editor 使用已加载的项目契约指纹；Sample 从实际加载的 ProjectSchemas 程序集计算，不能把命令行预期值直接当实际身份。解析测试确认错误契约拒绝、正确契约接受。
- 挂载实际调试 Dock、GraphNodeControl 和 EntryListEditorControl，通过真实 loopback TCP 校验两作用域变量、草稿冲突/重置、位置/断点、单步及历史不变。进一步断言实际 BorderThickness，避免只验证 ViewModel 标记。

## 验收中修正

- 停止/取消绕过普通命令队列，停止不会排在不可跳过的长追帧后；断线取消旧请求，默认 Engine 适配清断点并解除暂停。运行操作代次读取改为原子操作。
- 普通读档与调试恢复通过同一执行门中的显式身份/代次检查区分，不再使用门外临时恢复标记；普通读档原子移除旧重放策略和缓存，旧 finalizer 不能覆盖新场景。
- 前跳的当前场景基线在旧活动实例提交 CatchUp 终态并清理后捕获，避免恢复动画中途状态；收束当前未读对白同样抑制已读写入。
- Runtime 本地变量归一化前缀，桥接回写按 UID 保留原存储键；默认 Avalonia 运行停止/释放时刷新玩家变量，避免面板当前值正确但重开/落盘仍旧值。
- Sample Closing 在 DI scope 释放前幂等解绑窗口，Closed 不再从已释放容器解析服务。绑定回调失败也通过完整 adapter 清理订阅与运行。
- 超出 4 MiB 的响应返回有界错误并保留连接，避免请求一直等待；能力不支持的暂停/继续/单步按实际 capability 禁用。
- Entry 高亮移除会压过 class style 的本地 Border 值；断点标记移到编号旁，保留原拖拽手柄结构。损坏的本地启动配置显示错误而不阻止面板/项目设置构造；项目设置修改后启动可重新加载配置。

## 验证证据

命令均在主工作区依次执行，不并发构建共享 obj：

| 验证 | 结果 |
| --- | --- |
| Runtime 全量 | 112 passed；最后普通读档策略清理修订再跑 DebugSession 16 passed |
| Debugging 全量 | 10 passed，含身份不符、超大响应、长重放停止/断线 |
| Storage.FileSystem 全量 | 22 passed，含前缀/UID/实际重新加载 |
| Editor.Shared 全量 | 384 passed；路线定向 3 passed |
| Editor 全量初轮 | 252 passed / 5 failed；两项拖拽结构回归已修复，Headless Dispose 偶发项重跑通过 |
| Editor 最终回归 | 排除已独立复现的两项既有布局用例后 255 passed；实际调试及拖拽等六项定向 4 passed / 2 基线 failures |
| Integration 全量 | 111 passed / 1 既有资源 fake failure；排除该项后 111 passed |
| 原生跨进程显式测试 | 1 passed；默认全量不会自动打开原生窗口 |
| Architecture | 47 passed |
| GalNet.slnx build --no-restore | passed，0 errors；现有分析器/Avalonia warnings 保留 |
| git diff --check | passed |

跨进程命令：`dotnet test test/GalNet.IntegrationTests/GalNet.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ExternalSampleConnectsAtTitleStartsEditsSeeksStopsAndRejectsOldRuns -v minimal`。

## 未混入本 feature 的既有失败

1. `GroupToolbarBatchesAreSingleHistoryStepsAndSpaceInsertsOneAtCapturedAnchor`：滚动尾部间距为 0，断言要求至少 40。
2. `GroupTransformAndColorUseRealControlsSingleHistoryAndLosslessSave`：窄布局行宽为 482，断言要求小于 480。

两项在 UI 接入前提交 `40ae34d` 的独立 `git archive` 快照中重新还原、编译并运行，失败值完全相同；对照目录/ZIP 已安全删除。没有修改原断言或跳过它们来伪报全量通过。最终过滤命令：`--filter 'FullyQualifiedName!~GroupToolbarBatches&FullyQualifiedName!~GroupTransformAndColor'`。

3. `DefaultPresentationUsesAndReleasesInjectedAdapters`：既有 Assets fake 的 GetFilesAsync 抛 NotSupportedException，被图像变体 catalog loader 调用。该 fake 和 AvaloniaGamePresentation 相对 `40ae34d` 无 diff，异常发生在 Engine/调试绑定创建之前；本次保留，没有修改其他 feature 的适配。过滤命令：`--filter 'FullyQualifiedName!~DefaultPresentationUsesAndReleasesInjectedAdapters'`。

## 范围修订和后续

- 首版采用 150ms 全量状态轮询；没有交付增量事件流。运行身份/执行代次/变量原值仍严格校验。
- 按用户最终确认仅缓存最近自然入口；热点前缀和固定入口预计算缺少完整状态证明而延期，已提供 payload/条数预算和测量，不在项目打开时枚举路线或预跑插件。
- 原生验收覆盖默认宿主的实际对白/恢复链；没有逐一人工验收音视频/特效所有组合、文件选择器和多路线弹窗的视觉效果。暂停是流程暂停，媒体时钟未完整冻结。
- 没有专用重复环次数编辑器；普通多选与候选不遍历重复节点，运行协议可传入有界显式重复边。
- 正式操作与接入以 [外部游戏调试手册](../../docs/manual/external-game-debugging.md) 为准。
