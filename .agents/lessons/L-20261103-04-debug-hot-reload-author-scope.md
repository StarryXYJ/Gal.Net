# 调试热重载以作者操作范围分类

状态：confirmed
来源：F-20261103-01-debug-content-hot-reload

## 现象与根因

在 Editor 中修改当前或未来 Entry 的剧情文本，只会改变 I18n 文件；编译 Entry 仍保存稳定本地化 key。若根据构建文件摘要反推变化范围，I18n 变化会被误判为全局变化，导致所有文本修改都要求重新开始游戏。

## 正确边界

- Editor UI 修改以 `AuthorChangeJournal` 已知的 Group/Entry 范围为分类依据；构建只产生完整候选，不推断修改来源。
- 仅涉及 Entry 的本地化操作必须保留对应 Group/Entry 范围，不能因为落盘在 I18n 文件而升级为全局变化。
- Editor 内没有 Entry 范围的操作，以及文件监听发现的外部重载，统一按全局变化要求重新开始游戏。
- 回归必须真实修改本地化剧情文本，分别覆盖未来 Entry 直接应用和当前已执行 Entry 返回 Group 开头；只修改 Condition 无法复现该问题。

关联文档：`features/F-20261103-01-debug-content-hot-reload/design.md`、`docs/spec/editor/debugging.md`。
