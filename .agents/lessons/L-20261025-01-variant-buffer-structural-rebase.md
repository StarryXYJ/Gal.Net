# 资源结构重写必须覆盖已跟踪缓冲

状态：confirmed
日期：2026-10-25
来源：资源 ID、选择器缓存与文件移动同步修复

现象：移动 `.variants` 或其图片目录时，文件事务已计算新 folder，但打开过的描述文件由 ProjectContentSession 的 ManagedContent 写出；仅修改事务备份不能改变实际写出的缓冲内容。

根因：ProjectFileOperation 的非托管 Copies 会跳过 ManagedContent；ProjectContentSession 从保存版 EditorFileDocument.Data 写出文件。原 FileChange.Apply 只移动 Path，没有对工作版和保存版的 Data 分别重基。

正确做法：在结构变更的 Apply 内根据当前文档位置和当前 folder 重算路径，分别转换工作内容与最近保存内容；验证也覆盖两份转换后的文档。Undo/Redo 从当时的配置重算，不能恢复首次捕获的整份 JSON，否则会回退之后保存的 default 或 dimensions。

验证：ResourceIdentityTests.cs 中 OpenVariantBufferRebasesFolderAndPreservesSavedAndUnsavedChanges 的描述移动/图片目录移动两例均通过，覆盖打开的 Inspector、未保存 default、磁盘保存基线、Save 后 Undo/Redo。关联规范：docs/spec/editor/project-and-files.md。

适用：文件结构操作会重写内部关系，且文件可进入项目文本缓冲的场景。禁止仅改路径/磁盘备份或以未打开文件的测试替代缓冲生命周期验证。
