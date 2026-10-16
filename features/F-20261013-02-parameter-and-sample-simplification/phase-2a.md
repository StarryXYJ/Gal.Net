# Phase 2a：字段描述与消费者迁移

日期：2026-10-14。状态：verified。

## 实现与范围

- Core.Parameters 新增 FieldDescriptor 与 EditorHints/ValueEditorIds。字段保存 ID、值描述、required/JSON 默认值、标签 key 和独立展示提示；EditorId 是开放字符串，Step 不作为合法值约束。没有新增 Helper 项目或第二套动态值类型。
- DynamicParameterDescriptor 组合唯一 FieldDescriptor；DynamicParameterTable.Fields 显式冻结输入顺序，不从反射或排序推断。原 Type/ValueDescriptor 入口只是手写便捷构造。默认值公共校验移到 FieldDescriptor。
- 内置 EntrySchema 不再把 editorType 混进 constraints；既有 EntryParameterType 由独立提示适配，所有内置编辑类型保持原映射。schema show 单独输出 editor 元数据。
- 内置 primitive 的 duration/fadeIn/fadeOut 非负范围、particle.burst 正数数量声明在值约束中，删除对应自定义基础校验分支；参数规范化与工具结构描述使用同一规则。视频跳点顺序、动画 plan、粒子资源等业务校验保留。
- SettingItem<T>.Field 提供同一结构；数值设置的默认值/提交范围校验使用 ValueContract 与 ValueValidation，Step/Format/UnitKey 只作提示。设置 DefaultValue 仍是宿主动态状态，不复制到冻结 Field。ActionSetting 不提供值字段；ChoiceSetting 的动态选项、标签、回退和异步通知保留宿主实现。
- Editor 加载/保存及编译参数转换传完整 ValueDescriptor，保留自定义 JSON 配置。文本转换两个方向均依据 CLR 类型：真实 string 使用原始文本，字符串编码的 enum 使用带引号 JSON。删除失去消费者的 DynamicParameterValue 工具，不保留兼容层。

CLI 分页在 Phase 1 已完成共用；本阶段不再重写 CLI flag/路由语义。视觉/音频效果的 stage/scope、游戏变量模型及执行参数读取保留现有职责；没有发现本阶段必须合并的完整模型。内置 DTO 的声明/读取胶水交由 Phase 5 条件评估，不新增一层每条 Entry 必须维护的 wrapper。

## 验证

串行 dotnet test，使用 --no-restore -m:1 -p:UseSharedCompilation=false，禁用 Avalonia telemetry。

| 项目 | 范围 | 结果 |
|---|---|---|
| GalNet.Core.Tests | 全部 | 90/90 |
| GalNet.Editor.Shared.Tests | 全部，含真实 CLI、契约及保存 | 170/170 |
| GalNet.Presentation.Tests | 全部，含设置应用/保存/动态选项 | 134/134 |
| GalNet.Primitives.Builtins.Tests | 全部 | 73/73 |
| GalNet.Architecture.Tests | 全部 | 31/31 |
| GalNet.IntegrationTests | ScreenshotEntryTests | 14/14 |

合计 512 项通过；临时日志 Temp/F-20261013-02-tests/phase-2a-*.trx 不提交。新增测试覆盖有序冻结、默认/null、开放提示、所有既有 Entry 编辑类型映射、值约束实际拒绝输入、字符串编码枚举真实磁盘修改/保存、设置范围和 Step 的分离。

Headless 显式构建成功；截图集成构建同时覆盖 Sample 项目。通过既有部署脚本独立构建 Sample.Contracts，并整体刷新 GameTestCase/Contracts。实际 schema show GameTestCase flow.wait 返回 numeric 提示及 minimum=0；build GameTestCase 成功编译 4 个 Group、diagnostics=[]。

18 个本阶段 C# 文件执行 whitespace 整理与 import/IDE0005 门禁，git diff --check 通过。初次 import 检查发现两处排序，修正后复验通过；普通命名/分析器/Avalonia 既有警告未扩展清理。没有人工 GUI 或全解决方案回归。本次验证基于当前工作区，其他任务正在修改的平台/Sample 文件不纳入本阶段提交。

## 成本与保留边界

新增字段/提示生产代码 2 个文件、48 行；现有生产文件净增 28 行，总计净增 76 行（含空白/注释，不含测试和文档）。这是共享表单所需的元数据成本，不宣称本阶段整体代码量下降。收益是删除旧转换工具、避免 JSON 配置丢失、将内置基础范围从不可描述的业务 validator 迁入公共 schema，并让设置与 Entry 共用最小字段结构。

没有新增 schema 容器框架：表单可以直接消费 IReadOnlyList<FieldDescriptor>。不强制全部设置/效果/变量迁移；设置队列、强制值及业务校验仍由原对象处理。NumericEditorKind 和旧 Editor UI 选择路径暂留供 Phase 2c 迁移，尚未提供新工厂/控件。旧 string 编辑表示对部分 nullable string 的限制仍存在，后续绑定必须提供省略/null 独立状态。

编辑转换陷阱已记录 [lesson](../../.agents/lessons/L-20261014-02-editor-text-needs-value-contract.md)。公开构造函数签名再次变化，项目契约需重新构建/部署，不提供二进制向后兼容。

## 下一阶段

Phase 2b 在现有 GalNet.Avalonia.Controls 内实现无样式 SchemaForm、字典工厂注册与每字段绑定/释放协议；Phase 2c 再接入 Editor 和玩家设置。
