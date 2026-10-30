# Entry 撤销不能复活旧控件的提交权

状态：candidate
日期：2026-10-18
来源：F-20261015-02-editor-authoring-workspace / P5d

迁移审查发现，仅按 Entry 存在性和 type ID 校验旧字段不能区分“当前绑定”与“删除/换类型后又撤销恢复的旧绑定”。稳定 Entry ID 应跨撤销保持，但控件绑定的生命周期不能因此恢复。

实现采用参数表单 Dispose、字段集合成员身份及专属控件 schema generation：条目移除/换类型/页面关闭使现有绑定永久失效；撤销恢复作者数据时重新创建绑定。项目历史只保留稳定 ID 和作者值，不保留页面。禁止用“条目又出现了/类型又相同了”作为恢复旧控件提交权的条件。

EntryBindingsPreserveAuthorJsonAndRejectRemovedAndReplacedSchemas 验证换类型、删除及各自撤销后旧字段提交仍返回 obsolete，当前绑定正常读取作者内容；跨页面挂载测试验证页面关闭后旧表单拒绝提交，项目 Undo/Redo 和保存重开仍有效。p5d-binding-focused.trx 的 4 项均通过；本条是审查发现后的防护回归，未记录修复前失败运行，因此保留 candidate，不宣称先前生产环境已复现。

适用：异步 DTO/字段编辑器、资源选择器、页面重建和撤销恢复。相关规则见 docs/spec/editor/groups.md。
