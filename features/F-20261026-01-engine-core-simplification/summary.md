# 引擎核心精简与职责收敛

状态：实现已落地；P1–P5 verified，P6 保留环境待验证项。

## 实现结果

- 表达式双引号文本按 JSON 转义绑定为值，保留 NCalc 单引号语法；非法条件按 false 处理并限量诊断。变量赋值表达式失败/无值时保留原值，不把失败写成空字符串。
- 状态、播放模式和检查点由 EngineNotifications 按发生顺序在执行门外发布，隔离订阅者异常。音频诊断观察者不能阻断后端清理。
- 所有推进路径有批次预算和让出机制，长剧情/合法循环不因预算耗尽报错；调用方取消不使 Engine Faulted。跨批次重查 scope 与阻塞边界。
- Engine 读档使用 RestoreFromAsync，先准备 GameRestorePoint 并验证图位置、变量和模块状态，再替换旧 scope；复用已解码的候选状态。宿主替换前预检候选存档，无效存档保留当前运行。旧 scope 的取消回调异常不能留下半份恢复。
- 变量读取与集合快照隔离；运行图保存验证后的私有副本。模块共享场景记录保留，通过 GameState.Read/Update 协调，锁对象不跨程序集公开。
- Entry 只有一份 JSON 参数；AuthoringPrimitiveEntry/CompositeEntry 与 sealed PrimitiveEntry 类型分离。移除 IsGeneric、字符串 Values、CreatePrimitive<T>、Layer 历史参数特判和必然失败的序列化回退。Choices 最终只产生类型化 enum 约束，表单和生成器同步迁移。
- primitive/呈现使用 IGameContext，Engine 使用含游标与恢复能力的 IGameRuntime；分发依赖改为 Execution.IPrimitiveDispatcher。编辑器设置/最近项目/布局编码迁到 Editor.Abstraction.Settings，无使用者的 LauncherSettings 删除。
- 删除活动列表的多余排序、sequence 字段和自动阻塞镜像集合；未引入缓存、增量快照、对象池或并行调度。

## 关键边界

错误处理按业务后果确定。可选展示有已定义降级时继续；无效条件有诊断；无效存档拒绝本次操作。关键执行或状态失败仍正常传播，不统一吞异常。已部分提交的状态不靠 catch 伪装成成功。

GameState 的活记录是执行模块内部共享对象，不宣称其读取结果是不可变快照。Engine 的图副本对宿主/作者代码隔离，继续使用已有 Node/Group 数据形状，没有另建公开 CompiledGraph 类型层。

Engine 仍拥有唯一执行门；调试控制保留为内部 DebugSession，没有新增独立 Runner。通知队列与已有导航/活动实例协作者承担独立职责。同步的低层 GameRuntime.RestoreFrom 是纯内存准备/提交入口，Engine 的读档入口已异步化；现有同步释放入口仍供同步宿主使用，不为兼容新增包装。

## 验证

最终回归共 1648 通过、4 项因 Windows 创建符号链接缺少特权而失败、2 项跳过。详细结果如下；本地输出在忽略产物 `artifacts/engine-core-audit/final-*.log`。

| 测试项目 | 通过 | 失败/跳过 |
| --- | ---: | --- |
| Core | 150 | 0 |
| Runtime | 134 | 0 |
| Primitives.Builtins | 177 | 0 |
| GameHost | 6 | 0 |
| Architecture | 49 | 0 |
| SchemaGenerator | 18 | 0 |
| Storage.FileSystem | 22 | 0 |
| Presentation | 243 | 0 |
| Editor.Shared | 419 | 3 失败、2 跳过 |
| Editor | 308 | 0 |
| Integration | 122 | 1 失败 |

4 个失败均停在测试准备阶段的 Directory.CreateSymbolicLink：GroupCompileServiceTests.LinkedOutput_IsRejectedAndExternalDirectoryUnchanged、ProjectAuthoringSafetyTests.Preview_RejectsLinkedOutputBeforeWriting、ProjectSchemaContextTests.AncestorLinks_AreRejectedForRawReadsAndContractLoads、ScreenshotEntryTests.GeneratedNames_AreUnique_AndRelativeOrLinkedDirectoriesAreRejected。没有通过跳过或修改这些用例来掩盖失败，仍待支持符号链接权限的环境复核。Editor.Shared 初轮另有一次文件移动拒绝访问，最终完整重跑已通过，根因未确认。

原生桌面/真实外进程的显式测试没有执行；相关能力不能由 headless 测试替代。Editor 构建已通过；最终解决方案构建仅剩 Browser 的 NU1403：Microsoft.NET.Sdk.WebAssembly.Pack 10.0.12 包内容哈希不匹配。只使用本地包缓存的 locked restore 仍复现，仓库锁文件保持未改动；没有禁用哈希校验或改写包缓存来绕过问题。空白格式和 git diff --check 已通过。

P6 保持 in-progress，待具备目录符号链接特权、正确 WebAssembly SDK 包和原生桌面条件后补验，不把以上失败算成完整验收。

验证命令使用单进程构建、禁用共享编译器，并为 Avalonia 设置遥测 opt-out；现有平台环境限制与代码问题分别记录。未开展专项 benchmark/profile，不报告未经测量的性能收益。

## 计划调整

- P5 补充宿主替换前的候选存档预检：仅修正 Engine 读档不能防止上层提前释放旧运行。
- P6 自审补充旧 scope 取消回调异常的隔离与回归，保证合法候选不会因旧回调异常停在恢复中间。
- 不增加额外可变场景镜像/深拷贝体系；保留模块活记录，隐藏同步实现并明确使用边界。
- 调试路线测试原先引用未声明的第 7 个出口，夹具补齐实际分支项，保留“忽略条件/选择进行显式路线回放”的原测试目标。

## 当前规范

本次公开 API 有破坏性变化，仓库内调用方已同步迁移；外部项目的 Contracts DLL 需要使用新 Core/Contracts 重新构建，不保留旧二进制适配。

- [架构](../../docs/spec/architecture.md)
- [运行时](../../docs/spec/runtime.md)
- [Entry 与参数](../../docs/spec/entry-types.md)
- [术语](../../docs/glossary.md)
- [接入表现层](../../docs/manual/new-presentation-layer.md)
