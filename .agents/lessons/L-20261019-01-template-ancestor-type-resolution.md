# 延迟模板中的祖先类型需要显式解析

状态：confirmed
日期：2026-10-19
来源：F-20261015-02-editor-authoring-workspace / Group 属性编辑器改造

现象：Group 字段 DataTemplate 中的反射绑定 `$parent[forms:SchemaForm].ShowLabels` 可以通过 XAML 编译，但实际挂载时抛出 `Unable to resolve type forms:SchemaForm`，整个参数区无法生成。三个真实 Group 控件测试同时失败，单独 FieldEditorContext 测试通过。

根因：该延迟模板的运行期 ReflectionBinding 类型解析上下文不能解析这段表达式的命名空间别名。构建成功不能证明运行时绑定可解析。

修正：用 `ShowLabels, RelativeSource={RelativeSource AncestorType={x:Type forms:SchemaForm}}`，由 XAML 显式解析类型。修正后五项重点测试以及完整 Editor 181 项回归通过，包含宽窄截图、嵌套对象/列表与 Timeline 挂载。

适用：带命名空间祖先类型的延迟 DataTemplate/ControlTemplate 反射绑定。不要凭编译结果宣称模板可用；需要真实主题挂载、交互与截图。此结论针对已复现表达式，不声称所有 `$parent` 语法都不可用。

证据：features/F-20261015-02-editor-authoring-workspace/phase-group-properties.md；Temp/group-properties/group-properties-focused.trx、GalNet.Editor.Tests.verified.trx。
