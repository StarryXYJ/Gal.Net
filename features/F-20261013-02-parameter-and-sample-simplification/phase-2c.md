# Phase 2c：共享字段编辑器宿主接入

状态：verified。Editor 38、Presentation 158、Architecture 31、Sample 截图 14，共 241 项回归通过；不代表 DTO 生成或自动绑定已经实现。

## 实现与边界

Editor 的数值、布尔、文本、选项和 JSON 参数使用完整 FieldDescriptor/ValueDescriptor，通过共享工厂保留草稿及校验。提交替换原 Entry.Parameters 字典，workspace 历史、保存和外部恢复继续观察同一路径。打开面板不再写入省略的默认值；旧 schema 的绑定不能提交到新字段投影。EditorFieldEditors 仅补充选项标签本地化，不复制值转换或保存逻辑。

设置页通过 SettingItem<T> 的 IFieldBinding 复用共享工厂，async apply/save 仍由 SettingsCoordinator 串行执行；forced/availability、动态类型化选项、失败重试和卸载取消保留。IPlayerSettingsService 提供宿主 registry，三种主题显式传入；主题保留行布局、标签、单位、可用性及错误。

删除 Integer/Float/Select 的独立参数 ViewModel/模板，设置 INumericSetting/IChoiceSetting、数值/选项 UI 转换代理，以及旧 SettingEditor/EditorTemplates 覆盖机制。不保留兼容层。

资源选择、表达式、说话人/变量补全和多行文本保留专属路径；Action/CustomSetting 保留专属行为。当前字符串编辑文档不能无损表示显式 null 字符串，适配器明确拒绝；原始 JSON 工具路径不受此限制。

## 验证

- Editor：真实 EntryListEditorControl + Semi 主题、数值编辑、workspace 撤销/重做和保存快照；大整数、自定义字符串枚举及 bool converter、省略/default/null、旧 schema 提交保护。
- Presentation：真实设置页的无效草稿、异步应用/保存失败重试、强制值、动态 enum 选项、外部刷新与卸载取消；Classic/Minimal/Paper 的工厂替换和结构回归。
- 架构门禁 31 项、Sample 截图 14 项通过；集成测试构建了修改后的 Sample 宿主。
- 只整理本阶段文件格式。Editor 使用独立输出目录，避免覆盖用户运行中的编辑器。

测试宿主修正：主题必须在构造 XAML 控件前加载。Presentation 使用官方程序集缓存会话，Dispatch 保持 PerTest 应用隔离，避免 Headless 12.0.5 的反复启动/释放竞态。失败证据、原因和边界见 L-20261014-04、05；未通过吞掉异常或跳过测试处理失败。

## 收益与下一步

同一组默认工厂进入两个正式宿主，业务提交机制独立；移除了两套通用类型编辑分支。尚有每字段投影/单字段表单与两套宿主结构模板，这是已确认的 Phase 2d 收敛对象，不继续扩大这些包装。

按 [DTO 收敛计划](dto-convergence-plan.md)，下一阶段用一个 Entry DTO 生成字段 schema/成员访问表，公共适配器承载通知与宿主提交，替换试点旧路径。不会全量设置 DTO 化或生成 module/保存/撤销代码。

本阶段生产 C#/XAML 净增 51 行（含最小绑定适配与模板，未计文档/测试）；生成器仍为下一阶段工作。框架基础的总体成本不能用这个增量代替。
