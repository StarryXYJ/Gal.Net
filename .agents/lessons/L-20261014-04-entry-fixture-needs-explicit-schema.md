# Entry fixture 必须显式选择待测试 schema

状态：confirmed；来源：F-20261013-02 Phase 5a。

现象：EditorWorkspaceViewModelTests 的 legacy 参数编辑分支在 flow.wait DTO 化后失败，ParameterFields.Single 找不到项，但共享表单正常存在。

根因：fixture 用 BuiltinModules.Wait 代表旧 schema，生产迁移改变了测试前提。git show Phase 4 确认此前已存在该依赖。

发现方式：完整 Editor 回归 39/40，检查 fixture 与 DTO 分支；显式注册手写 duration schema 后 40/40。

正确做法：测试 legacy/DTO 两种接入路径时分别显式提供对应 schema，不借用会继续迁移的 builtin 作为固定模型。

适用范围：Entry schema 迁移、Editor/CLI 回归 fixture。禁止为了过时 fixture 给产品增加 fallback 或重新引入重复声明。
