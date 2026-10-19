# GalGroup 属性编辑器改造

日期：2026-10-19。状态：verified。

用户已明确授权实现 [属性编辑器设计](group-property-editor-design.md)。此切片独立于 Timeline 图形曲线阶段；不改写 P6e3a 的完成状态，不实现实时预览。

## 范围与退出条件

1. 统一字段行、标题左上、重置同行右侧、窄 Dock 排版；包括现有专属资源/文本字段，Group 不显示常驻 null/省略/恢复草稿按钮。
2. Group 的空非字符串可选输入恢复缺省；默认值重置、可选无默认值清空、必填无默认值禁用，原文件 null 不因显示改变。Sample 的现有操作保持独立。
3. Builtins Transform/ToTransform 成员注册为结构控件；只更新目标成员，保留大小写与未知内容；颜色使用支持透明度的主题拾色器，确认一次历史。
4. Timeline 成员改为跳转按钮和摘要，先定位 Entry 再激活单实例 Dock；缺省时不因打开创建数据，旧绑定失效。
5. 普通 DTO/列表继续按现有类型递归分派，改进组标题和元素重复标签；高级源码仍作为不能用结构控件处理的值的修复入口。

验证要求：真实主题挂载及宽窄布局截图；Transform/Color 单历史、取消和保存重开；Timeline 非活动行定位及关闭重开；清空与默认值语义；受影响的 Editor/共享控件回归、架构检查、解决方案构建和格式验证。只有证据齐全才标记 verified。

## 实施选择

- 复用成员注册覆盖 JsonElement 参数，没有迁移 Core/Runtime 或文件格式。
- 统一 FieldEditorRow 由共享 Controls 提供，Group 自行选择模板和简化动作；不更改 Sample 操作策略。
- 默认值重置与无默认可选清空共用一个图标，Tooltip 分别说明；必填无默认禁用。
- 对象折叠仍保留懒创建/释放语义；不在此切片移除八层/200 元素防护或加入任意动态结构编辑器。

## 验证记录

证据写入 `Temp/group-properties`，初轮构建日志 `Temp/group-properties-build.log`。首轮捕获 Transform 控件局部事件闭包的 definite-assignment 编译错误，已修正；不是运行期或既有基线故障。

真实挂载时三项测试暴露延迟模板无法解析命名空间祖先类型的问题；改为显式 RelativeSource + x:Type 后通过。见 `.agents/lessons/L-20261019-01-template-ancestor-type-resolution.md`。沙箱内 VSTest 在 testhost 连接本机回环端口阶段停滞，诊断日志 `Temp/group-properties-testhost-diag.log`；授权的沙箱外测试能够正常启动，不将启动失败记为用例失败或通过。

完整 Editor 181、Presentation 243、Shared 223、Architecture 45、Integration 110 项通过，共 802 个不同用例。Presentation 另外六个原生媒体环境用例未执行，不计入通过数。初轮后补齐截图中的 Transform 子输入对齐、清空与无关更新保留草稿，以及无默认 Timeline 禁止通用重置；最终重新执行 Editor 181 和 Presentation 243 项，均通过。普通对象根校验失败后的源码修复、Typed Transform 列表新增/重排旧绑定、Timeline 禁用重置均有实际控件测试。

最终解决方案构建成功，16 条警告、0 错误；whitespace 与 IDE0005 的 verify-no-changes 检查通过，最终源码 SHA-256 核对一致。末轮 CanReset 属性曾触发 C# 14 的 field 关键字绑定冲突，已用 @field 明确引用构造参数后重新构建和回归，不保留失败绕过。

证据：

- `Temp/group-properties/GalNet.Editor.Tests.final.trx`、`GalNet.Presentation.Tests.final.trx`，其余为 `GalNet.Editor.Shared.Tests.trx`、`GalNet.Architecture.Tests.trx`、`GalNet.IntegrationTests.trx`；聚合见 `test-summary.json`。媒体未执行项以逐项 Result 的 NotExecuted 为准，不误用 NUnit 汇总计数。
- `Temp/group-properties/solution-build.log`、`format-verify.log`、`imports-verify.log`、`source-sha256.json`。
- `group-properties-wide.png`、`group-properties-narrow.png`、`p6b-structured-group.png`、`p6d-timeline-dock.png` 为实际 Semi 挂载渲染；宽窄截图分别为 1000×760 与 520×760。
- 四语各 727 keys，键集合及摘要占位符 `{0}/{1}/{2}` 一致。

正式事实同步至 `docs/spec/editor-groups.md`、`docs/spec/control.md`。本补充切片 verified，不改变整个 F-20261015-02 仍 in-progress 的状态。

## 自审与边界

- 专属编辑器仍提交项目作者意图；没有写入 Runtime 或改变作者/编译格式，普通子项 patch 保留未知内容，父级明确重置才替换对象。
- Timeline 跳转携带稳定 Group/Entry 身份，借用作用域装饰转发宿主元数据，同时保留过期绑定检查；未创建定义不因导航写入。
- 常态只显示重置；Transform/Timeline 源值异常才提供修复，普通 DTO 局部修改被根校验拒绝时显示源码入口。没有为了消除 JSON 而丢失坏值修复路径。
- 源值比较使用语义 JSON 相等，避免同值快照克隆或其他字段更新取消 Transform 草稿；列表结构换代仍取消旧元素输入。
- 保留八层/200 元素防护与动态字典、复杂转换器的 JSON 回退；未实现任意多态 schema、长列表虚拟化或布局特性/generator 扩展。
- 验证使用实际 Semi 主题挂载和 Headless 渲染，覆盖宽窄窗口与四语；未宣称完成真实桌面多 DPI 人工创作全程或原生媒体验证。Timeline 图形曲线、事件及实时预览均不在本切片范围。
