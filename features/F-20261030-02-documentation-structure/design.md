# 正式文档与 Wiki 信息架构设计

状态：拟议结构，待后续实施。当前事实与目标结构分开记录。

## 1. 当前问题与依据

截至 2026-10-30，`docs/` 有 36 篇 Markdown：16 篇 spec、17 篇 manual，以及索引、理念、术语表。Wiki 只有默认 `Home.md`。

当前存在这些结构问题：

- manual 混合作者操作、Sample 扩展、原生后端、开发者性能测量和引擎发布。
- spec 全部平铺；架构、运行时、文件格式、Editor 与共享 UI 不易区分。
- `customize-avalonia-sample.md` 同时包含玩家发行、服务替换、主题、DTO 表单和对白历史。
- `external-game-debugging.md` 同时描述 Editor 操作、游戏接入和内部状态语义。
- `dock-design.md` 混合当前契约、扩展步骤和排障；`release-packaging.md` 混合 CI、引擎发版和游戏合包。
- `docs/README.md` 未完整列出已有页面；根 README 的仓库地图仍有 `src/Launcher/`，当前 Sample 已有 CrossPlatform 和 Player 目录。
- CrossPlatform README 仍称其为未接入游戏逻辑的模板基线；此描述需要对照当前实现重新核查，不能直接作为 Wiki 发布依据。

这些是本次盘点发现的结构问题和待核查项，不代表已经完成全文正确性审查。

## 2. 读者与文档职责

| 读者或任务 | 入口 | 应得到的内容 |
| --- | --- | --- |
| 第一次接触项目 | quick-start | 选择路径、准备环境、运行 Sample、做出可运行的小项目 |
| 使用 Editor 创作 | manual/editor | 界面位置、操作步骤、保存行为、预览、导出及常见问题 |
| 使用默认玩家应用 | manual/sample | 内容加载、启动、设置、存档、历史、Gallery 与平台限制 |
| 定制自己的游戏宿主 | manual/extensions | 配置、页面、主题、契约、action、变量、参数表单和平台适配 |
| 分发游戏或发布引擎 | manual/publishing | 内容与应用组合、构建参数、发行产物和发布操作 |
| 参与项目开发 | manual/development | 构建测试、CLI、性能测量和文档维护 |
| 查询实现规则 | spec | 当前模块职责、状态/生命周期、格式、行为和不变量 |

`manual` 的职责从原先以二次开发为主扩展为正式操作手册；实施时应同步修正 `docs/philosophy.md`、根 README 和相关 agent 文档入口中的旧定义。

`spec` 中的“架构设计”只表示已经落地的当前架构。候选方案、未来设计与实施计划继续放在 `features/`。

## 3. 目标目录

下面是完整目标信息架构，不要求一次创建所有文件。已有内容先归位；新页面只有经过核查并有足够正文时才进入正式导航，不生成空壳页。

每个分类的 `README.md` 承担分类说明、阅读顺序及下级入口。路径使用稳定英文名，页面标题和导航使用中文；不在文件名里编码排序数字。

```text
docs/
├─ README.md                            文档首页：按读者任务导航
├─ philosophy.md                        设计理念与高层架构概览
├─ glossary.md                          统一术语表
├─ quick-start/
│  ├─ README.md                         快速开始：选择使用或开发路径
│  ├─ requirements.md                   环境、SDK、依赖及平台前提
│  ├─ run-sample.md                     运行示例，区分发行包与源码路径
│  ├─ first-project.md                  创建→剧情→预览→导出→运行
│  └─ build-from-source.md              首次源码构建的最短闭环
├─ manual/
│  ├─ README.md                         操作手册总览
│  ├─ editor/
│  │  ├─ README.md                      Editor 使用路线
│  │  ├─ projects-and-workspace.md      新建/打开、面板、Inspector、保存
│  │  ├─ story-editing.md               Graph、Group、分支、变量和角色
│  │  ├─ resources.md                   导入、资源身份、引用与管理
│  │  ├─ image-variants.md              图片差分图集
│  │  ├─ audio.md                       音乐轮播、Track/Bus 与音频效果
│  │  ├─ default-visuals.md             演出、动画与默认视觉预设
│  │  ├─ localization.md               游戏文本、语言、翻译交换与检查
│  │  ├─ preview-and-debugging.md       预览、外部游戏调试与问题定位
│  │  ├─ content-packaging.md           内容构建、分包、导出及资源补丁
│  │  └─ external-tools.md              CLI/agent 创作及外部文件热重载
│  ├─ sample/
│  │  ├─ README.md                      Sample 类型及使用路线
│  │  ├─ startup-and-content.md         启动、加载内容与路径配置
│  │  ├─ gameplay-and-settings.md       推进、自动/快进、输入与玩家设置
│  │  ├─ saves-history-and-gallery.md   存档、已读、对白历史与 Gallery
│  │  └─ platform-support.md            各平台前提、已验收范围与限制
│  ├─ extensions/
│  │  ├─ README.md                      Sample 与宿主扩展路线
│  │  ├─ customize-sample.md            工程选择、组合入口及最小改造闭环
│  │  ├─ pages-and-themes.md            页面注册、导航、主题及窗口配色
│  │  ├─ actions-and-contracts.md       项目契约、primitive/composite/action
│  │  ├─ settings-and-variables.md      自有设置、变量桥接与会话绑定
│  │  ├─ parameter-editors.md           DTO 字段提示与参数编辑控件
│  │  ├─ external-debugging.md          游戏宿主接入外部调试协议
│  │  ├─ new-presentation-layer.md      新 UI 表现层的最小接入流程
│  │  ├─ media-and-platform-backends.md 媒体/音频后端与平台生命周期适配
│  │  └─ editor-panels.md               自定义 Editor 面板及 Dock 接入
│  ├─ publishing/
│  │  ├─ README.md                      选择游戏分发或引擎发布路径
│  │  ├─ game-distribution.md           运行包+内容、compose/build-compose
│  │  ├─ engine-release.md              引擎/NuGet/Editor/Sample 发布与 tag
│  │  ├─ ready-to-run.md                R2R 操作、契约身份与适用边界
│  │  └─ native-aot.md                  AOT 操作、元数据与已验证边界
│  └─ development/
│     ├─ README.md                      贡献者阅读路线
│     ├─ build-and-test.md              完整构建、测试、格式与本地 CI
│     ├─ cli.md                         CLI 入口、查询/修改边界与帮助发现
│     ├─ cpu-benchmark.md               启动基线、埋点及调用栈测量
│     └─ documentation.md               文档职责、核查规则与 Wiki 同步操作
└─ spec/
   ├─ README.md                         技术规范总览与模块关系
   ├─ architecture/
   │  ├─ README.md                      当前整体架构及依赖图
   │  ├─ modules.md                     程序集、依赖方向、职责与测试边界
   │  ├─ lifecycles.md                  组合根、scope、所有权与释放顺序
   │  └─ diagnostics.md                 CPU 事件及可选诊断依赖契约
   ├─ runtime/
   │  ├─ README.md                      执行模型概览
   │  ├─ execution.md                   Engine、batch、分支与活动实例
   │  ├─ entries.md                     schema、target profile 与执行注册
   │  └─ state-and-save.md              状态、稳定快照、存档与已读
   ├─ content/
   │  ├─ README.md                      作者内容→编译→安装→加载概览
   │  ├─ assets.md                      资源身份、provider、租约与覆盖
   │  ├─ file-formats.md                项目/PAK/galpak 格式及版本契约
   │  └─ localization.md                UI/游戏文本边界、回退与覆盖
   ├─ presentation/
   │  ├─ README.md                      呈现端口与默认实现边界
   │  ├─ game-host.md                   会话、玩家规则与宿主组合
   │  ├─ game-view.md                   页面、导航、主题与共享字段控件
   │  └─ media.md                       音频/视频/渲染后端契约与资源所有权
   └─ editor/
      ├─ README.md                      编辑器模块与作者数据流
      ├─ project-and-files.md           项目 scope、文件操作与资源面板
      ├─ graph.md                       Graph 编辑行为
      ├─ groups.md                      Group、字段、Timeline 与交互
      ├─ variables.md                   变量声明、草稿与引用检查
      ├─ history-and-save.md            历史、事务、dirty、保存与恢复
      ├─ localization.md                文本身份、作者查询与翻译事务
      ├─ scene-targets.md               场景/音频候选、能力与引用分析
      └─ dock.md                        面板生命周期、布局与持久化
```

图片等共享附件采用 `docs/_assets/<主题>/`；导航元数据采用 `docs/navigation.json`。两者都不是读者目录树的正文分类，实施有实际需求时再增加。

## 4. 分类与拆分规则

1. Quick Start 只完成一个最小成功流程，提供后续链接，不重复完整手册。缺少可用发行包时，只展示已验证的源码路径，不假设读者能下载尚未发布的产物。
2. Editor 手册回答“如何创作”；Sample 手册回答“默认应用如何运行和使用”；扩展手册回答“如何编写和组合自己的功能”。例如音频剧情操作归 Editor，设备回调接入归扩展，后端契约归 spec。
3. 内容导出、剧情包和资源补丁归 Editor 的 content-packaging；把内容与应用合成可分发产品归 game-distribution；发布引擎本身归 engine-release。
4. manual 写前提、步骤、预期结果和排障；spec 写职责、接口、状态、不变量及限制。同一个事实指定一个权威页面，其他页面简述并链接。
5. philosophy 保留动机和分层原则；architecture 展示当前真实程序集与依赖。术语只在 glossary 统一定义，具体契约链接回所属 spec。
6. 页面按稳定主题拆分，不按每个接口或每个 feature 建页。拆页后内容不足时可先保留为分类页中的章节，不为了匹配目标树制造短空文档。
7. 平台支持状态的读者入口统一为 sample/platform-support；其他手册只写本操作的前提并链接。不得把跨平台项目存在、交叉编译成功或 Headless 自检通过描述为图形/媒体/实机已经完整支持。
8. CLI 参数以实际路由注册表和 `help --json` 为核查来源。手册覆盖常用任务，不复制一份容易过时的完整命令数据库。

## 5. 现有 36 篇正式文档的迁移归属

以下路径均相对 `docs/`。本表表示归属，迁移前仍须核查与去重；不是直接原文复制清单。

| 当前路径 | 目标路径或拆分归属 |
| --- | --- |
| README.md | README.md：重写入口，分类顺序由统一导航定义 |
| philosophy.md | philosophy.md：保留，核查高层图及更新 manual 定义 |
| glossary.md | glossary.md：保留，逐项核查类型和术语 |
| manual/audio-playback.md | manual/editor/audio.md：重复、轮播与停止操作 |
| manual/audio-mixing.md | manual/editor/audio.md：创作流程；内部契约归 spec/presentation/media.md |
| manual/content-packaging.md | manual/editor/content-packaging.md |
| manual/cpu-benchmark.md | manual/development/cpu-benchmark.md |
| manual/cross-platform-backends.md | manual/extensions/media-and-platform-backends.md；用户可见支持范围归 manual/sample/platform-support.md |
| manual/custom-actions.md | manual/extensions/actions-and-contracts.md |
| manual/customize-avalonia-sample.md | manual/extensions/customize-sample.md、pages-and-themes.md、settings-and-variables.md、parameter-editors.md；使用操作归 manual/sample；发行参数归 manual/publishing |
| manual/default-visuals.md | manual/editor/default-visuals.md |
| manual/editor-authoring-workspace.md | manual/editor/projects-and-workspace.md；DTO 扩展章节归 manual/extensions/parameter-editors.md |
| manual/external-game-debugging.md | manual/editor/preview-and-debugging.md：使用；manual/extensions/external-debugging.md：接入；内部语义归对应 runtime/editor spec |
| manual/external-project-authoring.md | manual/editor/external-tools.md；CLI 通用入口归 manual/development/cli.md |
| manual/game-localization.md | manual/editor/localization.md |
| manual/image-variants.md | manual/editor/image-variants.md |
| manual/native-aot.md | manual/publishing/native-aot.md |
| manual/new-presentation-layer.md | manual/extensions/new-presentation-layer.md |
| manual/ready-to-run.md | manual/publishing/ready-to-run.md |
| manual/release-packaging.md | manual/publishing/engine-release.md、game-distribution.md；CI 操作归 manual/development/build-and-test.md；后端操作归对应扩展手册 |
| spec/architecture.md | spec/architecture/README.md、modules.md、lifecycles.md；宿主契约归 spec/presentation/game-host.md |
| spec/assets.md | spec/content/assets.md；后端所有权细节归 spec/presentation/media.md |
| spec/control.md | spec/presentation/game-view.md、game-host.md；Editor 专属字段交互归 spec/editor/groups.md |
| spec/cpu-benchmark.md | spec/architecture/diagnostics.md |
| spec/dock-design.md | spec/editor/dock.md：已实现机制；添加面板步骤归 manual/extensions/editor-panels.md；旧候选方案留历史 |
| spec/editor-files.md | spec/editor/project-and-files.md |
| spec/editor-game-localization.md | spec/editor/localization.md |
| spec/editor-graph.md | spec/editor/graph.md |
| spec/editor-groups.md | spec/editor/groups.md |
| spec/editor-history-and-save.md | spec/editor/history-and-save.md |
| spec/editor-scene-targets.md | spec/editor/scene-targets.md |
| spec/editor-variables.md | spec/editor/variables.md |
| spec/entry-types.md | spec/runtime/entries.md |
| spec/file-formats.md | spec/content/file-formats.md |
| spec/i18n.md | spec/content/localization.md |
| spec/runtime.md | spec/runtime/README.md、execution.md、state-and-save.md；宿主专属职责归 spec/presentation/game-host.md |

迁移中补充目前缺失的 Quick Start、Sample 使用、资源操作和剧情创作说明。这些页面的内容须从当前 UI、源码、测试、命令实际输出及示例工程提炼，不能仅从 feature 计划拼接。

## 6. 仓库内其他文档的归属

- 根 README：项目说明、环境摘要、最短启动和正式文档入口；完整构建/发布步骤收敛到 docs。
- GameTestCase README：保留与样例工程相关的目录、内容和运行入口，通用概念链接正式手册。
- scripts README 与 scripts/ci README：保留脚本清单、入口及维护说明；通用教程链接正式文档。
- CLI README：保留组件职责与帮助发现入口；操作教程归 manual/development/cli。
- Sample.Contracts、Contracts.Builtins README：保留模块本地说明和最小示例，契约规则链接 spec/runtime/entries。
- CrossPlatform README：保留该子工程的构建入口、项目布局和本地前提；正式平台状态链接 sample/platform-support。
- Themes、Miniaudio/native README：保留局部资产或原生构建细节，不原样镜像到 Wiki。
- skills/galnet-project-authoring：属于随产品使用的 skill；正式手册说明其安装和使用，skill 自身仍保留原位置。
- features：保留需求、候选方案、实施证据、总结和剩余验收；只将已验证当前事实提炼到 docs，不批量发布历史到 Wiki。
- .agents：保留上下文、lesson 与流程 skill，不进入 Wiki 正文。

重写后应避免各 README 继续复制同一套长教程。旧 feature 的原始执行记录不改写历史；对其中因搬迁失效的正式文档链接做机械修复，必要时在 feature 索引/总结提供现行入口。

## 7. Wiki 的分类与完整正文

### 7.1 阅读结构一致，物理存储适配 Wiki

Wiki 不是只做索引：每篇正式正文都会发布到 Wiki，首页和侧栏提供完整入口。

GitHub Wiki 的默认 Pages 列表不会自动把 docs 的嵌套目录展示为相同的目录树。因此采用稳定页面 ID 的平铺文件发布，通过分类页及 `_Sidebar.md` 的分组、缩进链接表达层级。不同页面 ID 不会因重复使用 README.md 而冲突。

拟议映射示例：

| 文档源 | Wiki 文件 / 页面 ID |
| --- | --- |
| docs/README.md | Home.md / Home |
| docs/philosophy.md | Philosophy.md / Philosophy |
| docs/glossary.md | Glossary.md / Glossary |
| docs/quick-start/README.md | Quick-Start.md / Quick-Start |
| docs/manual/README.md | Manual.md / Manual |
| docs/manual/editor/README.md | Manual-Editor.md / Manual-Editor |
| docs/manual/editor/projects-and-workspace.md | Manual-Editor-Workspace.md / Manual-Editor-Workspace |
| docs/manual/extensions/README.md | Manual-Extensions.md / Manual-Extensions |
| docs/spec/architecture/modules.md | Spec-Architecture-Modules.md / Spec-Architecture-Modules |

读者在两处看到相同的分类名、父子关系和排序。例如 Wiki 首页及侧栏都按以下顺序展示：

```text
文档首页
├─ 快速开始
├─ 操作手册
│  ├─ Editor 使用
│  ├─ Sample 使用
│  ├─ Sample 与宿主扩展
│  ├─ 构建与发布
│  └─ 项目开发
├─ 技术规范
│  ├─ 架构设计
│  ├─ 运行时
│  ├─ 内容与资源
│  ├─ 呈现与宿主
│  └─ 编辑器
├─ 设计理念
└─ 术语表
```

### 7.2 统一导航数据

拟采用一个轻量 `docs/navigation.json` 定义分类树、中文标题、页面源路径、顺序与稳定 Wiki ID，不再手写第二套目录。

- 每个正式页面恰好有一个分类归属；其他入口通过交叉链接抵达。
- 分类入口也是正常页面，使用 README.md 承载说明，不是无内容占位。
- Wiki ID 明确记录；以后调整本地路径时可保持已有 Wiki URL。
- 页面 H1 与导航标题的一致性在生成时检查；生成分类索引时只替换指定的导航区块，保留人工正文。
- 构建校验未列出的正式 Markdown、重复 ID、源文件缺失及不合法路径。`_assets` 和导航配置自身不计入正文页面。

### 7.3 同步的目标行为

```text
当前代码/测试/验证证据
         ↓ 人工核查并修正文档
docs/ 正文 + navigation.json
         ↓ 生成、转换链接、校验
wiki/ 完整正文 + 分类页 + _Sidebar.md + _Footer.md
         ↓ 独立 Git 提交与推送
GitHub Wiki
```

正文源文件只在 docs 编辑。Wiki 页脚展示“由 docs 生成”、源码文档链接和生成依据的主仓库 commit；这个 commit 表示发布来源，不等于所有命令和平台在该 commit 上都重新验证过。

生成器需处理：相对 Markdown 链接与 fragment、分类 README 链接、附件路径，以及指向源码/脚本/feature 的链接。已发布正文间互链指向 Wiki；源码和历史链接回 GitHub 主仓库对应来源提交。Mermaid 与代码块保留。实施时验证 Wiki 页面标题、中文标题锚点及图片的实际渲染。

本地同步应先生成到暂存目录，检查通过才更新 wiki 工作树；遇到 Wiki 有人工改动或远程分叉应报告冲突，不直接覆盖。只清理生成清单追踪的旧页面和附件，不扫描删除其他文件。

第一次先提供可审阅的本地生成结果；CI 自动发布在本地验证通过后接入。自动发布可由 docs/导航/生成器变更触发，同时提供手动入口，并单独检查 Wiki 仓库的写入凭据。不能假定主仓库默认 Actions token 能写另一个 Wiki Git 仓库；权限不可用时仍可本地生成和单独推送。

## 8. 内容清理与核查方法

核查每个主题时，在本 feature 中记录“源页面、目标页面、实现/测试入口、已修正问题、验证范围与未决项”。状态区分未核查、已核查需修改、已修订和已验证，不能仅移动文件就标记完成。

优先检查高变动内容：Sample.Player/CrossPlatform 共享宿主、内容导出与安装格式、CLI/合包入口、音视频后端、平台验收边界、资源句柄/异步加载生命周期，以及 Editor 实际交互。

实施核查的证据优先级：当前源码与适用测试、可追溯的实际运行记录；已核实的 feature 总结辅助定位变更。agent-knowledge、旧 spec 和旧 feature 设计只作为线索，不单独证明事实。

页面正文应说清适用对象和前提。命令写出工作目录、必要依赖与成功标志；示例尽量使用仓库现有最小工程，不虚构不存在的发行包、参数或 API。统一排查失效路径、已删除类型、过时版本、相互矛盾的说明和未完成平台承诺。

删除或合并旧页前，先确认有效信息已有新归属。保持正式路径的内部引用可解析；对已对外引用且值得保留的入口，可保留短跳转说明并安排后续淘汰，不保留另一份完整正文。

## 9. 后续实施顺序与退出条件

| 阶段 | 工作 | 退出条件 |
| --- | --- | --- |
| 1. 主题核查 | 盘点每页事实与源码/测试，形成差异表；优先核查快速开始所需路径 | 所有现有页面有归属，高风险与未知项显式列出 |
| 2. 结构迁移与正文整理 | 按主题移动、拆分、合并、补充新手路径，修复 README/代码/脚本/skill/feature 中受影响的引用 | 无空壳或失效引用；新内容有证据；未验收边界保留 |
| 3. Wiki 本地生成 | 实现导航配置、生成器、链接转换、管理清单和本地预览 | docs 与 Wiki 分类/正文一致；幂等；不会覆盖未管理内容或人工改动 |
| 4. 发布与日常维护 | 核查凭据、初次推送；根据实际权限接入 Actions，编写维护说明 | 线上可读、链接及附件正常；新增/重命名/删除页面都有可验证的同步路径 |

本次只交付设计，不移动现有正文、不创建空目录、不更改业务代码，也不声称已经建立自动同步。

## 10. 验收重点

- 新读者能在首页区分“运行 Sample”“使用 Editor 做游戏”“定制宿主”“参与开发”。
- Quick Start 覆盖一条实际验证过的最小流程，并明确平台前提。
- 当前 36 篇正式文档全部有迁移归属，有效内容无意外丢失，重复正文合并。
- manual 与 spec 职责清楚，术语一致，源码/脚本和文档内部链接有效。
- 平台和功能的实现、构建通过、运行验收三者不混为一谈。
- Wiki 有完整正文、分类首页和树形侧栏，导航与本地一致；技术页面之间不绕回 docs 才能继续阅读。
- 重复生成不产生无意义差异；手工改动、远程冲突和缺少发布权限有明确反馈。
