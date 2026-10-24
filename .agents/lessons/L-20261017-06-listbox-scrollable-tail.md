# 可滚动尾部留白必须验证满视口并使用主题公开入口

状态：confirmed
日期：2026-10-17
来源：F-20261015-02-editor-authoring-workspace / P5a

现象：短列表背景取消选择测试通过，但完整回归中的长列表断言显示最后一项与视口底部间距为 0，而非预期约 64 DIP。首轮 Editor 140/141 通过，失败为 GroupToolbarBatchesAreSingleHistoryStepsAndSpaceInsertsOneAtCapturedAnchor，当时日志见 phase-5a.md；同名 editor.trx 已被最终成功回归覆盖。

根因与验证：初版通过普通样式设置 ItemsPresenter.Margin，没有在所用 Semi 模板下获得实际尾部留白。该模板将 ListBox.Padding 绑定到 ItemsPresenter.Margin；改用 ListBox.Padding 后，不更换 ItemsPanel、不添加伪条目，超过视口时可滚动到最后一项之后的空白，group-tail-fixed.trx 通过。

正确做法：使用主题公开的 Padding 入口，并在项目实际主题下检查滚动到最大 Offset 后最后容器的相对位置。验证列表应超过 Viewport；短列表天然存在的空白不能证明尾部空间有效。不要为留白移除虚拟化或把假 Entry 放进作者集合。

公开模板参考：https://github.com/irihitech/Semi.Avalonia/blob/main/src/Semi.Avalonia/Controls/ListBox.axaml 。参考源码不代替所安装版本的实际控件测试。适用范围：列表尾部插入区域、背景取消选择、虚拟化列表的 margin/padding 调整。