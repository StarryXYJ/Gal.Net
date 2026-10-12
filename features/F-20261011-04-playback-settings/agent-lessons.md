# 暂存 agent lessons

## 工具栏模板仍可能被 Fluent 状态样式覆盖

状态：confirmed，2026-10-12。自定义 Button 模板若沿用 PART_ContentPresenter 名称，Fluent 的 pointerover/pressed 模板子选择器仍会直接覆写该 presenter.Background，导致 Button.Background 正确而实际呈现错误。游戏工具栏使用自己的 ToolbarContentPresenter 名称和 TemplateBinding，避开默认模板子选择器，保留命令、内容及焦点行为。九组布局×配色测试先复现 presenter 与按钮画刷不一致，调整后通过。验证自定义按钮时需检查实际模板背景，不能仅断言外层 Background。来源：F-20261011-04-playback-settings。

## 玩家点击不等于消费推进边界

状态：confirmed，2026-10-12。来源：F-20261011-04-playback-settings；PlaybackModeTests、GameEnginePrimitiveDispatchTests、BuiltinAutoPlaybackTests。

现象：视频分段 seek 后开启快速，视频完成并移除，但 Engine 不启动下一句计时，左键才恢复；手动文本 reveal 和点击不可跳过 blocker 后开启 Auto/Quick 同样受影响。根因为 AdvanceAsync 在调用 Skip 之前清空 _advanceBoundary，而 Skip 不保证逻辑完成。修正为只有确实释放 blocker、进入后续剧情时才消费边界，不通过重新枚举已移除实例来推断是否曾达到过边界。

同类路径：进入追帧时提前清空边界，现有自定义 blocker 回退正常执行且没有完成；取消追帧再切 Auto/Quick 后无法续跑。压缩和清理之后按剩余 blocker 恢复边界。恢复/停止/Choice 等真正的生命周期边界仍明确清空，不能全局取消清理。

验证：手动路径四例、视频六例（有手动点击的四例先失败）、取消追帧两例先复现；修复后加上真实 ADV/NVL 四例，共新增 16 项测试通过。禁止用“只从首次 Dispatch 开启模式”的测试代替“中途手动操作后再开启模式”的验证。`.agents` 当前只读，本条暂存 feature，待可写时整理索引。

## 即时文字速度不能代替全文快速完成

状态：confirmed，2026-10-12。TypewriterTextBlock 的 CharactersPerSecond=0 仅省略逐字符延迟，显式 `\\d{...}` 仍会等待；最终追帧重建也会受影响。Avalonia 即时 ADV/NVL 路径在 Start/Append 后显式 RevealAll，跨越 delay 与 skip 分段。三布局×三配色用 60 秒 delay 验证最终同步在 2 秒内完成，Presentation 125 项通过。适用快速全文和最终展示重建；禁止仅设速度 0 就承诺忽略所有等待。来源：F-20261011-04-playback-settings。

`.agents` 在当前会话是只读目录，以下已确认经验暂存在 feature 内，待可写时整理到 agent lessons/索引。

## 样例编译通过不等于作者文档校验通过

状态：confirmed，2026-10-12。

发现：GameTestCase 能编译且双路线运行测试通过，但 Headless validate 报跨 group 的 `line` 重复。EditorDocumentValidator 和 AddEntry 把 GroupEntries 展平后要求全局唯一；编译器逐 group 校验，已读使用 (groupId, entryId)，命令查找也以 group 定位。正确作用域为 group 内唯一，不应改名样例绕过错误约束。

修正：校验和 AddEntry 按目标 group 检查；新增跨 group 同名合法、同 group 重复拒绝及样例作者文档校验测试。Shared 测试 18 项、样例集成测试 6 项通过，CLI validate 无诊断，4 个 group 重编译成功。

另：本地启动日志确认旧 progress 无版本字段导致 version 0 被拒绝。按用户要求不迁移；本轮显式修复本地样例时将旧文件移到 progress.legacy-20261012.json.bak，保留设置和存档。运行时不增加自动备份/清空兼容逻辑。

适用：样例验收应包含作者文档校验、编译及运行，不能只用 compiler smoke 断言所有工具约束一致。来源：F-20261011-04-playback-settings 样例修复。

## 持久展示重建必须使用 Engine run scope

状态：confirmed，2026-10-12。

发现：Sample Prepare 原先传 Engine.Runtime 和启动/读档调用者 token 给 PersistentSceneRestorer。恢复出来的循环动画/视频并非 Engine dispatch 实例，不能假定 Engine 清活动集合会停止它们；原始 Runtime 也没有该代场景隔离。

修正：Engine.SynchronizePresentationAsync 在同一执行门下传 scoped runtime 和 run token。调用者 token 控制进入操作，持久播放保留 run token，不能通过返回后立即 Dispose 的临时 linked CTS 承诺后续停止通知。

证据：PresentationSynchronizationUsesAnIsolatedRunScopeThatRestoreAndStopCancel 验证旧场景引用隔离、读档/停止取消以及旧回调变量写入拒绝；Runtime 70 项和 Integration 67 项通过。

适用：读档后的动画/视频重建、追帧最终呈现重建。禁止把原始 Runtime 和临时 UI 操作 token 当作持久呈现 scope；仍需跟踪非 Engine 所有的恢复播放及晚清理，scope 修正不等于追帧协调已经完成。

来源：F-20261011-04-playback-settings；关联 `.agents/lessons/L-20261010-02-restore-run-scope.md`、`docs/spec/runtime.md`。
