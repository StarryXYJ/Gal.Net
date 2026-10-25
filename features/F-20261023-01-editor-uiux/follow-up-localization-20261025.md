# 本地化表格实测修正

状态：verified（自动验证）。用户明确要求修复隐藏语言后的列对齐、正文从左上编辑，并将逐字更新改为失焦提交以降低输入卡顿。本轮沿用 feature 实现流程，不改变翻译文件格式及项目保存事务。

## 设计修订与实现

- 表头与内容行都固定左对齐。原表头指定 Width 但保留默认对齐，语言减少到比视口窄时居中，而 ListBox 内容靠左。真实挂载测试复现首列相差 121 DIP；覆盖横向滚动后隐藏中间列、仅剩开发语言和恢复列。
- TextBox 的 HorizontalContentAlignment/TextAlignment 为 Left、VerticalContentAlignment 为 Top，浏览摘要也置顶。测试检查实际 TextPresenter 的顶部坐标，不只检查属性值。
- 输入只更新单元格文字，捕获一次来源基线；不逐字调用作者写入，也不逐字通知全项目刷新。首次待提交状态仍通知保存/dirty 状态，同时抑制这次通知引起的自身表格重建。
- 失焦或 Ctrl+Enter 提交一次当前文本，Save All 收拢未提交输入；空串、错误重试、来源失效保护保留。提交期间再输入的内容等待后续提交，不自动接着逐字写入。Escape 放弃未提交输入；若上一笔提交尚在进行，仅取消后续输入，保留已送出的值。

## 验证

进行中。原错位复现日志：`artifacts-localization-alignment-repro.log`。第一次定向 33/34 通过，失败定位到上一笔失焦提交未结束时立即重新输入并按 Escape 的边界；已补上对当前已送出值的保留和输入框同步，继续复验。首次测试编译缺少 Avalonia 坐标扩展 using，已补齐。

## 最终结果

- 完整 Editor 回归 **295/295 通过**，未排除用例；日志 `artifacts-localization-blur-editor.log`，TRX `test/GalNet.Editor.Tests/TestResults/localization-blur-editor.trx`。覆盖新隐藏列坐标回归、逐字输入无写入/无逐字项目刷新、失焦/Ctrl+Enter/Save All、输入法组合态、异步提交后再输入、Escape、空串与来源冲突。
- `dotnet build GalNet.slnx --no-restore -c Release -m:1` **0 错误、256 警告**，包含平台目标；日志 `artifacts-localization-blur-solution.log`。构建在测试结束后顺序进行，未重现并行复制 DLL 锁冲突；未把现有 CA/nullable/XAML 警告描述为无警告构建。
- 五个受影响 C# 文件的只读 whitespace 与 IDE0005/style 检查通过，`git diff --check` 通过。style 指出的视图 using 顺序已修正，不混入其他模块格式化。
- TextChanging 在渲染前同步捕获单元格本地输入，避免 TextChanged 的异步渲染时机让立即 Save All 或 Escape 读到旧值；真实挂载测试在赋值后不等待渲染直接 Save All，检查保存内容与焦点，已通过。
- 正式 spec/manual、feature summary 与 agent 上下文已更新。记录两条可复用经验：显式窄表左边界、同步捕获本地待提交输入。原生 IME 窗口和真实系统 DPI 保留桌面人工验收边界。

本轮不恢复常驻草稿/复核 UI，不修改作者格式或 Runtime。已有的角色诊断相关改动与四语资源修改保留，未纳入本轮提交。
