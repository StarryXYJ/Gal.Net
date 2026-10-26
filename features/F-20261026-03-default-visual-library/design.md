# 设计

## 当前事实

GameTestCase 有 Bloom / Blur / ColorGrade / Blinds 项目资源。Composite 已将转场展开为 animation.play；效果 time 可由时间线驱动。渲染支持 layer 与 scenePost，shader 的 size uniform 自动绑定。图层 Z 当前仅决定合成顺序。shader texture 参数尚无纹理解析器。

## 设计方向

默认资源作为显式项目资产部署，不新增运行时按名字回退的隐藏效果目录。编辑器安装包可嵌入初始化模板；新建默认项目时，在原子 staging 中将 `.sksl` 与 metadata 复制到 `Assets/Effects/`，并创建项目自己的默认视觉资源绑定。dry-run 准确报告新增文件。自定义契约项目不隐式注入内置内容。

项目资产是预览、运行与导出的唯一资源来源；更新编辑器不会自动覆盖已有项目。已有项目通过显式补入操作添加默认资源，遇到已有文件或绑定保留作者修改，不自动替换。

## 已确认的资源引用与 Entry 设计（2026-10-26）

用户已确认：

1. 单个 shader 不新增独立效果 Entry，统一使用现有 `effect.apply` / `effect.stop`。默认项目提供的是 shader 资产和默认绑定。
2. 转场、集中线和冲击等需要编排的演出保留 Composite Entry。预设通过显式资源参数接收 shader；多个 shader 使用具名参数，如 `linesProgram`、`blurProgram`。遮罩图片等也作为独立资源参数传入。
3. 项目配置保存用途到 shader 资源 ID 的默认映射，例如 `speedLines`、`blur`、`reveal`。用途键用于创作便利，不是运行时的效果 ID，也不要求 shader 的 GUID 固定。
4. 编辑器添加预设时读取项目绑定，将实际资源 ID 写入 Entry。作者可在 Entry 上改选其他项目 shader。修改项目默认绑定只影响之后的填充，不追改已有 Entry。
5. 编译器读取 Entry 上的明确引用，并原样写入生成的 `effect.apply.program`。编译器不持有 shader 对象、不读取编辑器安装路径，也不通过用途名或硬编码默认 GUID 补齐资源。
6. 默认绑定缺失时，创作表单保留未填字段并提示选择；完整编译拒绝缺失必需引用。CLI / 外部作者显式传入同样的资源参数，不依赖编辑器补默认值。
7. 替换 shader 需要满足配方所需契约：如遮罩转场要求可动画 float `progress`，集中线要求可动画 float `time`，并支持对应 layer / scenePost stage。stage、参数类型与可动画性由项目资源 metadata 校验；不把“任意 shader 可用于任意预设”作为承诺。
8. 预设参数中的 shader / 图片引用以及项目配置绑定需要进入资源依赖检查。编译后时间线事件中的引用也必须被导出依赖收集识别。

数据流：初始化模板 → 项目 shader 资产与用途绑定 → 编辑器填入复合 Entry 的资源参数 → 编译得到显式 `effect.apply` 与动画计划 → 运行时按项目资源 ID 加载。

示例（资源 ID 为占位符，配置 section 与字段最终名称待实现核对）：

```json
{
  "defaultVisualPrograms": {
    "speedLines": "<project-speed-lines-shader-id>",
    "blur": "<project-blur-shader-id>",
    "reveal": "<project-reveal-shader-id>"
  }
}
```

```json
{
  "type": "preset.speedLines",
  "parameters": {
    "playbackHandleId": "impact-lines-01",
    "program": "<project-speed-lines-shader-id>",
    "durationFrames": 60,
    "speed": 2
  }
}
```

示例 type ID 与字段名是设计示意，尚未声明为已发布 wire 契约。预设编译生成承载图层、效果实例、时间/透明度轨道和末尾清理；资源 ID 从输入传递，临时实例 ID 与资源 ID 分开。

视觉配方契约与纯时间线编译位于 Contracts.Builtins；SkSL 及模板部署资源由 Editor.Shared 分发，平台渲染能力留在 Rendering / GameView。效果资源不进入 Core。

集中线使用透明颜色图层作为承载，layer shader 生成带预乘 alpha 的程序线条；time 为逻辑动画时间，无墙钟自动播放。场景级冲击可将同一 shader 用于 scenePost；组合优先使用临时图层，结束时释放。

景深显式选择参与图层，各图层应用相同参数的 DepthBlur。渲染向支持的 shader 注入当前 layerZ，每次绘制重新读取；radius = clamp((abs(layerZ-focusZ)-focusRange)*falloff,0,maxRadius)。这样移动 Z 后仍生效，不扩展 Core 成为 3D 引擎，也不让对话 UI 失焦。第一版不自动覆盖之后新增的图层。

图片遮罩通过通用 texture 参数解析补齐，不绑定某种具体转场类型。所有 shader 纹理租约由对应效果实例拥有，停止/替换/页面释放时释放。

## 组合与边界

组合优先只输出一个 animation.play，沿用事件、轨道、存读档与快进机制。临时 ID 从显式 playbackHandleId 派生；调用方保证播放期间唯一。相对动作使用 Additive 并回到零；保留终态的镜头/登退场明确使用 Replace。取消语义沿用现有 animation.stop；不宣称取消一定执行末帧清理，必须验证并说明。

默认配方需要编辑器能分析其展开，不让新增类型全部变成 opaque。新转场复用既有分析，演出按编译结果传递状态。

## 取舍

- 先交付推荐的第一批和同底座简单预设，完整远期列表保留在 catalog，避免承诺未经设计的翻页与真实 3D 景深。
- 不新增默认自动开启的全局滤镜，也不修改现有项目资产。
- 使用仓库已有 shader 与原创实现，避免第三方许可与外部服务依赖。
