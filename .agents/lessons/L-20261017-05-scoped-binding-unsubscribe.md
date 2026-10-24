# Scoped binding 的订阅必须只退订一次

状态：confirmed
日期：2026-10-17
来源：F-20261015-02-editor-authoring-workspace / P6a

现象：DtoEditorTests 首轮 27/29 通过，移除一个 DTO 后订阅计数为 -1，失败工厂与正常项共享测试 binding 时计数为 0 而非 1；证据 p4b-validation/dto-ui-focused.trx。

根因：字段 FieldEditorContext.Dispose 先从 scoped binding 移除订阅，随后 DtoEditorContext.Dispose 再释放 scoped binding，两条释放路径均无条件向借用 source 退订。普通 CLR event 的重复 remove 看似无害，但拥有自定义事件访问器的宿主可能按订阅计数维护资源。

正确做法：包装器显式记录是否实际订阅 source。最后一个字段订阅移除和包装器 Dispose 统一走幂等 Unsubscribe；只移除自己建立的 source 事件，不释放借用 binding。上下文释放保留异常并继续清理其他字段/控件。

验证：修正后共享聚焦 29/29 通过；重复类型重排、移除、创建失败与窗口卸载均检查订阅数量。新增 release failure 用例继续验证其他项仍被释放。

适用：多层 binding/事件转发包装器、控件 lease、并行存在的多次释放入口。禁止把重复 event remove 无异常当作生命周期正确性的证据。