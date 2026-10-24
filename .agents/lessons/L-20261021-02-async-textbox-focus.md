# 异步提交用只读保护文本输入焦点

状态：confirmed
日期：2026-10-21
来源：F-20261019-01-editor-game-localization / P5

i18n 表格测试首次 Ctrl+Enter 提交成功，接着编辑再移出焦点却不提交。提交期间 Busy 绑定到 TextBox.IsEnabled 会让仍在编辑的输入框立即丢失焦点；恢复启用不会恢复原焦点，后续不再有预期的失焦事件。

提交期间绑定 TextBox.IsReadOnly，禁止用户改写正在提交的值，同时维持控件启用和焦点。保留异步重入保护，提交完成后重新读取作者值；项目失效仍独立禁止写入。实际挂载 Ctrl+Enter→再输入→失焦提交在修正后通过，而不是在测试中额外 Focus 掩盖行为。

关联：features/F-20261019-01-editor-game-localization/phase-5.md。
