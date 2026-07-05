# 条目类型参考

## 格式约定

- 条目参数用 `; ` 分隔，键值对用 `: ` 分隔
- `[C]` = 复杂条目（编译为多个简单条目），`[S]` = 简单条目
- `?` 后缀 = 可选参数，缺省有默认值
- `*` 后缀 = 参数可多选，用 `,` 分隔

### 通用参数

所有条目均携带：`condition` — 基于存档变量和用户变量的表达式，false 则跳过本条目，默认 true。
所有参数类型都是包装过后的类型，而不是原类型。

---

## 文本类

### text [C]

在对话框逐字显示一句对白。按 Enter 换行则编译为多条顺序执行的 text 条目。

| 参数 | 类型 | 说明 |
|---|---|---|
| speaker | I18nKey? | 说话人，显示在对话框上方，可为空 |
| content | I18nKey | 富文本。`<b>` 加粗、`<i>` 斜体、`<color=#fff>` 颜色。`\d+N` 控制此处延迟(ms)，负数则立即显示。`\n` 换行。`{变量名}` 插入变量值 |
| interruptible | bool | true=点击直接显示全部文本并标记完成（语音可继续播放） |
| auto_next | bool | true=显示完毕自动推进，间隔见全局设置 |
| voice_asset | string*? | 配音文件引用。多选仅在有换行时生效（每行对应一个） |
| control_instance | string? | 引用的 WidgetInstance ID。缺省继承上一条目 |

> 编译后成为多条 `simple_text` 简单条目（每个换行为一条）。

### nvl [C]

全屏文本模式。隐藏对话框，屏幕中出现半透明大框，框内显示段落文本。

| 参数 | 类型 | 说明 |
|---|---|---|
| content | I18nKey | 同上。换行即换行（不拆分条目）。一页超出框高则自动分页 |
| layer_id | string? | 大框的底图 Layer ID 或 WidgetInstance ID |
| interruptible | bool | true=点击快速显示完当前页，再点下一页 |
| auto_next | bool | true=每页显示完毕后自动翻页 |

> 编译后成为多条 `nvl_page` 简单条目。

### narration [S]

屏幕中央无对话框直接显示文本，类似电影字幕。

| 参数 | 类型 | 说明 |
|---|---|---|
| content | I18nKey | 纯文本 |
| position | string | `center` / `top` / `bottom` |
| duration | float? | 秒，0 或留空 = 等待点击 |

---

## 音频类

每个 Channel 维护独立队列。同 Channel 新播放默认替换当前。

### play_audio [S]

| 参数 | 类型 | 说明 |
|---|---|---|
| asset | asset | 音频资源 |
| channel | string | `bgm` / `voice` / `sfx1`~`sfxN`（默认4，项目设置可调）/ `ambient` |
| mode | string | `once` / `repeat` / `times` |
| times | int? | 仅 `times` 时生效，默认 1 |
| blocking | bool? | true=等待播放完毕才推进下一句。默认 false |
| fade_in | string? | `无` / `线性` / `指数`，默认无 |
| fade_in_duration | int? | 毫秒 |
| crossfade | string? | `立即停止` / `交叉淡入`。默认立即停止 |
| volume | float? | 0~1，默认 1 |

### stop_audio [S]

| 参数 | 类型 | 说明 |
|---|---|---|
| channel | string | 同上 |
| fade_out | string? | 同上 |
| fade_out_duration | int? | 毫秒 |

### pause_audio [S]

| 参数 | 类型 | 说明 |
|---|---|---|
| channel | string | 同上 |

### resume_audio [S]

| 参数 | 类型 | 说明 |
|---|---|---|
| channel | string | 同上 |

### enqueue_audio [S]

将音频加入轨道队列末尾。

| 参数 | 类型 | 说明 |
|---|---|---|
| channel | string | 同上 |
| asset | asset | |
| times | int? | 默认 1 |
| fade_in | string? | |

### configure_audio [S]

配置轨道队列行为。

| 参数 | 类型 | 说明 |
|---|---|---|
| channel | string | |
| queue_end_action | string | `丢弃` / `保留并顺序播放` / `保留并随机播放` |
| empty_queue_action | string | `播放最后一首` / `停止` |

---

## 图像类（Layer）

背景和立绘统一为 Layer。

### show_layer [S]

| 参数 | 类型 | 说明 |
|---|---|---|
| id | string | Layer ID，同 ID 再次调用则替换 |
| asset | asset | 图像资源 |
| x | float? | 默认 0 |
| y | float? | 默认 0 |
| z | float? | 默认 0（背景），立绘建议 5~20 |
| transition | string? | 过渡效果名称，与目标图像联动（如交叉溶解） |

### hide_layer [S]

| 参数 | 类型 | 说明 |
|---|---|---|
| id | string | Layer ID |
| transition | string? | 淡出过渡 |

### move_layer [S]

| 参数 | 类型 | 说明 |
|---|---|---|
| id | string | Layer ID |
| x | float | 目标 x |
| y | float | 目标 y |
| z | float? | 目标 z |
| duration | float | 秒 |
| curve | string? | `linear` / `ease_in` / `ease_out` / `ease_in_out` / 多次 ease / bounce 变体 / 后续可实现自定义曲线。默认 linear |

### replace_layer [S]

同 ID 切换为另一张图，可选过渡。

| 参数 | 类型 | 说明 |
|---|---|---|
| id | string | Layer ID |
| asset | asset | 新图像资源 |
| transition | string? | 交叉溶解等 |

### transform [S]

对 Layer 应用缩放/旋转/倾斜变换。

| 参数 | 类型 | 说明 |
|---|---|---|
| id | string | Layer ID |
| category | string | `缩放` / `旋转` / `平移` / `倾斜` / `透明度` |
| params | float[2~4] | 依据类别：缩放=x,y；旋转=角度；平移=x,y；倾斜=x,y；透明度=alpha |
| duration | float | 秒 |
| curve | string? | 同上 |

### show_character [C]

复杂条目——出场/退场的快捷封装。

| 参数 | 类型 | 说明 |
|---|---|---|
| id | string | Layer ID |
| asset | asset | |
| visible | bool | true=出场，false=退场 |
| position | x,y,z? | 仅出场 |
| transition | string? | 默认 fade |
| sfx | string? | 出场/退场音效 |
| duration | float? | 默认 0.5s |

> 编译为：显示/隐藏图像 + 播放音效 + 过渡。

---

## 特效类

自动结束的特效（震动、闪白）无需手动关闭；持续特效（飘雪、色调）需手动关闭或自动根据 duration 停止。

### effect [S]

| 参数 | 类型 | 说明 |
|---|---|---|
| id | string | 特效实例 ID（同一 ID 再次调用替换旧特效） |
| effect_name | string | 特效名称：`shake` / `vignette` / `flash` / `snow` / `sepia` / ... |
| effect_params | kv*? | 特效专属参数，`key:value` 对，`,` 分隔 |
| duration | float? | 秒。自动结束的不填或填 -1 则等待手动关闭；填正数则到时自动调用关闭 |

常用特效参数：

| 特效 | 参数 |
|---|---|
| `shake` | `intensity` 强度(默认5), `frequency` 频率(默认10) |
| `vignette` | `intensity` 暗角深度(0~1), `color` 颜色(默认#000) |
| `flash` | `color` 颜色(默认#FFF), `duration` 持续时间(默认0.3) |
| `snow` | `density` 密度(默认1), `speed` 速度(默认1) |
| `sepia` | `intensity` 强度(0~1) |

### stop_effect [S]

| 参数 | 类型 | 说明 |
|---|---|---|
| id | string | 特效实例 ID |

---

## 控件类

控制预定义的 WidgetInstance 的可见性与属性。

### show_control [S]

| 参数 | 类型 | 说明 |
|---|---|---|
| id | string | WidgetInstance ID |
| transition | string? | 过渡效果 |

### hide_control [S]

| 参数 | 类型 | 说明 |
|---|---|---|
| id | string | WidgetInstance ID |
| transition | string? | |

### set_control [S]

运行时动态修改控件实例的 Config。

| 参数 | 类型 | 说明 |
|---|---|---|
| id | string | WidgetInstance ID |
| property | string | 属性名（如 `TextColor`、`FontSize`） |
| value | string | 新值 |

---

## 变量类

### variable [S] — action=set

| 参数 | 类型 | 说明 |
|---|---|---|
| target | string | 变量路由，如 `player.affection.alice` |
| value | string | 新值 |
| type | string? | `bool` / `int` / `float` / `string`。默认 string |

### variable [S] — action=op

| 参数 | 类型 | 说明 |
|---|---|---|
| target | string | |
| action | string | `加` / `减` / `乘` / `除` / `取反` / `取随机` |
| value | string | 操作数（取反不需要） |
| type | string? | 同上 |

### variable [S] — action=random

| 参数 | 类型 | 说明 |
|---|---|---|
| target | string | |
| min | float | |
| max | float | |
| type | string? | `int` / `float` |

### variable [S] — action=eval

对目标变量执行数学表达式求值。表达式中用 `[var]` 引用其他变量。

| 参数 | 类型 | 说明 |
|---|---|---|
| target | string | 结果写入的变量 UID |
| expression | string | 数学表达式。支持 `+ - * / ^ ( )`、`sin()`/`cos()`/`tan()`/`sqrt()`/`abs()`/`log()`、`pi`/`e`。用 `[var]` 引用变量值 |

表达式示例：
- `[score] + 10` — 变量 score 加 10
- `[a] * [b] + sin([angle])` — 混合运算
- `[hp] / [max_hp] * 100` — 百分比计算

底层使用自建递归下降求值器，变量引用使用 `[var]` 占位符。

---

## 视频类

### play_video [S]

| 参数 | 类型 | 说明 |
|---|---|---|
| asset | asset | 视频资源 |
| blocking | bool? | true=等待播放完毕。默认 true |
| skippable | bool? | true=点击跳过。默认 true |
| volume | float? | 0~1 |

### stop_video [S]

无参数。

---

## 鉴赏

### unlock [S]

| 参数 | 类型 | 说明 |
|---|---|---|
| category | string | `立绘` / `CG` / `OST` / `场景` |
| id | string | 对应类别下的内容 ID（文件引用或组 ID） |

解锁状态持久化在用户变量中。

---

## 流程控制

### wait [S]

| 参数 | 类型 | 说明 |
|---|---|---|
| duration | float | 秒 |

### jump_page [S]

可指定是否确认弹窗。

| 参数 | 类型 | 说明 |
|---|---|---|
| page_id | string | ScreenInstance ID |
| confirm | bool? | true=弹出确认框。默认 true |

### return_title [S]

返回标题界面。无参数，或可指定是否确认弹窗。

| 参数 | 类型 | 说明 |
|---|---|---|
| confirm | bool? | true=弹出确认框。默认 true |

### save [S]

调出存档界面。

| 参数 | 类型 | 说明 |
|---|---|---|
| slot | int? | 指定槽位则自动保存；留空则弹出存档界面 |

---

## 外部集成（预留）

### script [S] — 预留

| 参数 | 类型 | 说明 |
|---|---|---|
| script | string | 脚本文件引用或内联代码 |
| language | string? | `lua` / `python`。默认 lua |

### http [S] — 预留

| 参数 | 类型 | 说明 |
|---|---|---|
| url | string | |
| method | string? | GET / POST。默认 GET |
| body | string? | POST 请求的 body |
| target | string? | 响应写入的变量路由 |

---

## 分支（Graph 节点，非组内条目）

### 选项分支节点

组内最后执行后，展示选项，玩家选择决定走哪条边。

定义在节点属性中：选项列表 `[ {文本, 出现条件?}, ... ]`。

```
---(节点连入边)--- | 选项1 | 条件1 | --- 节点n
             | 选项2 | 条件2 | --- 节点m
             | 选项3 | 条件3 | --- 节点p
```

实际存储为 `[ {文本, 出现条件?, 目标边}, ... ]`。

### 条件分支节点

根据变量自动匹配边。

定义在节点属性中：存在条件列表，按顺序匹配。
实际存储为 `[ {条件表达式, 目标边}, ... ]`。

---

## 条目类型速查

| 类型 | C/S | 阻塞 | 说明 |
|---|---|---|---|
| `text` | C | 是 | 编译为多条 text 条目 |
| `nvl` | C | 是 | 编译为多条 nvl_page |
| `narration` | S | 是 | |
| `play_audio` | S | 可选 | |
| `stop_audio` | S | 否 | |
| `pause_audio` | S | 否 | |
| `resume_audio` | S | 否 | |
| `enqueue_audio` | S | 否 | |
| `configure_audio` | S | 否 | |
| `show_layer` | S | 否 | |
| `hide_layer` | S | 否 | |
| `move_layer` | S | 否/是 | 阻塞 = 等待移动完成 |
| `replace_layer` | S | 否/是 | 阻塞 = 等待过渡完成 |
| `transform` | S | 否/是 | |
| `show_character` | C | 否/是 | 编译为 show_layer/hide_layer + 音效 + 过渡 |
| `effect` | S | 否 | 非阻塞，duration=-1 则手动关 |
| `stop_effect` | S | 否 | |
| `show_control` | S | 否 | |
| `hide_control` | S | 否 | |
| `set_control` | S | 否 | |
| `variable` (set) | S | 否 | action=set |
| `variable` (op) | S | 否 | action=op |
| `variable` (random) | S | 否 | action=random |
| `variable` (eval) | S | 否 | 自建递归下降求值器 |
| `play_video` | S | 是 | |
| `stop_video` | S | 否 | |
| `unlock` | S | 否 | |
| `wait` | S | 是 | |
| `jump_page` | S | 否 | |
| `return_title` | S | 是 | |
| `save` | S | 是/否 | 指定 slot 则否 |
| `script` | S | 可选 | 预留 |
| `http` | S | 否/是 | 预留 |
