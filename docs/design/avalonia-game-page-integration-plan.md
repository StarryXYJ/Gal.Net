# Avalonia 游戏页面与内容对接计划

## 目标

补全官方 Avalonia Sample 与 `GalNet.Avalonia.GameView` 中可复用游戏页面的功能，并让各项页面能力能够由游戏流程和资源内容驱动。

## 维护约定

- 当前阶段使用 `进行中` 标记；未开始阶段保留完整工作项。
- 某阶段完成、验证并提交后，将其详细工作项压缩为一条完成摘要，并标记为 `已完成`。
- 每个阶段单独提交 Git，提交信息以 `feat(game-page): ...` 为前缀；文档状态变更包含在该提交中。

## Phase 1：Layer 页面层级（已完成）

已完成：JSON `.galgroup` 编译、隐藏句柄的泛型动态实例管理、Layer 的 Transform/z/五种展示模式、`layer.replace`、场景恢复，以及 Sample/编辑器预览共享渲染链路。`hide` 会删除实例并立即使句柄失效；失效操作记录诊断后安全跳过。


## Phase 2：动画原语与转场（已完成）

已完成：通用 `AnimationRequest` 与内置曲线语法糖、可序列化关键帧 `AnimationPlan`（Step/Linear/Hermite、帧事件、Replace、阻塞/跳过/batchId）、独立 `IAnimationView`、稳定状态提交和 Avalonia 实时多轨播放。Sample 已用手写 48 帧交叉淡化验证第 0 帧显示、双背景透明度轨道、结束帧隐藏与批次跳过。

补充完成：两类动画均有独立播放句柄、`Loop` 与 `animation.stop`。Loop 强制非阻塞、不可跳过；每轮事件完整重放，Layer 句柄翻译为本轮局部实例并在轮末清理，不进入存档。`AfterIteration` 与 `CompleteImmediately` 分别在轮末或跳到终点后结束。Loop 的局部 `show`/`hide` 不做静态配对校验，由作者控制，运行时始终执行轮末兜底清理。

## Phase 3：Effect 效果（已完成）

已完成：平台无关的 `effect.apply` / `effect.stop`、可动画且可存档恢复的 `EffectInstance`、Overlay/Layer 双向关联，以及由普通 Animation Plan 驱动的 `transition.blinds`。Avalonia Effect factory 自动发现并自持渲染、停止和清理逻辑；内置 CPU 对象池粒子发射器与 Layer 百叶窗 mask，Sample 第二分支已实际覆盖二者及粒子参数动画。

已完成：`EffectCatalog` 从 factory 元数据生成，向编辑器提供 effect 下拉、Scope/参数/可动画属性提示和 JSON 诊断；运行时只记录诊断，始终保持宽松执行。持续 Effect、Loop 与已提交动画值可存档恢复；`effect.stop` 立即移除逻辑 effect，粒子排空仅保留为本次运行的视觉尾迹。

## Phase 4：音频

- 接入 BGM、音效和语音的播放、停止、循环与音量控制。
- 将资源定位、播放状态和页面/流程调用完成连接。

验收：Sample 能按游戏流程切换 BGM 并播放音效或语音。

## Phase 5：视频

- 支持开场、过场和背景视频的基本播放。
- 处理结束回调、跳过、转场衔接和资源释放。

验收：Sample 可播放并跳过一段由游戏内容触发的视频。

## Phase 6：Live2D

- 接入模型显示、模型切换及基础表情/动作触发。
- 将对话和游戏流程与模型参数、动作事件连接。

验收：Sample 可通过游戏内容显示模型并触发表情或动作。
