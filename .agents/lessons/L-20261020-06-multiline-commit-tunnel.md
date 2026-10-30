# 多行 TextBox 提交快捷键在 Tunnel 处理

状态：confirmed
日期：2026-10-20
来源：F-20261019-01-editor-game-localization / P4

首轮 P4 定向中后端文本/复制测试通过，但两个真实挂载的多行原文输入用例无法提交。TextBox 的类处理会先消费 Enter 类按键，普通 KeyDown 订阅不能可靠收到 Ctrl+Enter。

GameTextFieldEditor 使用 InputElement.KeyDownEvent 的 RoutingStrategies.Tunnel，在 TextBox 默认插入换行前处理 Ctrl+Enter；普通 Enter 保留多行输入，单行 Enter 提交，组合态不提交。之后相关 27 项及 29 项定向通过。不要仅调用后端提交或手动设置 VM 来替代多行输入控件的实际 routed input 验证。

关联：features/F-20261019-01-editor-game-localization/phase-4.md；docs/spec/editor/localization.md。
