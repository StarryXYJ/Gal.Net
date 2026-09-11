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

## Phase 3：Effect 效果（进行中）

### 目标与边界

Phase 2 已提供 Layer 的出现、隐藏、移动、透明度、震动、闪烁与相对动画；这些能力不再在 Effect 系统中重复实现。Phase 3 建立的是可注册、可持久化、可动画化的视觉效果系统，用于粒子、遮罩、滤镜和其他不能仅靠改变 Layer 数值属性表达的效果。

渲染宿主分为两类，但游戏流程只使用一套 `effect.apply` / `effect.stop` 入口：

| Scope | 目标 | Avalonia 渲染位置 | 典型效果 |
|---|---|---|---|
| `Overlay` | 整个游戏场景 | `SceneLayerHost` 上方、对话/选择 UI 下方 | 雪、雨、全屏闪光、全局调色 |
| `Layer` | 一个有效的 `layerHandleId` | 对应 Layer 的视觉装饰器或 mask 内 | 百叶窗 mask、局部调色、描边、局部粒子 |

百叶窗等效果属于 `Layer` Effect；它接收目标 Layer 的视觉输入并控制其 mask，不复制图像、不改变 Layer 的基础 Transform，也不引入第二套转场运行时接口。

### 注册、数据与动画模型

当前首个增量直接以 `effectId` 和结构化 JSON 参数驱动宿主；之后可在不改变运行时协议的前提下加入独立于 `EntryRegistry` 的 `EffectCatalog`。Catalog 届时声明：

- `effectId` 与 `scope`；
- 参数 schema、默认值和资源参数类型；
- 可动画的浮点属性及其合法范围；
- 对应的 Overlay 或 Layer 渲染器工厂；
- 正常停止时是否需要收尾（drain）以及收尾完成的判定。

`effect.apply` 的请求数据为 `EffectRequest` / `EffectInstance`：`effectId`、`instanceId`、可选 `targetHandleId` 与结构化 JSON `parameters`。Effect 本身没有内建时长、阻塞或跳过选项：所有时间推进都由普通 `animate` / `animation.play` 描述，或由作者显式发出 `effect.stop(instanceId)`。`targetHandleId` 对 Overlay 为空，对 Layer 必填；运行时验证目标 Layer，并同时维护 `EffectInstance.TargetHandleId` 与 `Layer.EffectInstanceIds` 的双向关联。

每个活跃效果在 Runtime 中是 `EffectInstance : AnimatableSceneInstance`，以 `instanceId` 为稳定动画句柄。效果参数分为两组：

- 资源、枚举、布尔等静态启动参数，例如粒子贴图；
- 声明为可动画的有限浮点属性，例如 `emissionRate`、`particle.initialVelocityX/Y`、`particle.noiseStrength`、`particle.scale`、`opacity`、`mask.progress`、`color.gradeIntensity`。

`animate` 与 `animation.play` 的目标校验从固定的 `Layer.AnimationProperties` 泛化为目标 `AnimatableSceneInstance` 的属性声明，因此效果自动获得现有 Replace / Additive、曲线、循环、停止和批次跳过语义；不为 Effect 复制一套动画系统。

### 生命周期与推进语义

`effect.stop` 只是把停止请求交给宿主 Effect；Runtime 立即移除可寻址实例和存档中的活跃描述，具体视觉如何收尾由 Effect 自己决定。粒子发射器收到停止后将发射率归零，并保留已租用粒子直到自然回收；百叶窗 mask 则立即解除并释放自己的 Layer 绑定。这样不会把每种效果的 drain 策略硬编码进游戏流程。

需要固定时长、阻塞或跳过的效果时，非原语转场编译为一个普通 Animation Plan：第 0 帧创建 Layer/Effect，轨道动画化 Effect 参数，结束帧 `effect.stop`。Plan 原有的阻塞、跳过和 batch 语义自然适用，无需为 Effect 复制第二套调度器。

### 首个内置效果：Overlay 粒子发射器

首个验收效果为通用 `particle.emitter`，Scope 为 `Overlay`。雪只是该发射器的一份参数预设，而不是单独的雪系统。参数至少包括：

- `particleTexture`；
- `emissionRate` 与最大粒子数；
- 初始速度向量、随机范围与噪声扰动；
- 粒子 scale、生命周期、透明度曲线和随机 seed。

首版采用 CPU 更新：每个发射器维护自己的对象池，按发射率租用粒子、逐帧积分位置与速度；生命周期结束的粒子回到池中。正常停止将 `emissionRate` 置为零，不再创建粒子；已租用粒子继续更新并自然回收。池为空且没有存活粒子后，发射器才报告 `Completed`。GPU 粒子或批量渲染是后续可替换的渲染实现，不改变 EffectCatalog、生命周期或存档协议。

### 存档与恢复

存档的“稳定状态”定义为可确定地序列化和恢复，而不是没有持续播放中的视觉。无固定时长、依赖句柄停止的动画和效果不会破坏稳定状态。当前活跃 Effect 以 `ActiveEffectState` 保存 `effectId`、实例句柄、目标 Layer 句柄和参数；读档先恢复 Layer 与 `EffectInstance`，再由宿主重建视觉。不能只保存一个字符串 ID。

长期 Loop 动画的持久化状态包含：

- 种类（即时动画、动画 Plan 或 Effect）、稳定播放/实例句柄与目标句柄；
- 完整可重放描述（动画请求/Plan，或 Effect 的 `effectId`、参数和 Scope）；
- 当前生命周期状态、循环次数与当前轮相位，或效果的启动时间/剩余时间；
- 对粒子效果而言还包括随机 seed、发射是否已停止及恢复所需的时间基准。

持续粒子发射器可以根据 seed 与经过时间重建近似相同的场面；停止后的 drain 属于宿主短暂尾部，不重新写回 Runtime 的活跃 Effect。当前 Loop 动画保存原始原语参数并在读档后重启一个新 iteration，因此会继续活跃且保持原句柄；精确恢复保存瞬间的 iteration 相位是后续精度改进。

### 实施顺序与验收

1. 已完成：Effect 数据模型、可序列化的活跃 Effect 状态、目标 Layer 双向关联，以及任意 `AnimatableSceneInstance` 的 Plan 轨道校验。
2. 已完成：Avalonia Overlay 粒子对象池和 Layer 百叶窗 mask；Sample 的第二分支实际播放百叶窗，并接着播放飘雪。
3. 已完成：`transition.blinds` 编译为创建 Effect、驱动 `progress`、停止 Effect 的通用 Plan，不依赖 Layer 专用动画分支。
4. 后续：Catalog 参数校验、更多 Effect 动画属性、Layer 滤镜和 GPU 实现。
5. 已完成基础恢复：长期 Loop 动画保存原始入口参数并在读档时重启；后续可增加精确 iteration 相位、PingPong 相位与相关诊断。

验收：Sample 内容能够启动 Overlay 飘雪和 Layer mask；停止飘雪后不再发射且存活粒子自然消失；无时长 Effect 和 Loop 动画在存档/读档后保持活跃且可按原句柄停止。Loop/PingPong 的精确相位恢复待单独验收。

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
