# 条目类型参考

## 格式约定

- 条目类型 ID 使用点分隔格式，如 `layer.show`、`audio.play`
- `.galgroup` 中的条目参数为 JSON 对象；以下表格描述对象字段
- `?` 后缀 = 可选参数，缺省有默认值
- 所有参数类型都是包装过后的类型，而不是原类型
- 所有条目均携带：`condition` — 基于变量的表达式，false 则跳过本条目，默认 true

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

对场上可动画实例的一个浮点属性执行 Replace 模式插值。当前内置的可动画实例是 Layer；Runtime 在动画完成或跳过后才提交最终值。若同一 `handleId` 与 `property` 已有动画在执行，新动画会替换旧动画。

| 参数 | 类型 | 说明 |
|---|---|---|
| handleId | SceneHandle | 场上可动画实例的内部句柄 |
| property | select | 目标属性。Layer 支持 `transform.x`、`transform.y`、`transform.rotationDegrees`、`transform.scaleX`、`transform.scaleY`、`opacity` |
| from | float? | 起始值；省略时在动画实际开始时读取当前显示值 |
| to | float | 目标值 |
| duration | float? | 持续时间（秒），默认 0.25，不能小于 0 |
| curve | object? | 曲线定义，默认 `{"kind":"Builtin","builtin":"Linear"}` |
| blocking | bool? | 是否等待动画完成，默认 `false` |
| skippable | bool? | 是否允许用户推进时跳过，默认 `false` |
| batchId | string? | 供宿主按批跳过动画的可选标识 |

`curve` 支持三种格式：`Builtin`（`Linear`、`Step`、`EaseIn`、`EaseOut`、`EaseInOut`）、`CubicBezier`（`x1`、`y1`、`x2`、`y2`）及 `Lut`（至少两个 `{ value, tangent }` 节点和 `Step`、`Linear` 或 `Smooth` 插值）。曲线输入固定归一化到 `[0, 1]`，输出不截断，因而贝塞尔或 LUT 可产生回弹/超调；`Smooth` 使用节点切线，`Step` 与 `Linear` 忽略切线。

> Handler: `AnimateHandler`。无效、失效或类型不匹配的句柄，以及不被属性范围接受的起止值，都会记录诊断并安全跳过。

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
| `layer.hide` | 否 | 隐藏图层 |
| `layer.move` | 否 | 移动图层 |
| `layer.replace` | 否 | 替换图层资源，保留其余状态 |
| `animate` | 由 `blocking` 决定 | 插值场上实例属性 |
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
