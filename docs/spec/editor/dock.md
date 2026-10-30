# Dock 面板与布局契约

Dock.Avalonia 承载项目工作区中的文档与工具面板。`EditorDockFactory` 是项目 scope 的面板/布局所有者，`EditorLayoutSession` 协调布局恢复与持久化，`DockLayoutSerializer` 使用明确的布局数据契约。

## 注册与所有权

`IDockPanelContribution` 提供稳定 PanelId、TitleKey、Placement、是否项目内唯一、默认显示/菜单入口、关闭/浮动能力、ViewModel/View 工厂及可选 Inspector。`IsGlobal` 表示当前项目内唯一，不是应用全局 singleton。

公开扩展使用 `DockPanelContributionBase<TViewModel>` 或带实例参数的泛型基类。内置 `DelegateDockPanelContribution<T>` 是 Editor 内部辅助类，不能当作外部公开 API 使用。

面板 Context 关联 ViewModel，视图解析通过注册贡献及 Editor view factory 完成。Dock 页面不拥有项目作者文档；关闭页面释放自己的订阅/控件，不能销毁项目会话或共享历史。

## 内置入口

| PanelId | 主要用途 |
| --- | --- |
| NodeGraph | Graph 工作区 |
| Assets | 资源管理 |
| Log | 日志与诊断 |
| GroupEditor | 按稳定 Group ID 打开的条目文档 |
| FileEditor | 按项目文本缓冲身份打开的文件文档 |
| Variables / Characters | 项目变量及角色 |
| Localization | 游戏文本与翻译 |
| ResourceOverrides | 资源覆盖矩阵 |
| Timeline | 时间轴对象、关键帧与事件 |
| ExternalDebug | 外部玩家调试 |
| Problems | 项目检查与输入问题导航 |

Inspector 根据活动内容贡献选择详情。旧内嵌 GamePreview 和 UiCustomization 不是当前面板贡献；不可用历史布局项在恢复时按实际校验处理。具体注册和默认标志以[内置贡献](../../../src/Editor/GalNet.Editor/Dock/BuiltInDockContributions.cs)为准。

## 焦点与输入

Inspector 跟随当前文档的激活和焦点，工具面板焦点不应抢走作者来源；锁定 Inspector 保留目标。`EntrySelectionContext`、`TimelineEditSession` 等项目服务保存稳定选择和编辑来源，面板不借用已关闭 Group 页的字段控件。

普通 Entry 输入草稿按项目会话保留，Timeline 专属工具仍有自己的确认/取消生命周期。具体规则见[Group](groups.md)、[历史与保存](history-and-save.md)。

## 布局恢复

`EditorLayoutSession` 从 Editor 设置读取已保存布局，通过 serializer 恢复有效文档与浮动窗口，工厂重新关联当前项目服务。布局状态与作者内容历史分离，移动窗口或套用布局不会成为剧情 Undo 操作。

手动工作区预设包括剧情编写、动画编辑和游戏调试；打开工具不自动切换预设。已有用户布局优先，面板最大化可以恢复原布局。无效或缺失的目标不得悄悄绑定到另一个占用旧位置的对象。

添加面板步骤见[扩展 Editor 面板](../../manual/extensions/editor-panels.md)，使用步骤见[工作区](../../manual/editor/projects-and-workspace.md)。
