# Inspector 必须跟随 Dock 焦点

状态：confirmed
日期：2026-10-24
来源：features/F-20261023-01-editor-uiux/follow-up-20261024.md

复现：Group 已是主文档的 ActiveDockable，点击底部资源区后再点击 Group 条目。主文档 ActiveDockable 没变，只有 FocusedDockable 改变；仅监听 ActiveDockableChanged 会把资源 Inspector 留在画面中。

规则：Inspector 和撤销路由同时处理标签激活与 Dock 焦点。辅助工具没有 Inspector 时，不覆盖最近可检查的作者目标；后台 Workspace 选择刷新不能抢走当前目标。锁定期间仍更新最近作者上下文，解锁时再跟随。焦点事件不应被当作作者历史或触发布局自动保存的内容变化。

验证：UiuxInspectorFollowsFocusBackToAlreadyActiveGroupAndRespectsLock 使用真实项目 Dock Factory 的 SetFocusedDockable，覆盖跨区域返回、锁定/解锁、后台资源刷新及辅助 Log；修复前稳定失败。
