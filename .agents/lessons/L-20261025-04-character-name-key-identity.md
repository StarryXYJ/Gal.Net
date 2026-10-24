# 姓名文本 key 不能替代角色身份

状态：confirmed
日期：2026-10-25
来源：features/F-20261023-01-editor-uiux/phase-ui-20261025.md

现象：若角色草稿和选择只按 DisplayNameKey 缓存，两个共享姓名文本的角色会互相取得草稿；删除一个角色后还可能选中另一个角色。

根因：CharacterCatalog 只校验角色 ID 唯一，允许多个角色使用同一 DisplayNameKey。GUI 新建默认生成不同姓名文本 key，不代表所有合法作者数据都具有该唯一性。

验证：源码 CharacterCatalog 的唯一约束与 CharactersSharingANameTextNeverShareDraftsOrAcquireADeletedCharactersSelection 回归共同覆盖；角色 ID 改名跟随与旧基线拒绝另有连续性回归。

正确做法：按角色 ID 与姓名文本 key 组合区分草稿。仅在会话中始终未出现共享的姓名文本 key 上允许推断 ID 改名；一旦出现共享，不凭文本 key 合并身份。重基缓存键时保留原声明基线，使外部修改后的提交仍接受冲突检查。

适用：角色表、引用选择、搜索投影与项目级草稿。禁止将文本 key、显示名或行号当作普遍唯一的作者对象身份。文本共享与作者对象身份是不同契约。
