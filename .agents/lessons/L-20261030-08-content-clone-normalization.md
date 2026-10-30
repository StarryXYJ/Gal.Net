# 内容克隆不能隐式规范化持久字段

- 状态：confirmed，2026-10-30。
- 来源：F-20261030-01-project-quality / P5。
- 范围：作者快照、保存基线与 ProjectVariableDefinition.Clone。

逐字段契约测试发现：单独修改变量声明 Name 或 DefaultValue.Name 后，克隆把 DefaultValue.Name 改成声明 Name。四个字段用例失败；两个真实 SaveAsync 用例进一步确认保存成功后 IsDirty 仍为 true。证据：out/project-quality-p5-contract-before.log、out/project-quality-p5-save-before.log。

原因：工作版比较覆盖两个名称，作者校验要求它们一致，但保存快照经过 Clone 后被隐式规范化，绕过了名称不一致校验；工作版仍保留无效输入，保存后继续 dirty。克隆不再代表调用方捕获的作者内容。修正后的保存应拒绝不一致名称、保留磁盘与完整恢复草稿；显式修正名称后才成功并变 clean。

正确做法：克隆保留全部持久字段，复用 Variable.Clone 创建独立可变外壳；VariableValue 是封闭不可变值，可安全共享。名称同步属于 GUI/CLI 的显式重命名或类型修改操作，不能藏进快照捕获。增加字段时同时检查比较、克隆与真实保存后 clean；只验证 dirty 变 true 无法发现这种问题。

禁止通过保存后强制清 dirty、忽略该字段或修改工作版来掩盖快照不一致。正式文档仍由用户后续整理。
