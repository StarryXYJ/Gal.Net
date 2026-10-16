# Phase 2d：DTO schema 生成与公共成员绑定

状态：verified。Generator 7、Core 91、Editor 39、Presentation 161、Architecture 36、截图 14，合计 348 项回归通过。契约独立部署、CLI schema show 和 GameTestCase 编译通过（4 个 Group，diagnostics=[]）。

## 已实现

- Core 的 GenerateFieldSchema/Field 特性、IFieldDto 成员访问及 IFieldSchema<T> 静态 schema 协议。值处理复用 ValueDescriptor/ValueCodec/ValueValidation；不增加第二套序列化/校验规则。
- 独立 GalNet.SchemaGenerator 生成有序 FieldDescriptor、getter/setter；只读取顶层非泛型 concrete partial class 的本类标记属性。支持 camelCase/JsonPropertyName、属性 JsonConverter/JsonRequired、常量初始化默认值、required/null、范围/长度/格式、开放 EditorId 及 Step/Format/UnitKey/标签。字段、init-only、非恒定默认值、重复字段 ID/顺序等以 GNF001 定位诊断。
- 一个通用 DtoBinding，借用 DTO 并按字段建立/移除 INotifyPropertyChanged 订阅；普通 DTO 显式 Refresh。宿主可接管提交和 presence，表单继续持有草稿与取消/生命周期。已验证 CommunityToolkit 的公开 partial ObservableProperty 属性可同时标记 Field，不依赖读取另一个生成器输出。
- ForDto<T> 用生成 schema 建立参数表及 Editor 数据工厂；ScreenshotOptions 替代截图手写参数描述，复杂文件名规则保留手写。执行 module、业务代码和保存队列不生成。
- Editor 对 DTO Entry 使用整组 SchemaForm，无逐字段旧投影；共享表单行模板，提交继续替换原参数字典，进入历史/保存。DTO 初始化后才发布到已挂载面板，重新配置会释放旧绑定。

## 验证与修正

实际控件测试覆盖输入校验、两生成器协作、外部通知/显式刷新、成员值隔离、converter/null/default，以及宿主异步提交取消和退订。真实 Editor 面板同时验证原路径和 DTO 路径的撤销/重做/保存，并覆盖带非 null 约束的 DTO 在挂载后重建。

新默认值路径暴露旧 Entry 默认投影的字符串编码问题：现在用完整值 codec 保留 enum 引号；无法被字符串文档无损表达的默认值不写入 Defaults，仍保留在 FieldDescriptor，编译可使用原值。Core 回归已验证，见 L-20261014-06。

生成器引用是 Analyzer，ReferenceOutputAssembly=false；移除继承的 OutputPath/OutDir，独立契约部署目录没有生成器/Roslyn 产物。架构门禁检查构建角色；GameTestCase/Contracts 整体更新，实际 CLI 使用该 DLL。未测试所有平台/AOT，也未声称完成全部设置 DTO 化或 Entry 执行作者路径。

## 收益与限制

截图字段类型、名称约定和约束由 DTO 成员统一维护；不再单独声明 DynamicParameterTable/参数类型。Editor 从 DTO 工厂取得 schema 与成员访问，试点不再创建 SchemaEntryParameterEditorItemViewModel。模板与作者参数提交桥接也收敛，生成器和公共适配器增加初始基础设施成本；一个字段样例本身不足以证明全项目净减行。

不执行任意构造函数或默认表达式来推断 schema；无初始化值不自动成为 schema 默认值。普通成员不负责 JSON presence，宿主仍保留原始字典。当前字符串作者文档仍不支持显式 null 字符串的无损编辑；公共 DTO/JSON 协议可表示它。复杂 JSON 序列化元数据、继承 schema、递归表单、私有 backing field 转发与自动 module/持久化生成尚未实现。

推荐开发者写普通属性或公开 partial ObservableProperty 属性，给成员标记 Field；引用生成器后调用 ForDto，UI 使用一个 DtoBinding。具体例子见 docs/development.md。后续 Phase 3 集中组合入口，Phase 4 继续减少参数读取和工厂样板，Phase 5 才扩大生成器覆盖/诊断；不把这些未完成事项记为本阶段已实现。

生产 C#/XAML 净增 291 行（含生成器 123 行、公共 DTO 绑定 73 行，未计测试/文档/构建配置和生成产物）。这是初始基础设施投入，不能宣称项目总代码减少；已删除试点参数表、字段投影和重复模板/提交逻辑，后续按多个 Entry 的实际维护点评估扩展。
