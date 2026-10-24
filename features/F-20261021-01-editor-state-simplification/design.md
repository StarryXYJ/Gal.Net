# 实施设计

## 写入口

- 删除无调用者的 Workspace 创建项目、旧 Graph 编辑包装、移动 Entry 包装。
- 删除 GraphEditingService 的 Entry 增删移动与编号实现，保留连接器显示状态职责。
- 删除 VariableDefinitions 集合包装及 Preview 声明编辑表单。变量面板通过 VariableAuthoringService 修改声明及明确引用；Preview 只提交运行时当前值。
- IVariableDefinitionService 仅返回保留稳定 UID 的 detached 声明快照，并发布刷新通知，移除声明写 API 和名称修正策略。
- 删除 Workspace 监听变量后补历史的快照、回放与重入标记，以及不再使用的 PushAlreadyApplied 入口。旧 API 直接移除。

## 状态所有权

GUI 的唯一内容比较来源为 ProjectContentSession.IsDirty。删除 EditorDocumentService 与 GalProject 的 dirty 字段和标记接口，删除 Workspace 的重复同步与保存 checkpoint。项目保存直接更新 ContentSession 的成功保存基线；History 的 IsDirty 绑定该会话。

独立 UndoRedoHistory 仍支持不绑定项目的游标 checkpoint，用于通用历史与独立测试；GUI 不用该游标决定保存状态。保留双版本文件计划、外部冲突、保存期间新增编辑、补偿与 RepairRequired 语义。

## 视图与验证

Preview 行复制声明元数据和运行时变量，不持有可变声明；名称、类型、默认值只读，当前值可编辑。隐藏或已被替换的行停止提交；项目关闭清除 Preview 行。声明刷新重新投影，运行时通知只更新显示、不回写。

Workspace 测试 fixture 改为绑定真实 ContentSession 并执行真实保存，不再依赖生产路径已删除的手工 dirty 标记。用实际挂载的 Preview 控件验证编辑和通知回显，并用项目 DI 验证没有 Workspace 的变量修改、引用撤销与保存重开。
