# 默认视觉资源与演出预设

新建默认契约项目时，编辑器将 16 个 SkSL 模板和 5 个天气粒子纹理复制到 `Buildin/Effects/Default/`。它们在所有项目中使用相同的固定资源 ID，可以查看和编辑；编辑器禁止删除、移动或改名这些资源及其父目录。预览、运行和导出读取项目中的资源，升级编辑器不覆盖作者修改。

视觉预设使用相应内置资源的固定 ID；每条 Entry 仍可单独选择其他资源。项目设置不再维护统一的默认用途绑定。若在编辑器外删除内置文件、sidecar 或改变其固定身份，校验和构建会报错。

## 显式安装

契约声明默认项目配置和 `effect.apply` 时，可以使用 CLI 显式安装缺失的完整资源对：

```text
project visuals install <project> --dry-run
project visuals install <project>
```

安装保留已有资源的作者修改；文件和 sidecar 不完整、路径被占用或身份不匹配时拒绝操作。dry-run 不改项目，重复安装没有变化。旧项目不自动迁移。

## 通用效果

使用现有 `effect.apply` / `effect.stop`，不为每种 shader 新增 Primitive。program 是项目资源 GUID，instanceId 是效果实例身份。

```json
{
  "type": "effect.apply",
  "parameters": {
    "instanceId": "scene-vignette",
    "program": "<vignette shader GUID>",
    "parameters": { "strength": 0.7, "radius": 0.3, "softness": 0.4 }
  }
}
```

不填 targetHandleId 表示场景后处理，填写则只作用于该图层；metadata 必须支持相应 stage。默认对话 UI 不在场景后处理范围内。

| 资源 | 参数与行为 |
|---|---|
| Bloom | strength、radius、threshold、softKnee；初始 strength=0 |
| Blur | radius、direction；二维模糊组合 [1,0] 与 [0,1] 两个有序实例；radius=0 为原图 |
| Vignette | strength、radius、softness、center；center 为归一化坐标 |
| ColorGrade | exposure（档）、saturation、contrast、temperature；中性初值 |
| DepthBlur | focusZ、focusRange、falloff、maxRadius；读取每次绘制的 layerZ |
| SpeedLines | time、speed、density、strength、flicker、mode（Radial/Directional）、angle（度）、center、color |
| Reveal | progress、mode（Wipe/Radial/Noise/Blinds）、softness、angle、count、center |
| MaskReveal | progress、softness、mask（项目图片 GUID）；图片亮度作为遮罩并缩放到画布 |

景深对每个参与图层显式应用 DepthBlur；半径为 `clamp((abs(layerZ-focusZ)-focusRange)*falloff,0,maxRadius)`。新增图层需要显式参与，不生成图片内部真实深度。layerZ 是保留 uniform，不能声明为作者参数。

直接使用 SpeedLines 时，创建透明颜色图层（#00000000），附加 shader，并通过 animation.animate 或 timeline.play 驱动效果实例的 time。它不读取墙钟；只应用 shader 不会自动运动。

## 复合预设

所有预设编译为一个 `timeline.play`。playbackHandleId 在播放期间必须唯一；`<playbackHandleId>/visual/` 是本次演出的临时对象命名空间，不能用作目标图层身份。

| Entry | 输入 |
|---|---|
| transition.reveal | oldHandleId、newHandleId、assetId、program、mode；Mask 另需 maskAsset |
| transition.blur | oldHandleId、newHandleId、assetId、blurProgram、radius；双层各两遍模糊 |
| preset.motion | handleId、motion；FadeIn/FadeOut/SlideIn/SlideOut/PopIn/Jump/Nod/ShakeHead/Push |
| preset.shake | handleId、amplitude、cycles、angle、playbackMode；一个条目作用于一个图层 |
| preset.speedLines | program、parameters（shader 参数对象）、speed、flicker、z、playbackMode |
| preset.impact | 集中线参数加 handleId、amplitude、cycles、angle、flashOpacity；组合震动、缩放脉冲、闪白和集中线 |

```json
{
  "type": "preset.impact",
  "parameters": {
    "playbackHandleId": "impact-01",
    "program": "<speedLines shader GUID>",
    "handleId": "hero",
    "parameters": { "mode": "Radial", "density": 80, "color": "#FFFFFF" },
    "durationFrames": 48,
    "amplitude": 16,
    "blocking": true
  }
}
```

帧率默认 60，最短 2 帧。人物 motion 的 transform 是作者指定的基准变换；Slide/PopIn/Push 据此生成绝对轨道，编译器不读取播放中的场景。Jump/Nod/ShakeHead、震动和冲击脉冲使用相对叠加并回到零。Nod 使用 rotationDegrees；ShakeHead 的水平位移幅度使用 distance。

Push 的 anchorX/anchorY 为相对画布中心的逻辑坐标。锚点缩放与 angle/distance 移动分别控制；只推近时 distance=0。倍数为 s 时，X 终点为 `x + cos(angle)*distance + (x-anchorX)*(s-1)`。

转场还接受 transform、z、displayMode。新图层需要排在旧图层前面；相同 Z 时后创建的新图层在前。既有 crossFade/slide/blinds/fadeBlack/fadeWhite/fadeColor 保留。

替换 shader 必须满足编排要求：layer stage，time/progress/radius 等参数的种类、可动画性和数值范围。项目验证、单组编译、build/export 拒绝缺失或不匹配的资源。Mask 模式需选择 maskReveal 对应程序；切换 mode 不自动覆盖作者已选的 program。

震屏和集中线的 `playbackMode` 默认为 `Timed`（指定时长），支持 `UntilStopped`（持续到停止）。定时模式的 `blocking` 决定剧情是否等待；持续模式固定为不阻塞且不可通过对白点击跳过，不受作者原来的 blocking/skippable 值影响。持续模式下 durationFrames/frameRate 是循环周期，震屏不逐轮衰减，集中线保持显示、不重复创建 Effect 或图层。人物动作、冲击和转场仍为单次播放。

例如，先插入持续震屏，经过若干对白后用一个“停止动画”结束：

```json
{
  "type": "preset.shake",
  "parameters": {
    "playbackHandleId": "earthquake",
    "handleId": "background",
    "playbackMode": "UntilStopped",
    "durationFrames": 60,
    "cycles": 4,
    "amplitude": 16
  }
}
```

需要同时震动多个图层时，为每个图层添加一个 `preset.shake` 条目，分别选择 `handleId` 和唯一的 `playbackHandleId`。`preset.impact` 同样每个条目只选择一个图层。旧的 `targets` 数组不再作为输入；已有多目标配置需拆成多个条目。

```json
{
  "type": "animation.stop",
  "parameters": { "playbackHandleId": "earthquake", "mode": "CompleteImmediately" }
}
```

集中线使用相同的停止方式。立即完成会等待本次演出收尾后再推进剧情：震屏偏移归零；集中线停止并移除自己的 Effect 和临时图层；其他独立效果保留。不需要追加 effect.stop。Timeline 的 `AfterIteration` 模式请求当前轮播完后再收尾，该停止 Entry 不等待整轮。无活动呈现的 Headless/CatchUp 循环直接完成逻辑收尾。

正常完成和快进也执行末帧清理。scope 取消/被替换不视为正常完成，不保证末帧事件执行；非阻塞预设应在切场前明确完成，不能并发复用同一 playbackHandleId。立即停止清理完成后允许复用该标识。

当前导出打包全部项目资产，时间线使用的 shader 和遮罩图片均在资源包中。静态引用查询仍报告候选与不完整性，零匹配不是删除安全证明。

## 展示与范围

GameTestCase 按主题分组比较基础与扩展视觉库：`shaders` 集中展示 shader/景深，`masks` 展示遮罩，`characters` 展示人物与情绪，`weather` 展示天气，`transitions` 展示转场。两条路线均可体验完整案例；详见 [演示项目](../../../GameTestCase/README.md)。

真实相机/深度图景深、散景、自动镜头跟随、描边和翻页尚未实现。下面的扩展库提供显式视差、胶片/故障/波纹、天气和文本卡。


## 扩展效果与演出

新增项目 shader 用途：`distortion`、`radialBlur`、`film`、`glitch`、`pixelate`、`spotlight`、`shapeMask`、`mood`。旧项目仍用显式 `project visuals install` 补入。天气绑定与 shader 绑定一样只用于创作填充，Entry 保存显式资源 GUID，已有作者文件不自动升级。

| Shader | 主要参数 |
|---|---|
| Distortion | strength（像素）、frequency、time、speed、mode=Wave/Ripple/Heat、centerX/centerY |
| RadialBlur | strength、mode=Zoom/Spin、centerX/centerY；固定 12 次采样 |
| Film | strength、time、grain、flicker、scratches |
| Glitch | strength、time、aberration、displacement、scanlines |
| Pixelate | blockSize，1 为原图 |
| Spotlight | strength、centerX/centerY、radius、feather |
| ShapeMask | mode=Rectangle/RoundedRectangle/Ellipse/Eyelid/Image/Letterbox、progress、centerX/centerY、width/height、rotation、cornerRadius、feather、invert、overlay、color、可选 mask 图片 |
| Mood | strength、mode=Memory/Dream/Tension/Injury |

动态 shader 的 time 仍需 Timeline 驱动。shader 与粒子颜色为 `#RRGGBB` / `#RRGGBBAA`；颜色图层沿用 `#RRGGBB` / `#AARRGGBB`，不能混用八位颜色顺序。

ShapeMask 的中心、宽高相对画布归一化，feather/cornerRadius 相对画布短边，rotation 为度。feather=0 表示硬边。overlay=false 时裁切目标图层透明度；overlay=true 时在独立颜色图层上绘制遮挡色。Image 模式按图片亮度渐进遮挡，progress=0 完全显露、1 完全遮挡；Eyelid 为 0 完全闭合、1 完全打开。

| Entry | 行为 |
|---|---|
| preset.mask | program、instanceId、motion、parameters、overlay、z；motion=Blink/Open/Close/LetterboxIn/LetterboxOut/Show/Hide；Blink 的 holdRatio 控制闭眼停留比例 |
| preset.flash | color、strength、count、overlay、z；闪光与多次闪电 |
| preset.idle | handleId、motion=Breathe/Sway、scaleAmount、rotationDegrees、distance、playbackMode |
| preset.weather | texture、weather=Rain/Snow/Petals/Dust/Fireflies、rate、maxParticles、width/height、wind、seed、z、playbackMode |
| preset.mood | program、instanceId、mood、strength、targetHandleId（空为场景）、exit；进入保留自身效果，exit=true 渐退并清理 |
| preset.heartbeat | program（Vignette）、可选 handleId、strength、playbackMode、可选 sound/trackId；双脉冲暗角与相对缩放 |
| preset.parallax | targets=[{handleId,transform,factor}]、distanceX/Y、scale、anchorX/Y；作者显式指定每层基准 |
| preset.emphasis | program（ColorGrade）、instanceId、handleIds、activeHandleId、dimExposure、clear |
| dialogue.emphasis | 强调参数加 speaker/content、可选 voice/trackId；强调动画非阻塞并与既有对白/语音组合 |

所有视觉 clip 沿用 playbackHandleId、frameRate、durationFrames、blocking、skippable、batchId。持续 idle/weather/heartbeat 用 `animation.stop` 停止，复用指定时长与 UntilStopped 规则。停止时只收回各自资源/相对偏移。天气发射区域使用左上角原点的像素坐标。雨、雪、花瓣从画面上方 16 像素的全宽发射带落入；尘埃和萤火虫在 width×height 范围浮动。停止天气 Timeline 使用 `particle.stop.clear=false`，仅停止旧轮发射；已有粒子自然淡出并在排空后释放，不阻塞下一轮开始。要让新旧天气短暂共存，使用不同的 playbackHandleId；同 ID 仍表示替换。手动需要立即清除时可使用 `particle.stop.clear=true`。雨滴默认细长半透明，粒子尺寸范围为 0.0572～0.143，并叠加 250 px/s 的水平风速形成斜雨；`wind` 可进一步调整。粒子尺寸按纹理等比缩放，同时影响雨丝的宽度和长度。发射带向上风侧扩展，保持下游覆盖。雨丝朝向跟随速度，花瓣颜色为桃红至粉红。为避免数量上限导致成批断档，天气实际发射速率取 `min(rate, maxParticles/(最大寿命+0.1))`；需要更高密度时相应提高 maxParticles。GameTestCase 在斜雨之后加入径向雨：用普通粒子 circle/radialVelocity 和 `initial.alignToVelocity=true` 从中心向外发射。天气纹理是可编辑项目资源，不表示高级粒子碰撞或真实发光。新雨滴模板为 4×64 的透明渐隐线条；模板升级不会覆盖既有项目纹理，既有项目要得到相同外观需替换 Rain 绑定的纹理，保留其资源 GUID。

遮罩默认只在场景内按 z 排序。`overlay=true` 的遮罩颜色层在默认 PlayerSurface 的透明覆盖面合成，覆盖场景、对白和文本卡；系统退出/返回确认始终更高。Close/LetterboxIn/Show 保留遮罩，Open/LetterboxOut/Hide/Blink 完成后清理；跨条目开关同一遮罩须使用相同 instanceId。

Blink/Open/Close 默认羽化为 `0.025`，可由 `parameters.feather` 覆写（含 `0` 硬边）。眼睑开口会扩大，`progress=0.65..1` 平滑淡出剩余角落遮挡，完全睁开不再切换可见性。闭合端连续收束羽化；边界预留两个 shader 像素。遮罩预设使用零切线三次 Hermite 缓动，Blink 在闭合保持段两端平滑衔接。

说话人强调要求显式给出完整参与图层和当前活动图层。相同 instanceId/参与图层沿用自己的调色实例，从当前显示值继续变化；下一次切换前不自动恢复。clear=true 渐变回中性后只移除自己的效果。`effect.apply.ensureOnly=true` 可确保同 program/target 的现有实例不被重建；身份已被不同 program/target 占用时拒绝。强调不自动映射角色和立绘，不修改其他调色实例。

新转场为 `transition.flash`、`transition.zoom`、`transition.pixelate`、`transition.glitch`，沿用交叉淡化的旧/新图层、资源、变换和时间参数。pixelate/glitch 还需显式 program；strength 控制强度，zoom 可指定 anchorX/Y。

GameTestCase 的 `presentation-library` 现在集中展示文本卡，与 `intro` 的文本/图集案例相邻；音效集中在 `audio`，视频集中在 `media`。每章有中英文标题和引导，并显式准备、清理布景。

## 通用文本卡

`text.card` 的 content 走剧情本地化；position 支持 TopLeft/Left/BottomLeft、TopCenter/Center/BottomCenter、TopRight/Right/BottomRight。styleName 是 App 的样式名，holdSeconds 可覆写样式停留时长；blocking=true 等待完整入场、停留、退场，默认非阻塞。快速播放/追赶不补弹提示；会话取消与重置清理，文本卡不写入存档。

左侧从左进入并向左退出，右侧反向，中间默认淡入；同位置新卡替换旧卡、旧退新进，不同位置独立。缺失样式回退到 default。默认呈现服务为 `TextCardHost` / `ITextCardPresenter`，App 可用 `TextCardTheme.<styleName>` ControlTheme 自定义 ContentControl 模板、字体和装饰；`PlayerSurface.TextCards.ResolveStyle` 可为样式名返回 `TextCardStyle(enter, hold, exit, distance)`。

Sample 在 PlayerView.axaml 提供 default/music/location/chapter 主题。音乐仍在真实 Started 后解析曲名，再使用同一服务的 BottomLeft/music；循环、读档和鉴赏不因此重复提示。
