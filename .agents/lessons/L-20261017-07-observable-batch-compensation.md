# 可观察集合批量历史要按实际状态补偿

状态：confirmed
日期：2026-10-17
来源：F-20261015-02-editor-authoring-workspace / P5b

发现方式：复核 GroupEntryAuthoringService 的 PresenceEdit/OrderEdit。ObservableCollection 会先修改集合再发通知；观察者抛错时，抛错的当前 Insert/Remove/Move 可能已经生效。只统计“正常返回的操作数”再逆向恢复，会漏掉当前一步，导致历史未追加或游标未移动、内容却已变化。

正确做法：批次开始记录受影响集合的实际顺序与对象身份；异常时对照真实集合恢复缺失项、移除本次新增项并恢复顺序/序号。恢复失败抛 HistoryRepairRequiredException，交由项目历史阻止后续写入。不能捕获异常后静默返回成功，也不能仅依赖 CompositeEdit 补偿已经正常返回的子步骤。

验证入口：EntryBatchFailuresRestoreContentAndHistoryOrRequireRepair，覆盖新增/删除/移动、Undo/Redo 失败时状态与游标，以及恢复再失败时项目 RepairRequired。p5b-failure.trx 的故障注入测试已通过，覆盖全部上述路径；适用于会触发同步通知的集合型历史。
