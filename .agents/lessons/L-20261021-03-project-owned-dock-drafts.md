# 关闭 Dock 不释放项目持有的草稿模型

状态：confirmed
日期：2026-10-21
来源：F-20261019-01-editor-game-localization / P5

手动将视图从窗口移除再重建能保留草稿，不代表真实 Dock 菜单关闭也正确。P5 的 Scoped ViewModel 在该路径测试中 IsDisposed 变为 true；EditorDockFactory.CloseDocument 对任意 IDisposable Context 执行 Dispose，绕过了项目 scope 的实际生命周期。

本地化 ViewModel 和草稿由项目 scope 持有，普通 CloseDocument 保留它，视图只解绑自己的订阅；项目关闭仍由 DI scope 释放 ViewModel 和草稿注册。不要在重开时返回已 Dispose 的 scoped 实例，也不要为了维持草稿而禁止项目释放。

真实菜单打开两次→输入待提交草稿→关闭→重开验证同一模型、IsDisposed=false、草稿仍在；最终 P5 定向 14 项通过。适用于需要跨面板关闭保留草稿的项目级工具。关联：features/F-20261019-01-editor-game-localization/phase-5.md。
