# DTO 迁移保留专属 Entry 编辑器

状态：confirmed；来源：F-20261013-02 Phase 5b。

现象：variable.set DTO 化后 Editor 的变量目标测试失败，专属 VariableName 控件投影消失；DTO 整组表单也没有 entry.* 专属工厂。

根因：ConfigureParameterFields 仅依据 FieldDataType 非空就提前返回整组 SchemaForm，绕过既有补全、资源、表达式和多行文本控件。

发现方式：完整 Editor 回归 39/40，检查注册表与逐字段投影；保留含专属提示 DTO 的原投影后 40/40，再增加混合字段绑定与过期提交回归。

正确做法：DTO 参数事实和 UI 控件选择独立。没有对应 factory 时保留现有专属控件，普通字段继续共享 schema binding；不重新手写 DTO 字段描述。逐字段与整组表单均使用同一 DTO.Schema。

适用范围：内置/项目 Entry DTO 化及 Editor 专属控件迁移。禁止静默降级为普通 TextBox、删除补全/资源过滤/表达式验证，或为此引入第二套参数事实。
