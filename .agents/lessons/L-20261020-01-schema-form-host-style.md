# DTO 表单跨宿主时必须显式提供展示样式

状态：confirmed
日期：2026-10-20
来源：F-20261015-02-editor-authoring-workspace / P6e4b

现象：Timeline 事件弹窗的 DtoEditorHost 构建成功、Diagnostics 为空，但找不到 TransformX，实际表单没有字段。真实挂载测试 events-b-focused.trx 复现，只有模型/控件构造检查不能发现。

根因：SchemaForm 是需要宿主模板的共享 TemplatedControl；Editor 原先把模板和字段操作样式限定在 EntryListEditorControl.Styles 内。新的 Timeline Flyout 不在该样式作用域中。DtoEditorHost 的诊断不等于子 SchemaForm 已取得模板或没有 fallback 诊断。

修正：把 Editor 表单样式抽到 Controls/AuthorFormStyles.axaml，由 Group 和事件弹窗显式引用；共享控件继续不依赖 Editor 外观。字段注册仍使用同一个 EditorRegistry，事件所需资产字段注册复用 AssetPicker。

验证：真实 popup 中取得 TransformX 并编辑、应用单历史、检查子 SchemaForm.Diagnostics；十种内置事件均实际挂载，检查资产选择、事件前场景选择及隐藏输出身份。events-b-form-focused.trx 与 events-b-all-types.trx 通过；保留首轮失败日志。

适用：把 DtoEditorHost / SchemaForm 接到 Dock、Flyout 或新窗口时。禁止仅凭 Host.Diagnostics 为空或编译成功宣称表单可用；验证模板、实际字段、子诊断和提交链路。不要为复用样式引入共享 UI 对 Editor 的依赖。
