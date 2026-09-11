# 条目类型参考

## 格式约定

- 条目类型 ID 使用点分隔格式，如 `layer.show`、`audio.play`
- `.rawgalgroup` 中的条目参数为 JSON 对象；`.galgroup` 是其编译产物，只允许原语条目
- `?` 后缀 = 可选参数，缺省有默认值
- 所有参数类型都是包装过后的类型，而不是原类型
- 所有条目均携带：`condition` — 基于变量的表达式，false 则跳过本条目，默认 true

原语有对应的 Runtime Handler；非原语只能保存在 `.rawgalgroup`，由编译器展开为原语。`animation.play` 是原语，即使它包含多条轨道和时间轴事件。

---

## 已实现的条目类型

### text — 文本显示

在对话框逐字显示一句对白。阻塞等待用户点击。

| 参数 | 类型 | 说明 |
|---|---|---|
| speaker | I18nKey | 说话人，显示在对话框上方，可为空 |
| content | I18nKey | 富文本。支持 `<b>` 加粗、`<i>` 斜体、`<color=#fff>` 颜色标记 |
| voice | AudioAsset? | 配音文件引用 |

> Handler: `TextHandler`（阻塞）。通过 `ITypewriterView.StartTypewriter()` 打字机效果渲染，点击调用 `SkipTypewriter()` 跳过。

---

## 图像类（Layer）

背景和立绘统一为 Layer。

### layer.show

| 参数 | 类型 | 说明 |
|---|---|---|
| handleId | SceneHandle | 场上 Layer 实例的内部句柄；编辑器通过同类型句柄定位器写入 |
| assetId | ImageAsset | 图像资源 |
| transform | object? | `{ x, y, rotationDegrees, scaleX, scaleY }`；原点为游戏画布中心，缩放必须大于 0 |
| z | float? | 默认 0（背景），立绘建议 5~20 |
| displayMode | select? | `Native`、`Tile`、`Fill`、`Uniform`、`UniformToFill`，默认 `Native` |
| transitionId | string? | 交给宿主呈现层解析的过渡效果 ID；留空则不播放过渡 |
| transitionDuration | float? | 过渡持续时间（秒），默认 0.5 |
| transitionBlocking | bool? | 是否等待过渡结束，默认 `false` |
| transitionParameters | MultilineText? | 传给过渡实现的自定义参数 |

> Handler: `ShowLayerHandler`（非阻塞）。创建或更新句柄对应的 Layer，再调用 `ILayerView.ShowLayer()`。

### layer.showColor

显示一个没有图像资源的纯色 Layer。它是原语，主要由场过渡等非原语在编译产物中使用；`color` 必须为 `#RRGGBB` 或 `#AARRGGBB`。该 Layer 是临时实例，不写入稳定场景状态或存档。

| 参数 | 类型 | 说明 |
|---|---|---|
| handleId | SceneHandle | 临时纯色 Layer 句柄 |
| color | string | `#RRGGBB` 或 `#AARRGGBB` |
| transform | object? | 完整 Transform，默认单位 Transform |
| z | float? | 默认 1000，通常覆盖目标 Layer |
| opacity | float? | 默认 1 |

> Handler: `ShowColorLayerHandler`（非阻塞）。创建临时 Layer，使用 `Fill` 方式覆盖游戏画布；后续仍通过 `layer.hide` 使其句柄失效。

### layer.hide

| 参数 | 类型 | 说明 |
|---|---|---|
| handleId | SceneHandle | 要移除的 Layer 实例句柄 |
| transitionId | string? | 交给宿主呈现层解析的过渡效果 ID；留空则不播放过渡 |
| transitionDuration | float? | 过渡持续时间（秒），默认 0.5 |
| transitionBlocking | bool? | 是否等待过渡结束，默认 `false` |
| transitionParameters | MultilineText? | 传给过渡实现的自定义参数 |

> Handler: `HideLayerHandler`（非阻塞）。从动态实例管理器和场景状态中删除 Layer；句柄随即失效。后续使用失效句柄的操作会记录诊断并安全跳过。

### layer.move

| 参数 | 类型 | 说明 |
|---|---|---|
| handleId | SceneHandle | 要移动的 Layer 实例句柄 |
| transform | object | 完整目标 Transform |
| z | float | 目标 z |
| duration | float | 移动持续时间（秒），默认 0.5 |

> Handler: `MoveLayerHandler`（非阻塞）。

### layer.replace

| 参数 | 类型 | 说明 |
|---|---|---|
| handleId | SceneHandle | 要替换资源的 Layer 实例句柄 |
| assetId | ImageAsset | 新图像资源 |

> 仅替换资源，保留 Layer 的 Transform、z 和展示模式；不存在或类型不匹配的句柄安全跳过。

### animate

对场上可动画实例的一个浮点属性执行插值。当前内置的可动画实例是 Layer；Runtime 在动画完成或跳过后才提交最终值。`Replace` 使用绝对值，同一 `handleId` 与 `property` 的新 Replace 动画会替换旧 Replace 动画。`Additive` 将关键值视为相对增量，可与 Replace 及其他 Additive 动画同时运行，显示值为 Replace 基值加上所有活动增量。

| 参数 | 类型 | 说明 |
|---|---|---|
| playbackHandleId | SceneHandle | 本次动画播放实例的句柄；活动期间必须唯一，可由 `animation.stop` 定位 |
| handleId | SceneHandle | 场上可动画实例的内部句柄 |
| property | select | 目标属性。Layer 支持 `transform.x`、`transform.y`、`transform.rotationDegrees`、`transform.scaleX`、`transform.scaleY`、`opacity` |
| from | float? | 起始值；省略时在动画实际开始时读取当前显示值 |
| to | float | 目标值 |
| duration | float? | 持续时间（秒），默认 0.25，不能小于 0 |
| curve | select? | `Linear`、`Step`、`EaseIn`、`EaseOut`、`EaseInOut`，默认 `Linear` |
| blocking | bool? | 是否等待动画完成，默认 `false` |
| skippable | bool? | 是否允许用户推进时跳过，默认 `false` |
| batchId | string? | 供宿主按批跳过动画的可选标识 |
| loopMode | select? | `Once`、`Loop` 或 `PingPong`，默认 `Once`；后两者必须非阻塞且不可跳过。PingPong 每轮从目标值平滑返回起始值后再重复 |
| blendMode | select? | `Replace`（默认，绝对值）或 `Additive`（相对增量） |

`animate` 是单属性便捷原语，只使用内置曲线。复杂多段曲线与多属性同步动画应使用 `animation.play`。

> Handler: `AnimateHandler`。无效、失效或类型不匹配的句柄，以及不被属性范围接受的起止值，都会记录诊断并安全跳过。

### animation.play

播放一个关键帧 `AnimationPlan` 原语。Plan 含 `playbackHandleId`、帧率、总帧数、阻塞/跳过/批次/循环设置，以及并行的 Float 属性轨道和时间轴事件。每条轨道可用 `blendMode: Replace|Additive` 选择绝对值或相对增量；轨道关键帧支持 `Step`、`Linear`、`CubicHermite`；事件只能是原语条目。Plan 目前支持 `Once` 与 `Loop`，Loop 的每一轮都会重放全部事件；`PingPong` 仅适用于 `animate`。

### animation.stop

| 参数 | 类型 | 说明 |
|---|---|---|
| playbackHandleId | SceneHandle | 要停止的活动动画播放句柄 |
| mode | select? | `AfterIteration`（默认，当前轮结束后停止）或 `CompleteImmediately` |

### transition.crossFade（非原语）

仅可出现在 `.rawgalgroup`。它接收旧/新 Layer 句柄、新资源与 Layer 显示参数、帧率和持续帧数，编译成一个 `animation.play`：第 0 帧显示透明新 Layer、两条 opacity 轨道交叉淡化、结束帧隐藏旧 Layer。该类型没有 Handler。

### transition.fadeBlack / transition.fadeWhite / transition.fadeColor（非原语）

三个场过渡共享同一组参数 schema，分别使用黑色、白色或 `color` 指定的纯色 Overlay。它们都编译为一个 `animation.play`：第 0 帧显示透明 Overlay，先淡入至不透明；随后隐藏旧 Layer 并显示新 Layer；全遮挡保持指定时长后淡出 Overlay，结束帧删除其临时句柄。整个过程同时只保留旧/新目标 Layer 与内部 Overlay，不复制图像或 Layer 状态。

| 参数 | 类型 | 说明 |
|---|---|---|
| playbackHandleId | SceneHandle | 本次过渡播放句柄 |
| fromLayerHandleId | SceneHandle | 要在场色完全遮住时隐藏的当前 Layer |
| toLayerHandleId | SceneHandle | 中点显示的新 Layer 句柄 |
| toAssetId | ImageAsset | 新 Layer 图像资源 |
| toTransform | object? | 新 Layer Transform，默认单位 Transform |
| toZ | float? | 新 Layer z，默认 0 |
| toDisplayMode | select? | 新 Layer 展示方式，默认 `Fill` |
| overlayZ | float? | 临时 Overlay 的 z，默认 1000 |
| fadeInDuration | float? | Overlay 淡入时长（秒），默认 0.4，必须大于 0 |
| holdDuration | float? | 全遮住画面后的保持时长（秒），默认 0.1，可为 0 |
| fadeOutDuration | float? | Overlay 淡出时长（秒），默认 0.4，必须大于 0 |
| blocking | bool? | 是否等待整个过渡结束，默认 `false` |
| skippable | bool? | 是否允许推进时跳过，默认 `true` |
| batchId | string? | 可跳过过渡的批次标识 |
| color | string | 仅 `transition.fadeColor`：`#RRGGBB` 或 `#AARRGGBB` |

这些类型与其他非原语一样，都是具有声明式参数列表的 Entry；它们只实现 `Compile`，不拥有特殊的转场运行时接口或 Handler。编译器以固定 60 FPS 将上述真实时间量化为关键帧 Plan。编辑器可按所属 `Transition` 分类聚合展示，并完全依据 schema 生成表单。

---

## 音频类

### audio.play

| 参数 | 类型 | 说明 |
|---|---|---|
| channel | select | `bgm` / `sfx` / `voice`，默认 `bgm` |
| asset | AudioAsset | 音频资源 |
| volume | float? | 0~1，默认 0.8 |
| mode | select | `once` / `loop`，默认 `once` |
| times | int? | 播放次数，默认 1 |

> Handler: `PlayAudioHandler`（非阻塞）。

### audio.stop

| 参数 | 类型 | 说明 |
|---|---|---|
| channel | select | `bgm` / `sfx` / `voice`，默认 `bgm` |

> Handler: `StopAudioHandler`（非阻塞）。

### audio.pause

| 参数 | 类型 | 说明 |
|---|---|---|
| channel | select | `bgm` / `sfx` / `voice`，默认 `bgm` |

> Handler: `PauseAudioHandler`（非阻塞）。

### audio.resume

| 参数 | 类型 | 说明 |
|---|---|---|
| channel | select | `bgm` / `sfx` / `voice`，默认 `bgm` |

> Handler: `ResumeAudioHandler`（非阻塞）。

### audio.enqueue

将音频加入轨道队列末尾。

| 参数 | 类型 | 说明 |
|---|---|---|
| channel | select | `bgm` / `sfx` / `voice`，默认 `bgm` |
| asset | AudioAsset | 音频资源 |
| times | int? | 播放次数，默认 1 |

> Handler: `EnqueueAudioHandler`（非阻塞）。

---

## 视频类

### video.play

| 参数 | 类型 | 说明 |
|---|---|---|
| asset | VideoAsset | 视频资源 |

> Handler: `PlayVideoHandler`（非阻塞）。

### video.stop

无参数。

> Handler: `StopVideoHandler`（非阻塞）。

---

## 对话框控件

### dialogue.show

显示对话框控件。

> Handler: `ShowDialogueHandler`（非阻塞）。调用 `IControlView.ShowDialogue()`。

### dialogue.hide

隐藏对话框控件。

> Handler: `HideDialogueHandler`（非阻塞）。调用 `IControlView.HideDialogue()`。

---

## 特效类

### effect.apply

| 参数 | 类型 | 说明 |
|---|---|---|
| type | string | 特效类型名称 |
| parameters | MultilineText | JSON 格式的特效参数，如 `{"intensity":5,"frequency":10}` |

> Handler: `ApplyEffectHandler`（非阻塞）。调用 `IEffectView.ApplyEffect()`。

### effect.stop

| 参数 | 类型 | 说明 |
|---|---|---|
| id | string | 特效实例 ID |

> Handler: `StopEffectHandler`（非阻塞）。调用 `IEffectView.StopEffect()`。

---

## 变量类

### variable.set

对目标变量执行表达式求值并赋值。

| 参数 | 类型 | 说明 |
|---|---|---|
| target | VariableName | 目标变量名，支持 `player.` / `save.` 前缀 |
| expression | Expression | 表达式，支持 `[var]` 引用变量、算术和比较运算 |

表达式示例：
- `[score] + 10` — 变量 score 加 10
- `[a] * [b] + [angle]` — 混合运算
- `flag == true && score > 50` — 复合条件

> Handler: `SetVariableHandler`（非阻塞）。底层使用 NCalc 引擎求值。

---

## 流程控制

### wait

| 参数 | 类型 | 说明 |
|---|---|---|
| duration | float | 等待秒数，默认 1 |

> Handler: `WaitHandler`（阻塞）。可被打断。

---

## 鉴赏

### unlock_gallery

解锁鉴赏条目，持久化到游戏进度（独立于存档槽位）。

| 参数 | 类型 | 说明 |
|---|---|---|
| category | select | `Portrait` / `Cg` / `Scene` |
| id | int | 对应类别下的内容 ID |

> Handler: `UnlockGalleryHandler`（非阻塞）。需要 `IGameProgressService`。

---

## 条目类型速查

| 类型 | 阻塞 | 说明 |
|---|---|---|
| `text` | 是 | 打字机文本显示 |
| `layer.show` | 否 | 显示图层 |
| `layer.showColor` | 否 | 显示临时纯色 Overlay 图层 |
| `layer.hide` | 否 | 隐藏图层 |
| `layer.move` | 否 | 移动图层 |
| `layer.replace` | 否 | 替换图层资源，保留其余状态 |
| `animate` | 由 `blocking` 决定 | 插值场上实例属性 |
| `animation.play` | 由 Plan 的 `blocking` 决定 | 多轨关键帧时间轴 |
| `animation.stop` | 否 | 请求停止 Loop 动画播放 |
| `transition.crossFade` | — | 非原语；编译为 `animation.play` |
| `transition.fadeBlack` | — | 非原语；黑场过渡，编译为 `animation.play` |
| `transition.fadeWhite` | — | 非原语；白场过渡，编译为 `animation.play` |
| `transition.fadeColor` | — | 非原语；自定义颜色场过渡，编译为 `animation.play` |
| `audio.play` | 否 | 播放音频 |
| `audio.stop` | 否 | 停止音频 |
| `audio.pause` | 否 | 暂停音频 |
| `audio.resume` | 否 | 恢复音频 |
| `audio.enqueue` | 否 | 入队音频 |
| `video.play` | 否 | 播放视频 |
| `video.stop` | 否 | 停止视频 |
| `dialogue.show` | 否 | 显示对话框 |
| `dialogue.hide` | 否 | 隐藏对话框 |
| `effect.apply` | 否 | 应用特效 |
| `effect.stop` | 否 | 停止特效 |
| `wait` | 是 | 等待指定秒数 |
| `variable.set` | 否 | 求值表达式并赋值 |
| `unlock_gallery` | 否 | 解锁鉴赏条目 |

---

## 规划中 / 预留条目

以下条目类型在设计中已规划，但尚未实现：

| 类型 | 说明 |
|---|---|
| `narration` | 屏幕中央无对话框直接显示字幕文本 |
| `nvl` | 全屏 NVL 文本模式 |
| `layer.transform` | 对 Layer 应用缩放/旋转/倾斜变换 |
| `show_character` | 立绘出场/退场快捷封装（编译为多条简单条目） |
| `audio.configure` | 配置轨道队列行为 |
| `effect.configure` | 特效参数动态配置 |
| `control.show` | 显示自定义 WidgetInstance |
| `control.hide` | 隐藏自定义 WidgetInstance |
| `control.set` | 运行时修改控件实例属性 |
| `variable.op` | 变量算术操作（加/减/乘/除/取反/取随机） |
| `variable.random` | 变量随机赋值 |
| `return_title` | 返回标题界面 |
| `save` | 调出存档界面或自动存档 |
| `script` | 外部脚本集成（lua/python） |
| `http` | HTTP 请求集成 |
