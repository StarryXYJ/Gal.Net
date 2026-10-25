# 横向表格变窄时必须明确共用左边界

状态：confirmed
来源：features/F-20261023-01-editor-uiux/follow-up-localization-20261025.md

现象：本地化表格在所有语言显示时对齐，隐藏部分语言后表头与正文错位。真实挂载测试在隐藏中间语言后测得正文左边界 362 DIP、表头 483 DIP，相差 121 DIP。

根因：表头 Grid 指定 Width，但保持默认 HorizontalAlignment；内容宽度变得小于 ScrollViewer 视口时，表头居中，而 ListBox 显式靠左。宽表场景掩盖了差异，与单元格语言顺序无关。

修正：表头与正文都显式 HorizontalAlignment=Left，并使用同一组列宽。只验证语言数据或列数不能发现实际布局错位。

验证：I18nVisibleLanguageHeadersStayAlignedWhenTableBecomesNarrowerThanViewport 在修复前失败，修复后通过；覆盖横向滚动后隐藏中间列、仅剩源语言及恢复语言，直接比较真实表头/单元格的 X 与宽度。不要只测表格宽于视口的初始状态。
