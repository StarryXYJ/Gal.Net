# 必填字符串字段不能同时声明可用的默认值

状态：confirmed
来源：custom.invoke 独立实现（2026-10-24）

现象：`CustomInvokeOptions.Argument` 声明 Required=true，并以空字符串初始化；缺少 argument 的输入被 Normalize 补成空串，仍到达处理器。

根因：SchemaGenerator 把常量属性初始化值导出为 schema 默认值；PrimitiveArgumentHelper.Normalize 优先补默认值，再检查缺失的 required。这是现有规则，不是本次通用原语需要改变的框架行为。

修正：要求作者显式提供、且空串合法的 string 字段，应明确 AllowNull=false，并不声明 schema 默认值。需要 CLR DTO 初值时在构造函数初始化，生成器不会把构造函数赋值导出为默认值。

证据：CustomActionTests.Contract_rejects_invalid_arguments_before_handler_creation 初轮缺 argument 的用例失败，调整 DTO 声明后通过；显式 null 也有拒绝回归。编译入口额外断言两个字段 IsRequired、AllowNull=false、DefaultValue=null。

适用范围：可接受空串但不接受缺省/null 的 DTO 字段。不要把 Required=true 等同于“即使有默认值也必须显式提供”，也不要为了局部字段语义修改全局 Normalize 的默认值规则。关联：docs/spec/entry-types.md、docs/manual/custom-actions.md。

复现补充：F-20261026-01 引擎重构 R3 自定义音频中，将 TrackId 从固定 bgm/vocal 改为 Required 时，保留空串属性初始化值同样导致省略参数仍通过。统一改为构造函数初始化，并对全部普通音频/语音 schema 断言 DefaultValue=null、缺省/null 拒绝；不要仅改 Required 标记。
