# Phase 1：中立值描述与公共工具

日期：2026-10-14。状态：verified。

## 实现结果

- Core.Parameters.ValueDescriptor 只保存值类型、null 策略、冻结 JSON 配置和约束，不包含字段名、必填/默认值、UI 或存储职责。
- ValueCodec 提供按描述序列化、反序列化、编辑文本转换及结构描述；支持 DTO、数组、枚举和自定义 JSON 配置。描述组合时重定位局部引用，不改写被引用子类型。
- ValueValidation 返回 ValueIssue（code/message/path）；基础约束覆盖范围、Unicode 字符长度、数组长度、类型化 enum 和旧字符串 options。大整数边界先按 decimal 比较，不统一转 double；浮点非有限值拒绝。
- DynamicParameterDescriptor 组合 ValueDescriptor，保留 Entry 的名称、必填和默认值，可直接接入带自定义 JSON 配置的值描述。已有 Type 构造入口只是便捷构造，不保留两份值规则。旧编辑文本 API 委托公共工具。
- CLI 参数组合中立描述，不引用 Entry 参数类。所有查询分页从同一 minimum/default 声明产生 help 与实际验证，执行读取已解析值；Has 仍表示用户是否显式输入，省略 limit 和显式 0 保持区别。
- 截图名称 null/长度使用公共规则，便携文件名、路径与设备名校验仍属于截图业务。项目 schema 查询使用公共结构描述。

## 边界与收益

新增公共生产代码为 3 个文件，共 191 行（含注释/空行）；未新增项目、运行期依赖、通用值容器或完整字段父类。本阶段建立基础，不宣称生产总代码量已经下降。

原 DynamicParameterTable 文件 114 → 104 行，CliInvocation 56 → 83 行，ScreenshotSchema 26 → 29 行，PrimitiveArgumentHelper 79 → 77 行。CLI binder 增加通用接入代码，删除 Headless 分散的分页读取函数；名称长度、enum/类型检查等规则只有一个执行来源。新增成本是否能被后续设置/编辑消费者复用，继续在 Phase 2a 检查，不以基础已存在为由强推全面迁移。

公共工具适用所有符合 JSON/CLR 描述的动态值；截图与分页仅为验证切片。嵌套 DTO 的结构规则使用 System.Text.Json 配置；根值约束不自动扩散为子字段业务规则。结构描述不能替代自定义 converter/业务 validator 的完整执行语义，也不是任意 JSON Schema 解释器。EditorHints、有序字段结构、设置接入、SchemaForm 和生成器尚未实现。

## 验证

串行运行 dotnet test，统一使用 --no-restore -m:1 -p:UseSharedCompilation=false --verbosity quiet；设置 AVALONIA_TELEMETRY_OPTOUT=1。

| 项目 | 范围 | 最终结果 |
|---|---|---|
| GalNet.Core.Tests | 全部 | 85/85 |
| GalNet.Editor.Shared.Tests | 全部，含真实 CLI/契约加载 | 169/169 |
| GalNet.Primitives.Builtins.Tests | 全部 | 69/69 |
| GalNet.IntegrationTests | ScreenshotEntryTests | 14/14 |
| GalNet.Architecture.Tests | 全部 | 31/31 |

合计 368 项通过。最终日志位于 Temp/F-20261013-02-tests/final-*.trx，为临时验证产物，不提交。Core 和 CLI 新增测试覆盖输入边界、DTO 成员路径、JSON 配置冻结、递归/嵌入引用、Unicode、大整数以及真实 help/files 调用。独立契约部署仍由既有加载/管线测试覆盖；没有运行全解决方案或人工 GUI 验证。

本阶段同时执行改动文件的 whitespace 与 IDE0005 import 检查。已有命名/分析器/Avalonia 警告保留，不作为本 feature 清理范围。

## 过程中修正

- schema show 不声明分页参数，执行不能假定一定存在 offset；保留操作层默认 0。
- serializer 的 enum schema 可仅包含 enum、没有 type；CLI 帮助类型推断与默认值输出必须使用相同 JSON 配置。
- 递归 DTO 的局部引用不能直接搬入包装/父 schema 后保留原路径；也不能假定第一层 Child 必然是引用节点。以实际 JSON Pointer 解析测试验证，相关 [lesson](../../.agents/lessons/L-20261014-01-schema-local-references.md) 已记录。

早期中断试点、上述失败运行均不计作通过；表格只记录最终完整验证。其他工作区 lesson 和 Temp 内容不纳入本阶段提交。

## 部署补验（2026-10-14）

用户实际运行发现 GameTestCase/Contracts 仍保留旧构建，引用已变更的 DynamicParameterDescriptor 五参数构造函数，导致 Method not found。前述测试使用新编译契约，未覆盖仓库内容项目的已部署产物，属于本阶段验证遗漏。

使用 scripts/contracts/build-project-contracts.ps1 重新构建 Sample.Contracts 并整体替换 GameTestCase/Contracts（含 Core、Builtin.Contracts）；构建零警告、零错误。随后运行 Headless 的 `build GameTestCase`，返回 operationId=project.build、success=true、compiledGroupCount=4、diagnostics=[]。未添加旧构造函数兼容重载。部署 DLL 与 Output 属于忽略的生成产物；README 补充 API 改动后必须重新部署的说明。

## 下一阶段

Phase 2a 只迁移有实质重复的消费者，并建立用于设置/Entry 的中立有序字段与开放编辑提示。Phase 2b/2c 再提供共享表单和宿主绑定，不能让 UI 构造进入 Core/契约。
