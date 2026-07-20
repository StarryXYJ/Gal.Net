# 条目类型参考

## 格式约定

- 条目类型 ID 使用点分隔格式，如 `layer.show`、`audio.play`
- 条目参数用 `; ` 分隔，键值对用 `: ` 分隔
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
| id | string | Layer ID，同 ID 再次调用则替换 |
| asset | ImageAsset | 图像资源 |
| x | float? | 默认 0 |
| y | float? | 默认 0 |
| z | float? | 默认 0（背景），立绘建议 5~20 |
| transition | select? | 过渡效果：`fade` / `dissolve` / `slide_left` / `slide_right` |
| duration | float? | 过渡持续时间（秒），默认 0.5 |

> Handler: `ShowLayerHandler`（非阻塞）。先应用 transition（如有），再调用 `ILayerView.ShowLayer()`。

### layer.hide

| 参数 | 类型 | 说明 |
|---|---|---|
| id | string | Layer ID |
| transition | select? | 过渡效果 |
| duration | float? | 过渡持续时间，默认 0.5 |

> Handler: `HideLayerHandler`（非阻塞）。

### layer.move

| 参数 | 类型 | 说明 |
|---|---|---|
| id | string | Layer ID |
| x | float | 目标 x |
| y | float | 目标 y |
| z | float | 目标 z |
| duration | float | 移动持续时间（秒），默认 0.5 |

> Handler: `MoveLayerHandler`（非阻塞）。

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
| `layer.replace` | 同 ID 切换图像 |
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