# P2 — 唯一 Graph 资源化与章节参数

状态：`verified`，2026-10-28；P1 提交为 `a3ae72bd`。

## 行为

- 保留一个项目一张图。`content.json` 记录唯一保留 Graph ID，Sample 自动读取；没有 Graph 列表、默认图设置、显式选图 API 或跨图存档字段。
- Graph/Group 都进入基础 PAK，不再发行 loose `Graph/**`。Group ID 从稳定节点 ID 派生，图 payload 指向该资源 ID；Graph 自身不再每次加载生成随机身份。
- CompiledStoryResources 同时服务导出和开发 provider，开发编译目录经 CompiledStoryAssetProvider 按同一 ID 寻址；缺少编译正文明确失败。安装加载不回退到 loose Graph。资源查询与内容加载使用统一的安装 provider 工厂。
- `FieldAttribute.ResourceTypeId` / FieldDescriptor 明确标记资源引用，生成 schema、嵌套结构及描述项复制保留它。编译 Group 按 primitive schema 生成依赖，不猜字符串；未知/opaque/custom.invoke 保留不完整标记。开发适配没有 schema 时不伪造非空 Group 的完整依赖。
- Group 作者 DTO 增加可选 storyPackage，运行节点与依赖同步携带；本阶段仍全部进入 base，实际拆包和缺席跳过交由 P3。
- GraphLoader 保留 player/save 声明。默认 Avalonia、Headless 及 GameEngine 自建 runtime 使用声明默认值；新 Run 的 save 初始化不从上次玩家服务缓存继承，已存 player 值保留。
- NewGameVariables 校验并复制 save-only 初始值，GameSession 在 Prepare 后、Begin 前应用；错误参数先于旧 Run 释放被拒绝，读档不使用新游戏参数。GameRun 拒绝对非新准备状态重复执行该初始化。
- AvaloniaGameSession 实现可选 IParameterizedGameSessionService；默认标题 VM 有 StartNewGameWithVariablesCommand，复用既有 Loading/Prepare/Begin 与页面替换能力。

## 验证

| 检查 | 结果 |
| --- | --- |
| 完整 Release 解决方案构建，`--no-restore -m:1 -nodeReuse:false -p:UseSharedCompilation=false` | 通过，最终完整构建 202 warning / 0 error |
| Editor.Shared 全套（中途快照） | 476/476 通过 |
| 最终 StoryResourceCompiler / ExportPreparation / ProjectContractPipeline / ImageVariantsResource 回归 | 23/23 通过 |
| GameHost 全套 | 8/8 通过，包括首条指令前参数、默认值重建、存档恢复与参数复制/拒绝 |
| Runtime 全套 | 123/123 通过 |
| GameInstallation / GameLocalizationLoading / SampleExtensibility 集成 | 18/18 通过 |
| 反射关闭 verification/ReflectionOff | P1 smoke 仍通过，新增 `P2_STORY_REFLECTION_OFF_OK graph=galnet:graph:main groups=1 saveDefault=1` |

导出集成实际检查 content.json、无 loose Graph、PAK 内 graph/group ID 获取、开发编译目录同 ID 获取、删除 PAK 后即使伪造 loose Graph 也不回退。旧直接 ZIP Group 路径断言和手写安装夹具已迁移至新格式，保留原自定义契约及文本检查。

## 实施边界

P2 没有实际剔除未引用 Assets，也没有把命名剧情 Group 放进独立包；这属于 P3。override 映射尚未应用，资源矩阵尚未创建。Gallery 保留旧基础导出，没有补丁功能。

自定义游戏资源目录需要显式注册 graph/group 对应 DependentAssetMeta；内置目录已经提供。测试新增到既有 Storage 工程的引用，对应锁文件已更新，未新增第三方依赖。

正式规范同步 assets.md / file-formats.md，Sample 手册增加初始章节参数用法。工作区并行的发布/AOT 改动不属于本阶段，提交按文件审计，不打包无关变动。
