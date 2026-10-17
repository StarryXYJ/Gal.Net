# 派生主题控件需验证实际模板入口

状态：confirmed
日期：2026-10-18
来源：F-20261015-02-editor-authoring-workspace / P6b

现象：StructuredFieldEditor 继承 Expander，测试直接设置 IsExpanded 后能编辑和保存，但实际 Semi 截图缺少标题和折叠按钮，用户无法从界面展开。已有业务状态和子控件测试全部通过，未覆盖主题模板入口。

根因：自定义派生控件默认使用自身类型作为样式键，而宿主只有标准 Expander 的主题。直接设置属性绕过了缺失的交互模板。

正确做法：复用基类主题时显式覆盖 StyleKeyOverride 为 typeof(Expander)；真实挂载后检查模板内的 ToggleButton 存在，并检查截图。自定义独立皮肤则由宿主提供对应类型的完整主题。

补充：覆盖样式键同时影响类型选择器。首轮完整 Editor 回归 155 通过、1 失败：forms|StructuredFieldEditor 不再匹配，四语文案停在英文默认值。宿主改用 Expander.structured-field 类选择器，并以带 owner 的属性名设置自定义 StyledProperty；需要同时验证主题按钮和本地化刷新。

适用：派生 Expander/Button 等模板控件。禁止仅凭属性赋值后业务结果正确，就声称用户能够触发该功能。

证据：StructuredGroupFormTests 检查实际主题 ToggleButton；p6b-structured-group.png 检查展开/收起入口和字段内容。
