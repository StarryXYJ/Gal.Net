# Editor 本地化绑定应显式依赖语言变化

状态：confirmed
来源：F-20261015-02-editor-authoring-workspace / P2i

现象：EditorLocalizationService 切到 en-US 后，服务返回英文，已挂载 NewProjectPanelView 的创建按钮却仍显示中文；只测试资源查询不能发现此问题。

定位：原 LocalizeExtension 对静态 key 使用 ReflectionBinding("[key]") 并依赖 Item[] 通知。本仓 Avalonia 12 的实际控件没有因此重新求值。P2i 的 NewProjectErrorsAndDisposedViewsKeepCorrectLocalizationLifetime 和完整四语窗口用例复现。

正确做法：静态文案绑定 CurrentCulture，再通过 converter 读取同一服务；动态 key 的 MultiBinding 同时依赖 key 路径与 CurrentCulture。嵌套属性用明确的 Path，避免将带点的属性路径当静态 key。程序创建的对话框绑定保存 IDisposable 并在 Closed 释放。保留 Item[] 通知给现有 ViewModel，但不要假设索引器绑定已刷新。

验证：原静态绑定在中文→英文时稳定失败，修正后真实按钮、Graph、历史、错误和关闭对话框可完成中→英→日→韩→中刷新；关闭对话框后切换不再修改其标题。

适用范围：Editor 的 XAML 本地化、动态字段标签与程序创建窗口。不得以 provider 查询成功代替已打开控件的动态切换验收，也不得以重建整个页面丢弃草稿来获得新文案。
