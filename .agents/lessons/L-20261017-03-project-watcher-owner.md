# 项目文件监听器必须持有所属项目的身份

状态：confirmed
日期：2026-10-17
来源：F-20261015-02-editor-authoring-workspace / P4b

现象：第一轮 P4b 测试在项目关闭后被 FileSystemWatcher 回调中的 NullReferenceException 终止，日志为 p4b-initial.log/trx。回调通过 Index.Root 再读取全局 IProjectService.Current.RootPath，而关闭流程已经清空 Current。

根因：项目 scope 及其后台回调的释放晚于全局当前项目切换。延迟事件不能假设全局 Current 仍是创建监听器的项目；相同问题还可能把旧项目事件投递给新项目。

正确做法：项目 index 在创建时捕获固定根路径；回调先检查 disposed，只使用该固定路径。投递到 UI 后再次核对当前项目身份和 disposed，再更新列表。停止 watcher/timer 后仍需防范已经排队的回调。

验证：修正后 P4b-cases-fixed 的 13 项包括连续创建/关闭项目和真实挂载的文件面板全部通过，未捕获并吞掉原始 NullReferenceException；测试主机不再退出。

适用范围：项目级 watcher、异步扫描、thumbnail 和延迟刷新。禁止让后台回调通过全局 Current 动态决定它的所属根目录。
