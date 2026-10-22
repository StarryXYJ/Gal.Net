# P4b：图集配置、预览与联动字段

日期：2026-10-22
状态：verified（P4 作者流程完成；整项 feature 最终验收仍待 P5）。

## 实现范围

- Assets 菜单从当前/选中目录创建图集，按稳定顺序选择显式默认图，省略 dimensions 时从文件名推断。描述文件及 sidecar 通过现有 CreateFile 事务，图片仍通过现有 Import 事务创建 sidecar；扫描不写文件。
- 新增属性面板：相对目录、默认图、维度 key/label、添加/移除/上移/重新推断、扫描诊断、原始 JSON 修复。维度身份变化有引用影响提示，不迁移剧情引用。
- 配置编辑直接投影项目拥有的 ProjectTextBuffer。应用配置形成一次文件历史；Save All 也可提交有效草稿，关闭属性面板不丢草稿。扩展名或 sidecar 声明为 image-variants 的文本都在提交及 Save All 验证，坏 default、维度和目录被拒绝，保留原文。项目释放后的旧缓冲只读。
- 预览列出所有轴的全部值，保留请求及保护维度，复用纯 resolver 显示实际 bitmap、结果原因及候选链。smile_skirt→weap→smile 保留 skirt。刷新/语言切换/预览不写历史；定义改变使请求失效时诊断，显式重置才回默认。图片加载及旧预览行按代次拒绝迟到发布，释放时解绑并释放 bitmap。
- show 初始选择使用多轴选择与默认继承；保留未知选择键及 JSON 修复。select 的维度/值跟随对象及维度联动，手动字符串入口保留已有未知值。来源不唯一时不生成确定选项，后台准备与作者/资源变代刷新控件并拒绝旧控件提交。
- 场景查询读取该代捕获的图集工作字节：Apply/Undo/Redo 后无需先保存到磁盘即可获得新定义；尚未提交的描述草稿只用于属性面板预览。资源读取和 Core 解析规则不变，没有把文件系统逻辑加入 Core。
- 冷启动选择 lease 区分正常资源准备完成与作者/资源代次变化；同一快照的准备完成可刷新结果，等待期间的内容变更/刷新/关闭仍使其过期。新增专门受控加载测试，并记录 agent lesson。
- 四语新增 18 个 UI 键；同步 editor-files、editor-scene-targets 规范和图集操作手册。

## 验证

最终日志与 TRX：`artifacts/image-variants-p4b/`；完整解决方案日志位于 `artifacts/image-variants-p4b-solution.log`。

- 新增 Shared 用例 9 项：稳定推断/default/全轴值、无写入、无效默认图、未知/重复 JSON、目录越界、缺 sidecar、跨扩展名重名、描述路径不能绕出 Assets。
- 新增 Editor 用例 3 项：实际挂载的创建→配置→预览→历史→Save All→重开；关闭面板仍校验坏草稿、工作定义与旧控件失效、初始选择、未知 JSON 保留；冷启动准备与作者修改失效。
- 初次新增扫描 7 项及挂载 2 项通过，补充边界和初始选择后重跑。首次完整 Editor 回归发现新菜单使旧测试按两项定位失效；改为通过命令识别创建菜单并确认图集入口。
- 初始选择测试在 history.Clear 后持有旧控件，被正确拒绝；调整为清历史后再获取控件。失败测试未关闭项目引发后续 dispatcher 回调访问已释放服务，补充 finally 关闭项目。上述相关五项定向重跑全部通过。
- 完整解决方案隔离构建通过（首次三个既有 Avalonia 警告，最终增量复核 0 警告/0 错误）。Editor.Shared 全量 393 项通过，Architecture 47 项通过。
- 最终 Editor 全量：257 通过、3 失败（共 260）；两项为 P4a 已记录的 Group 滚动尾部与属性行宽布局断言，另一项为未修改的 `TimelineSelectionNeverNormalizesOrRewritesIncompleteOrInvalidAuthorJson` 在 HeadlessUnitTestSession.DisposeAsync 出现 NullReferenceException。其七个参数用例与本阶段作者流程等共 12 项单独重跑通过；排除两项已知布局失败后，其余 Editor 258 项完整重跑全部通过，未再出现释放异常，未据此宣称最初全量全绿。
- 最终相关回归合计 698 项通过：Editor.Shared 393、Editor 258（排除上述两个已知布局失败）、Architecture 47；新增 Shared 9 项、Editor 3 项全部包含在通过结果中。实际挂载测试还等待真实 bitmap 解码完成并确认预览非空，验证四语刷新不写作者缓冲及空维度对象的保守诊断。
- 本阶段源码与测试 whitespace verify、四语资源 JSON 解析/键集合（各 952 keys）、相关文档相对链接检查通过。提交仅包含本阶段文件；另一 feature 的 phase-plan 改动保留。

构建/测试使用 `--artifacts-path artifacts/image-variants-p4b/build`，不写正在运行的 Editor 默认输出，不关闭用户程序。测试命令使用 `--logger trx` 留存证据。

## 剩余范围

本阶段不增加交叉淡化、图片叠加、Timeline 差分事件或全项目引用迁移。大型目录扫描目前是同步作者索引，未宣称无限规模性能；实际规模、内存、打包脱离目录运行及原生人工验收继续在 P5 验证。P4 整体通过不等于本 feature 全部验收完成。
