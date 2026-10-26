# 引擎核心精简与职责收敛

状态：in-progress；P1–P5 verified，P6 实现与可执行验证已完成，保留环境待验证项。

## 目标与约束

- 功能正确、低耦合、代码精简、职责清晰优先；不保留旧 API、旧数据格式或过渡适配层来维持向后兼容。
- 可恢复的小问题不应终止剧情；失败处理放在能够判断业务后果的模块边界。
- 优先删除重复表示、重复控制流程和无效分支；不为了拆分类而引入接口、服务或通用框架。
- Engine 保留唯一执行门与运行生命周期，协作者不能各自推进剧情；保留已验证的 batch、已读、播放模式和检查点语义。
- 本轮不做 benchmark、profile 或专项性能改造。仅顺手消除有明确不变量保障的重复遍历、排序、转换和分配；不宣称未测量的性能收益。
- 不为性能引入状态 revision、增量快照、缓存失效体系、并行调度或对象池；这些留给后续性能任务。
- 计划中的类型划分是方向，实施时依据实际调用关系收敛，不要求逐项创建新类型。

## 进度维护

按顺序实施；每次只将正在处理的阶段标为 `in-progress`。必要代码、调用方迁移、相关验证和文档完成后，才能勾选并标为 `verified`。

完成阶段的详细清单压缩为：**完成结果一句话 + 验证结果 + 证据链接/提交**。实际缺口不能被压缩掉；延期项标记 `skipped` 并说明原因，不计作完成。重要偏差在收尾 `summary.md` 中保留原因。

| 阶段 | 内容 | 状态 |
| --- | --- | --- |
| P0 | 核心审查与问题复现 | [x] verified |
| P1 | 收敛错误处理，修正表达式 | [x] verified |
| P2 | 修正推进、通知与恢复生命周期 | [x] verified |
| P3 | 收紧状态访问，隔离运行图 | [x] verified |
| P4 | 统一 Entry 数据，删除旧路径 | [x] verified |
| P5 | 收敛引擎协作者与模块边界 | [x] verified |
| P6 | 集成验证与文档收尾 | [ ] in-progress |

## P0 — verified

已审查 Core、Runtime、执行/呈现抽象并抽查 Builtins/GameHost，复现表达式、检查点观察者、读档位置、可变变量暴露、重复节点和推进取消问题。前次审查验证：Core 149、Runtime 114、Architecture 49 项通过；额外复现位于本地 `artifacts/engine-core-audit/Program.cs`（忽略产物，不是长期回归测试）。关键问题在对应实施阶段转为正式回归。

## 错误处理原则

严格检查集中在编译、内容装载、注册和存档恢复边界。日常执行只检查会变化或不能在入口保证的条件，不在多层重复执行相同校验。

| 情况 | 默认处理方向 |
| --- | --- |
| 重复 stop/hide、已清理的可选目标、预期取消、过期回调 | 幂等完成或忽略；通常不报错，也不反复警告。 |
| 可选音效/特效、可降级的展示后端失败 | 由领域模块定义静音、跳过或替代呈现；释放阻塞和资源，保留必要诊断。 |
| 本地化缺项 | 沿用已定义的文化/默认文本回退；最终缺失采用明确占位与诊断，不无故终止剧情。 |
| 事件观察者、日志接收方失败 | 隔离异常，不改变引擎执行状态；诊断失败不能递归造成新失败。 |
| 条件表达式解析失败 | 修复合法文本解析；非法条件返回 false 并给出定位诊断，作者校验明确报错，不静默吞错。变量赋值表达式失败拒绝该次赋值，不擅自写入默认值。 |
| 存档无效、读档位置不合法 | 拒绝本次恢复并报告原因，保留当前运行；不要把无效节点当正常结束。 |
| 保存/持久化失败 | 报告该次操作失败，保留可重试的数据；不误报保存成功，不无故中断仍可继续的剧情。 |
| 无法判定的控制流、必需指令缺实现、身份冲突、状态不变量破坏 | 入口尽早拒绝；若执行期间发生且不能保持一致性，则明确进入 Faulted。 |

“可选”由指令/模块的业务语义确定，不能依据异常类型或是否非阻塞一概跳过。已部分修改持久状态的操作，必须有一致的提交或补偿策略后才能降级。缺失 custom action 保留当前默认 warning/no-op 策略，不将所有未知指令都视为可选。诊断包含稳定 code 与可用的 Group/Entry/资源上下文，重复诊断应有界；优先复用现有通道，不建设全局错误策略框架。

## P1 — verified

修复字符串表达式、非法条件定位与限量诊断，失败/空值赋值保留原变量，音频诊断观察者不影响清理；现有可选展示降级和关键指令失败规则保留。Runtime 120、Builtins 177 项通过；实现与验证见 ExpressionEvaluatorTests、AudioRuntimeTests、BuiltinPrimitiveInstanceTests 及 [runtime 规范](../../docs/spec/runtime.md)。

## P2 — verified

检查点在锁外隔离发布，推进分批让出且可取消，Engine 改为异步读档并先准备验证候选再提交；宿主、调试和测试调用方已迁移。Runtime 125、Builtins 177、GameHost 5、Presentation 243 项通过；Integration 122 通过、1 项因 Windows 无创建符号链接特权失败（ScreenshotEntryTests:169）。完整解决方案构建另遇 Avalonia 遥测权限/WebAssembly 包哈希问题，最终构建在 P6 复核。行为证据见 GameEngineLifecycleTests 和 [runtime 规范](../../docs/spec/runtime.md)。

## P3 — verified

变量读取/集合快照隔离，运行图使用验证后的私有副本，状态锁改由 Read/Update 封装；执行模块共享的活记录保留，避免全量深拷贝。Runtime 132、Builtins 177、Core 149、Storage 22 项通过；新增外部图修改隔离、非法图入口与变量读取隔离测试，规范见 runtime/architecture。调试路线测试补齐真实的分支出口定义后通过。
## P4 — verified

Entry 统一为 JSON，作者/编译类型分离，删除 IsGeneric、字符串 Values、历史 Layer 特判和无效回退；选项统一为类型化 enum，Editor/CLI 保留输入适配与未知草稿。Core 149、Builtins 177、Generator 18 项通过，Editor 构建通过；Editor.Shared 初轮 417 通过、5 失败、2 跳过，其中选项夹具迁移后定向 5 项通过，余下为 3 项符号链接权限和 1 项文件移动拒绝访问（后者根因尚未确认，P6 重查）。规范见 entry-types。
## P5 — verified

primitive/呈现仅借用 IGameContext，Engine 管理 IGameRuntime 与 IPrimitiveDispatcher；通知统一为 EngineNotifications，删除阻塞镜像和冗余排序，Editor 设置移回 Editor.Abstraction，移除无使用者的 LauncherSettings。另补宿主替换前存档预检，避免上层提前释放旧运行。Runtime 133、Builtins 177、Presentation 243、GameHost 6、Architecture 49 项通过；Editor 构建通过，正式边界见 runtime/architecture。
## P6 — in-progress（环境待验证）

实现、正式文档、经验和自审已收尾；最终 1648 项通过、4 项创建符号链接权限失败、2 项跳过，Editor 308 项全通过。解决方案构建仅余 Browser WebAssembly SDK 包哈希不匹配，本地 locked restore 仍复现。原生桌面/显式外进程未执行。详见 [summary](summary.md) 与 [review](review.md)。

退出条件：在具备符号链接特权、正确 WebAssembly SDK 包和原生桌面条件的环境复核上述限制后，才将本阶段勾选为 verified。

## 可顺手做的优化

仅在触及相应代码时进行：活动实例顺序已经单调时删除多余 OrderBy；复用已经准备好的恢复结果；消除同一边界内无必要的重复查询/转换。保留必要的输入校验、隔离复制和检查点语义；若优化需要额外缓存或改变所有权，移出本轮。

## 参考

- [运行时规范](../../docs/spec/runtime.md)
- [架构规范](../../docs/spec/architecture.md)
- [Entry 契约](../../docs/spec/entry-types.md)
- 本计划依据 2026-10-26 会话中的核心审查及用户修订：功能/低耦合/精简优先，专项性能工作延期。
