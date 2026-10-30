# Editor 技术规范

Editor.Shared 维护作者数据与服务，GUI 提供面板、输入草稿与检查导航。工作版、保存基线和历史不是三套互相覆盖的项目状态。

<!-- navigation:start -->
- [Editor 项目文件操作](project-and-files.md)
- [Editor Graph 编辑](graph.md)
- [Editor Group 编辑交互](groups.md)
- [Editor 变量面板](variables.md)
- [Editor 项目历史与保存](history-and-save.md)
- [Editor 游戏本地化基础](localization.md)
- [Editor 场景目标查询](scene-targets.md)
- [Dock 面板与布局契约](dock.md)
- [外部调试状态与缓存契约](debugging.md)
<!-- navigation:end -->

## 编辑器工作流

`EditorProjectDocument` 是 UI 无关的图、Group、设置编辑聚合。资源 metadata 属于 `Assets/`；Gallery annotation 不进入 Editor document，也不存在集中式 Gallery authoring 文件。运行测试通过外部游戏调试构建正式内容并启动独立游戏进程；Editor 不再保留内嵌游戏 Engine、游戏呈现会话或运行变量副本。

GUI 的 `IEditorDocumentService`、变量定义服务和 `EditorDockFactory` 由项目 scope 持有。项目打开时加载作者 Graph/Group 一次；Group 以稳定 ID 和独立源路径登记为 `EditorGroupDocument`，项目级 `EditorGroupProjectionStore` 缓存可观察投影。Graph 节点、边、分支项直接引用作者 DTO，Group 页面以 ID 打开，页面关闭只释放订阅，不丢弃未保存内容。`EditorEntryData.Parameters` 保留原始 JSON 值；字符串仅用于控件和既有 CLI 输入适配。变量声明只保存在 Graph 的 playerVariables/saveVariables，settings 不再维护或派生变量副本。文档仓库仅接收项目路径，不消费设置模型或额外项目名称。

Editor 随发行包提供默认契约模板，新建项目不需要用户准备 DLL；留空自定义契约目录时，自动将默认契约复制到项目 `Contracts/`。新项目随后使用同一项目契约加载路径，自定义目录仅供需要自定义 schema 的项目选择。打开已有项目不会用该模板替换缺失或损坏的契约。

Editor 要求显式且唯一的 Entry 根节点，无效图不会被样例图替换或在加载时补节点。旧内嵌游戏预览已移除；旧布局中的游戏预览项作为不可用面板清理。

Editor 内容历史采用项目单栈，不随 Dock/Inspector 焦点切换；设置页首先接入显式草稿提交。GUI 的 `ProjectContentSession` 统一捕获并发布 Graph、Group 与完整设置，以实际内容与保存基线判断 dirty，后续编辑不被异步保存误标为已保存。关闭提供保存全部/放弃未保存内容/取消。作者文件事务的 manifest/备份与周期作者快照位于 `.galnet/editor/`，补偿失败禁止继续写入。Graph 基本操作由项目数据编辑记录承担，页面按文档事件同步投影；Group 创建/删除复用 Working/Saved 结构计划。分支/条目旧记录和完整 Group 草稿继续按后续阶段迁移，见 [Graph 基础编辑](graph.md)；当前语义及边界见 [Editor 项目历史与保存](history-and-save.md)。

CLI 只做参数、结果协议和退出码适配；Shared 提供原始查询、无写回校验、定向编译、严格编辑/保存、项目/Group 生命周期服务。原始访问保留未知内容，不依赖 entry DLL；有语义的操作使用项目 Contracts DLL，不回退内置。Editor 的 ProjectSchemaSession、catalog 及文档消费者由项目 scope 持有。不提供项目目录删除功能；最近项目列表仅支持移除记录。当前接口及范围见 [CLI README](../../../src/Editor/GalNet.Editor.Headless/README.md)。

`EditorAssetManager`、`AssetCatalogService` 与 `GameExportService` 共享 Editor 组合根的 catalog。导出把 Graph/Group 与资源写入基础/剧情 PAK，把内部 JSON 和内置文本分别封装为 game.pak、localization.pak，再将本体装入 `.galpak`；安装目录的 I18n 仅作为外置翻译入口。

`EditorWorkspaceViewModel` 保留可观察 UI 状态、命令编排、history checkpoint 和自动保存触发。`GraphSelectionState` 维护节点/边选择与 `IsSelected` 一致性；`EditorWorkspacePersistence` 组合 repository、document service、mapper 与 project service 完成图的投影加载和保存。Graph 集合以只读可观察集合公开，只有作者操作可以改变文档成员；Group 作者操作返回稳定 Entry ID，由界面解析投影。字段工厂直接查询变量声明，不通过 Workspace。GUI 与 Headless 共用 `GraphEditRules` 的名称、坐标与连接规则，空分支没有出口。内置 Editor 命令由单一 handler 入口分派，Graph、Entry、Variable 和 Project 实现按领域分文件维护。

`Shell.EditorLayoutSession` 拥有布局恢复、命名布局和延迟持久化；`EditorMenuBuilder` 构建菜单，页面负责绑定与交互编排。`ProjectContentSession` 统一协调事务，草稿参与者由 `ProjectDraftRegistry` 管理，保存快照与内存内容比较由 `ProjectContentBaseline` 管理。比较不会为了读取 dirty 反复序列化或克隆整个项目，也不依赖历史游标或通知是否遗漏。

外部调试的 `ProjectDebugPreparation` 负责保存、构建与监听准备；`ProjectContentBuilder` 同时返回实际编译内容的 SourceMap 和作者指纹。`ExternalGameDebugClient` 管理连接和请求，`DebugApplicationProcess` 管理启动、退出和输出监控；断开后继续排空进程输出的行为保留。

Editor 扩展 registry 通过非泛型 `IDockPanelContribution` 和 `IInspectorControlContribution` 保存异构贡献，`object` 只存在于该类型擦除边界。新 contribution 应优先继承 `DockPanelContributionBase<TViewModel>`、`DockPanelContributionBase<TViewModel, TParameter>` 或 `InspectorControlContributionBase<TDockViewModel, TInspectorViewModel>`，由 bridge 集中校验 parameter、dock ViewModel 和 inspector ViewModel 类型；错误类型会在贡献边界报告预期与实际类型。`IServiceProvider` 继续表示全局或当前项目 scope，view 继续返回 `object` 以避免 Editor.Abstraction 引用 Avalonia。内置 panel 和 inspector contribution 均使用这些强类型入口。
