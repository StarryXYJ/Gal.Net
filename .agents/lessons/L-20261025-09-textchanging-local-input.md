# 失焦提交仍需同步捕获本地输入

状态：confirmed
来源：features/F-20261023-01-editor-uiux/follow-up-localization-20261025.md

项目安装的 Avalonia 12.0.5 XML 文档明确：TextBox.TextChanged 在新文本渲染后异步发生；TextChanging 在 Text 属性更新后、渲染前同步发生。用 TextChanged 作为本地待提交模型的唯一入口，可能让紧随文字变更的 Escape 或 Save All 看到旧模型。

本地化表格改为 TextChanging 只同步单元格本地文字，不调用作者写入；失焦/Ctrl+Enter/Save All 才提交。保留 PreeditText 组合输入保护，提交失败仍保留输入，模型回填时按相等值过滤防止重入。已经送入历史的异步提交与后来新输入分开处理，Escape 只取消后者。

验证入口：I18nMountedTableCommitsOnBlurProtectsCompositionAndRetainsViewState 在 TextBox.Text 赋值后不等待渲染直接 Save All，检查保存值与焦点；另覆盖组字、Esc 和失焦提交。该挂载测试已通过；最终完整回归结果见本轮实施记录。
