# Phase 5a：全量 DTO 迁移基础

状态：verified，2026-10-14。当前无阻断项；下一批为 Phase 5b。全量迁移及异步实例简化尚未完成。

## 实现

Field 支持 DefaultJson 与 Choices；生成器保留字段顺序、JSON converter、null 和编辑提示。DefaultJson 不与属性 initializer 共存，非法声明给出 GNF001；JSON 内容与目标类型在 schema 初始化时验证。每次读取分别反序列化默认值，避免列表默认实例共享。

StringBooleanValueConverter 和 StringEnumValueConverter 提供显式字符串编码契约，保留旧内容中的字符串 bool/enum、选项与无引号编辑文本；普通 JsonStringEnumConverter 的原编辑表示不变。ValueCodec.DeserializeFields 集中 DTO 读取；Primitive 与新增 ForDto<T> Composite 入口共用字段事实。Composite 接收原始 JSON，再规范化与读取；手工 catalog.Create 仍可通过 Values 编辑文本创建，业务展开继续显式编写。

audio.stop 迁移到 AudioStopOptions，移除旧参数表和字符串读取。TrackId/ClearQueue 显式 null 仍采用原业务 fallback，FadeOut 保留 minimum 0；输出参数 schema 与旧 CLI 基线逐项完全相同。

## 验证

顺序测试：Core 95、Generator 11、Builtins 82、Presentation 174、Architecture 37、截图/集中组合 Integration 17、Editor 40，共 456 个不重复用例通过。最新后端补充路径定向 9 项再次通过，验证真实 audio.stop 请求。TRX 位于 Temp/F-20261013-02/phase5a-*.trx。

覆盖字符串 bool/enum 严格编码、nullable 值、错误类型/未知字段、JSON 默认值独立实例、真实表单 Choice 提交和 Composite 默认/null/batch/condition/source map。Generator 同时验证生成代码编译与声明诊断。

Editor 初次完整回归 39/40：原 legacy fixture 使用已在 Phase 4 DTO 化的 flow.wait，失去非 DTO 测试条件。改为显式选择手写 duration schema 后完整 40/40；没有更改产品 fallback。该原因记录到 agent lesson。

Contracts 独立部署、CLI 与 Headless 构建通过；Sample Avalonia 随 Integration 构建。真实 CLI audio.stop schema 前后完全一致；GameTestCase 编译 4 个 group，diagnostics 为空。构建存在既有 analyzer warning 与 NU1900 审计端点不可用提示，无编译错误。

## 边界与下一步

当前 43 条 entry 中 7 条参数 entry 使用 DTO，5 条无参数；31 条参数 entry 尚待 5b–5d。本批增加兼容已有 wire 格式所需基础，不声称总代码量下降；净收益在成批迁移与旧壳删除后核算。开放 payload、业务校验、持久快照、平台所有权和复杂实例行为继续保持。
