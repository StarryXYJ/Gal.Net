# 第四轮：结构化配置入口

2026-11-03，P4 进行中。配置 section 不再将所有未指定 editor 的非标量字段强制指定 JSON；交由现有 EditorRegistry 按 schema/集合和扩展注册选择结构编辑器。保留明确指定的 editor、字段绑定、草稿及 patch。

新增真实控件测试：扩展 section 的字符串列表使用 StructuredFieldEditor，修改后未声明的兄弟字段保留；既有音频配置草稿、Undo/Redo、保存重开测试同时运行，2/2 通过。

未完成：音频专用布局、其他复杂结构/异常和修复入口清单，以及真实桌面验证；未知结构依然会回退 JSON，不能把本轮解释为完成 P4/P5。
