# 默认视觉资源与演出预设

新建默认契约项目时，编辑器将 8 个 SkSL 模板复制到 `Assets/Effects/Default/`，为每个项目生成资源 GUID。预览、运行和导出读取项目资产；升级编辑器不覆盖已有 shader。

`galnet.project.defaultVisualPrograms` 保存 bloom、blur、vignette、colorGrade、depthBlur、speedLines、reveal、maskReveal 用途绑定。编辑器插入预设或切换类型时填入实际 GUID；修改用途绑定不会追改已有 Entry。CLI / 外部作者需要显式填写资源引用。

## 旧项目补入

先重建、部署与当前引擎匹配的项目契约（声明 effect.apply 和 defaultVisualPrograms），再使用 CLI：

```text
project visuals install <project> --dry-run
project visuals install <project>
```

只补缺失用途，保留已有绑定和作者修改。缺失绑定对应的默认资产路径被占用时，整次操作拒绝覆盖；可以手动将用途绑定到已有资源。发布复用作者文件事务与 settings 原哈希检查；dry-run 不改项目，重复安装没有变化。不提供自动升级/覆盖已有 shader 的入口。

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

GameTestCase 两条路线的 ending 组均展示调色、Bloom、暗角、Z 切焦、人物动作、多层震动、两种动态集中线、冲击、柔边擦除、模糊转场和图片遮罩溶解，附中英文说明。遮罩示例使用普通背景图片的亮度。

真实相机/深度图景深、散景、镜头跟随、胶片/故障/水波 shader、翻页、天气配方和章节标题仍是候选，不属于本批交付。
