# 实施阶段

用户已授权实现。每完成并验证一个 Phase 单独提交；不包含其他任务的工作树改动。验证证据必须来自实际命令/测试，不把静态调查当作通过。

## Phase 1：独立内置契约与组件组合

状态：verified

验证证据：Builtins 测试 63/63、Architecture 测试 28/28；独立 Contracts、Sample.Avalonia、Editor.Headless 构建通过。Editor 使用 `--output Temp/F-20261013-01-editor-build` 构建通过（运行中的用户 Editor 锁住常规输出；未终止用户进程），仅有既有 Avalonia 警告。内置程序集只引用 Core；13 个模块共 42 个 entry，汇总方法只返回不可变模块列表。已同步 entry-types 和 development 的验证事实。

- 目标：新建 GalNet.Contracts.Builtins，仅依赖 Core，迁移 schema/参数/纯校验/Composite，并提供组件入口与全量合集。
- 前置：已确认程序集拆分；方法/命名空间按 design.md 默认约定实施。
- 模块：Core 消费接口、内置契约新项目、Primitives.Builtins、所有直接消费者、solution、依赖门禁与 lock 文件。
- 任务：完整迁移事实源；更新执行引用；保留完整 ID 排他及模块共享状态，不混入新 CLI 行为。
- 验证：组件集合互不意外重复；All 等于预期全集；Composite 可编译且缺所需 primitive 失败；现有 Builtins/Core/架构测试；受影响执行和 Sample 构建。
- 文档：记录命名空间/依赖变化，正式事实在通过后同步。
- 风险：类型移动遗漏或残留双份定义；不靠旧 namespace alias 永久掩盖归属。
- 退出：工具契约引用图不包含 Runtime/平台执行，既有内置编译/执行测试保持通过。

## Phase 2：约定契约 DLL 与独立部署

状态：verified；依赖 Phase 1

验证证据：共享层 27/27（包括 9 项独立契约加载测试），架构 29/29；Sample.Contracts 独立构建与 Avalonia/Headless 项目引用构建通过。两个同名 DLL 的部分内置、自定义 primitive/Composite 集合独立；缺 DLL/依赖、错误入口、重复 typeId、不兼容 Core 版本均明确失败。流式加载允许在持有旧 catalog 时替换 DLL，新上下文读取新集合。部署脚本在专用临时内容项目中执行两次，确认完整依赖替换清除旧文件。Sample 当前只提供内置模块，截图契约与游戏运行注册改用入口在后续对应 Phase 接入。

- 目标：共享层可独立加载 Contracts/GalNet.Project.Contracts.dll 的固定静态入口。
- 模块：Editor.Abstraction/Shared 的加载结果协议与 ProjectSchemaContext、Sample 独立契约项目、构建/部署脚本。
- 任务：固定路径/入口集中定义；加载隔离与依赖解析；共享框架类型身份；可释放上下文；示例静态组合与独立构建。
- 验证：两个同名 DLL 不同 schema 集互不污染；部分内置+自定义 primitive/Composite；缺 DLL/入口/依赖、重复 ID/不兼容契约有可定位诊断；加载不构造执行/平台对象。
- 文档：约定目录、独立契约项目模板、部署方法、版本一致性、重新打开加载策略。
- 风险：文件锁与残留静态 delegate 持有上下文；不承诺立即 GC 或热重载。
- 退出：游戏可项目引用同一契约，工具可独立加载相同构建产物，完成正面与负面验收。

## Phase 3：共享查询与无损磁盘校验、CLI 接入

状态：in-progress；依赖 Phase 2

- 目标：AI 直接写文件后可通过 find/schema/validate 获取准确结果，无 UI、无写回。
- 模块：Editor.Shared 项目工具、诊断协议、Headless 适配、相关测试。
- 任务：按项目内资源 GUID/节点/条目 ID 查询并返回全部匹配；schema list/show JSON 描述；原始 JSON 校验、文件位置与对象诊断；保留未知字段并报告，不生成缺失 ID。
- 验证：合法直接编辑、无效 JSON/格式、未知 entry/参数、参数类型/required/约束、自定义 validator、跨 Group 重复条目 ID 的查询；缺契约时基础查询可用，语义校验明确失败；校验前后文件字节不变。
- 文档：命令用法、输出/退出码、结构校验与语义校验区分、既有 execute 支持边界。
- 风险：复用旧 repository 导致过滤/修复数据；必须使用原始读取链路。
- 退出：无平台依赖完成项目查询/校验；CLI 薄适配，非 CLI 消费者可调用同一服务。

2026-10-13 命令复核扩展（见 cli-contract.md）：本 Phase 拆成以下可分别验证提交的子阶段，尚未实现；此前 Phase 1、2 的证据不变。

- Phase 3a：统一操作描述与 help（全量/具体命令/--search/--json），移除旧 commands/扁平别名；稳定 operationId、参数/输出结构、诊断、退出码、dry-run/批次能力声明。验证 help 无项目/契约可用，JSON 描述覆盖真实路由，nullable/默认值/枚举正确，未知命令与参数明确失败。
- Phase 3b：原始读取的 files/find/graph/group/variables 查询，以及 references；确定排序、显式分页、项目路径边界、不隐式跨链接、未知数据保留和未识别引用诊断。验证缺契约基础查询可用、重复 ID 返回全部、交叉过滤/分页/无效 metadata/路径边界正确、查询不写文件。
- Phase 3c：validate、graph validate、group validate 分层诊断及 --structure-only；单文件不要求无关 Group 合法，完整项目检查明确覆盖范围。验证未知字段/缺 ID/错误版本/引用、类型/required/约束、自定义 validator/Composite 错误，结构和语义状态明确，校验字节不变。

Phase 3a 状态：verified。共享层 52/52，架构 29/29；包括 9 项真实 CLI 子进程验收和 16 项操作描述/路由测试。help 全量、具体命令、关键字 JSON 与错误输入无需项目即可工作。操作输入 schema 描述嵌套对象、字典、枚举、默认值、可空类型；不支持的旧 commands/扁平入口返回结构化用法错误。文件 dryRun 明确拒绝且不改文件。当前输出 schema 定义统一结果信封，data 结构随后续查询/校验操作细化；现有加载式 validate 未冒充已完成的原始磁盘校验。

Phase 3b 状态：verified。共享层 64/64、架构 29/29；原始 files/find/graph/group/variables/references 查询与 schema list/show 可调用同一共享服务。真实 CLI 在无契约项目查询成功，schema 查询缺 DLL 明确失败；自定义契约查询包含主 DLL SHA256。交叉过滤、稳定排序、显式分页、重复 ID、无效 metadata、孤立 Group、路径越界与实际目录链接均通过验证，查询前后原始文件字节不变。references 明确返回候选及不完整状态，未声称静态扫描能识别任意自定义代码引用。旧扁平查询入口已删除，不提供兼容别名。

## Phase 3d：单 GalGroup 编译

状态：planned；依赖 Phase 3c，作为 Phase 4 的前置

- 目标：group compile 使用项目 catalog，按 --id/--file 精确选择目标，不运行整项目 build。
- 模块：共享单 Group 编译操作、Core 编译器复用、CLI 路由及测试。
- 任务：无 Graph 归属的文件要求显式输出；默认 Group ID 输出到 Output/Graph/groups；源路径/输出保护；临时发布/失败保留旧产物；dry-run 返回目标与摘要。
- 验证：自定义 Composite 正确展开，所需 primitive 缺失明确失败；无关 Group 无效不阻塞目标；成功只更新目标产物，源/Graph 字节不变；失败、取消和输出冲突不破坏旧产物；dry-run 无写入。
- 退出：单文件与全项目编译复用同一契约/编译逻辑，并取得独立正反面证据。

## Phase 4：项目级 Editor、build/export 与运行注册一致性

状态：planned；依赖 Phase 2、3

- 目标：Editor/CLI/编译/导出均使用当前项目契约，游戏注册与契约匹配。
- 模块：Editor composition/ProjectService/scope/快捷键/catalog 消费者、ProjectContentBuilder、GamePackageExporter、游戏注册检查。
- 任务：移除全局 catalog 和 exporter 内部内置 fallback；依赖 catalog 服务调整为项目生命周期；动态贡献当前 entry 集合；官方按组件注册并保留执行状态所有权；宿主运行覆盖诊断。
- 验证：连续打开 A/B 项目不串用类型；加载失败清理；关闭项目释放消费者；自定义 Composite 经 validate/build/export 得到同一结果；部分内置契约不被全量注册扩充；缺执行和重复实现失败。
- 文档：项目契约来源与运行接入；明确预览不会自动获得自定义平台执行模块。
- 风险：Singleton 捕获项目依赖；Editor preview 的 schema 与全量实现不匹配；跨文件输出/导出失败恢复。
- 退出：项目级 catalog 确实贯穿主路径；只调整数据/生命周期，不改界面布局。

## Phase 5：必要项目/Group 工具与已有命令对齐

状态：planned；依赖 Phase 3、4

- 目标：补齐项目创建、显示名改名、Group 生命周期工具，复用已有编辑命令与引用检查。
- 模块：Editor.Shared/Abstraction 命令与持久化、Headless、ProjectService 引导路径。
- 任务：创建可接收已构建契约目录，空项目生成目录/引导而非虚构可运行；Group 新建/改名/删除同步 Graph 与 Raw 文件；已有写入准确使用项目 schema，不能静默删字段。
- 命令绑定：project/group 主入口与现有单元 operationId 共用业务实现和描述；查询/校验/编译也可被非 CLI 消费者直接调用。按 cli-contract.md 检查各写操作 dry-run/引用诊断/批次边界与帮助声明一致。
- 项目删除：用户已确认送入系统回收站，新增共享操作与 Windows 外层适配；解析/校验确切项目根，Editor 先释放当前会话，成功回收后更新最近记录；不可用/失败明确报错，不回退永久删除。最近记录移除保持独立操作，普通目录搬迁暂不进入范围。
- 验证：新建→独立契约部署→添加 Group/entry→校验→编译；稳定 ID 不随改名改变；Group 删除引用影响、文件/Graph 同步和失败恢复；未知数据的保存边界；dry-run 不写文件/关闭项目；项目根识别、回收适配成功/失败/不支持、当前会话释放及最近记录更新顺序。实际回收验收仅使用明确创建的临时测试项目，验证系统回收站可见与可恢复，不操作仓库或真实用户项目。
- 文档：区分显示名/目录/身份，原始写文件和工具写入均可用；未提供直接入口的候选能力明确说明。
- 风险：项目创建仍有两个来源，Graph/Group 多文件持久化不是天然整体事务。
- 退出：范围内基础生命周期工具有端到端证据；项目删除确实进入系统回收站，不支持/失败路径无永久删除 fallback，未取得实际平台验收时记录限制。

## Phase 6：单平台截图 entry 样例

状态：planned；依赖 Phase 1、2、4，可与 Phase 5 按依赖独立推进

- 目标：以真实场景截图证明独立 schema 可被工具消费、执行可直接调用视图层。
- 模块：Sample 契约项目、Sample.Avalonia 执行模块/实例/组合根、独立示例 Group、Integration/Presentation 测试。
- 任务：sample.screenshot(fileName?)；宿主决定目录；UI 线程调用 GamePage.CaptureScenePng，异步发布 PNG；blocking、取消、失败、PresentationCompletion、CatchUp 无副作用；不加入全内置 schema。
- 验证：工具无 UI 加载/校验/编译；真实 Avalonia 场景产生有效 PNG，尺寸与可辨像素符合预期；文件冲突/路径越界/无效布局/取消清理；CatchUp 不生成文件；没有截图执行的 Headless 运行明确失败。
- 文档：如何创建单平台执行模块，截图目录/参数/快进/读档语义；示例启动与验证命令。
- 风险：截图过早导致空尺寸；跨 UI 线程访问；fire-and-forget 丢失失败；读档重放重复写文件。
- 退出：取得实际 PNG 并视觉检查，契约程序集不引用 Avalonia。GUI/渲染限制须单独报告，不能以 fake 字节标为成功。

## Phase 7：整体验证、独立审核与收尾

状态：planned；依赖所有纳入范围的前置 Phase

- 目标：验证跨工具一致性，整理正式事实和实现总结，不掩盖剩余范围。
- 任务：适当架构/契约/工具/Editor/Presentation/Integration 测试；Sample build/export smoke；格式门禁；按当前环境顺序运行共享依赖构建，遵守已记录的受限 .NET/Avalonia 构建约定。
- 验证：同一独立契约用于游戏引用、CLI 查询/校验/build/export、Editor 两项目切换；截图实物验收；无关工作树改动未被覆盖。
- 文档：同步 architecture/entry-types/file-formats/development 及 CLI/示例使用说明，生成 review.md 与 summary.md，必要 lesson 即时记录。
- 退出：每项声明均有证据，未实现的候选与环境限制明确，全部范围完成后才关闭 feature。

## 尚需收敛事项

- 项目删除语义已确认采用系统回收站；具体平台适配与实物恢复验收在 Phase 5 完成。
- 截图目录使用宿主已有约定；平台不能解析有效目录时应明确配置/诊断。
- 契约目录/入口、新项目命名采用 design.md 默认值，若用户改名先同步两个文档再实现。
- 新增长期架构决定在实现前形成 Proposed ADR；正式规格只记录验证后的结果。
