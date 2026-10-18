# P6e2：关键帧画布与时间编辑

本记录保留 P6e2 原实现及验证历史。用户随后要求简化时间设置并允许随时切换轨道模式，原保秒/裁剪/应用交互由 [Timeline 交互简化](timeline-simplification.md) 取代，当前行为以该记录和正式 spec 为准。

日期：2026-10-18。状态：verified。前置 P6e1 verified，用户授权继续。

共享 Avalonia TimelineCanvas 只负责刻度、可见行、播放头、选择和拖动预览；Editor 会话负责稳定键身份、作者版本、无损 JSON patch 和单次项目历史。属性树改为按展开状态扁平化的同一行列表，固定行高并与画布共用纵向滚动；水平平移/缩放只影响时间区域，折叠父行显示子键汇总，汇总只选择不拖动。

本切片实现 Ctrl 多选、框选、整帧拖动、Delete、当前帧加键/上一键/下一键；首键不能平移或删除，碰撞不静默合并，失败保留预览并提供明确的替换碰撞/取消。拖动中不写作者内容，松手原子提交一次；Escape、捕获丢失、切 Entry、关闭和外部作者变化取消旧手势。

时长/FPS 使用数字草稿和显式应用：默认保留帧，另有保留秒数重定时；缩短默认拒绝裁掉内容，另有明确裁剪入口。所有键/事件帧一起计算，舍入碰撞整次拒绝，同帧事件顺序保留；Hermite 切线在保秒重定时时按实际相邻段帧跨度换算，保持归一化段形状。事件工具和曲线详情仍属 P6e3/P6e4，不执行游戏实时预览。

验证覆盖实际主题键鼠、可见行对齐/折叠、缩放滚动/播放头、一次拖动一条历史、取消/过期/碰撞/首键、时长/FPS/事件顺序、未知 JSON、四语、保存重开与相关回归。

## 实施与聚焦验证

共享画布和模型位于 Avalonia.Controls/Timeline，不依赖 Editor；Editor 的 TimelineKeyEditing 负责可见行和临时键身份，TimelineDocument.Keys 负责原始 JSON 局部修改。身份随作者版本失效，不给运行时关键帧增加持久 ID。轨道属性仍由既有 NumericUpDown 编辑，横向手势只修改时间，曲线值拖动留 P6e3。

13 项 Timeline 聚焦测试通过。新增真实 Semi 主题测试以物理键鼠覆盖两轨多选拖动、折叠汇总选择、纵向滚动前后行对齐、标尺定位、缩放/滚动绑定、碰撞替换、首键保护、框选、外部修改期间手势失效及保存重开；另一个用例覆盖时间草稿拒绝、裁剪、两种 FPS 行为、Hermite 切线换算、舍入碰撞、加键/定位和同帧事件顺序。未知字段、超过双精度整数范围的 JSON 数字及未来事件参数保留。

首次聚焦运行发现：折叠汇总留下多选后，单击已有选中键没有收敛到单键，随后 Ctrl 点击会切掉目标键。现于无拖动的鼠标释放时确认单选，拖动保留多选；汇总绘制也先按每个键的预览帧分组，部分子键移动时不会把未选键一起画到新位置。修正后完整 13 项通过，保留原真实指针断言。

实际截图 `p6e2-key-canvas.png` 已检查：树行与关键帧对齐，标尺和播放头共享时间原点，工具栏及底部缩放/平移可见；不宣称已有游戏预览或曲线编辑。

## 最终结果

隔离解决方案构建成功，53 条构建/分析器警告、0 错误。同一源码快照 Editor 169、Shared 223、Core 111、Architecture 45、Integration 110、Presentation 243，共 901 项通过、0 失败。Presentation TRX 另记录 6 项 NotExecuted，原因是未配置 `GALNET_AUDIO_LIBVLC` 原生媒体环境；终端汇总未将这些用例计入通过数，不把它们当作媒体验证成功。whitespace/IDE0005 verify 与源码 SHA-256 核对通过；四语各 680 keys，本切片新增 17 keys。

证据目录：`C:/Users/Starry/.codex/visualizations/2026/10/15/01a13e35-d53c-7270-b753-7b67a4eee31c/timeline-validation`。复核入口 `validate-p6e2.ps1`；构建 `p6e2-solution-build.log`，六项目结果 `p6e2-GalNet*.log/trx`，格式 `p6e2-format-verify.log`，指纹 `p6e2-source-sha256.json`，截图 `p6e2-key-canvas.png`。本阶段文档相对链接及 `git diff --check` 通过。

P6e2 退出条件满足，P6e 整体仍 in-progress。下一步 P6e3 插值、曲线及切线编辑，P6e4 事件和剩余状态/引用语义；实时场景预览继续排除。

## 用户反馈修正：播放头与控件交互

用户要求复选框与其他控件对齐、播放头拖动松手结算和 NumericUpDown 长按更快。播放头改为共享模型中的临时显示帧：移动只重绘线，松手请求一次定位后采样/重建属性行，Escape、捕获丢失、换模型或版本失效取消预览。三个带说明的 CheckBox 与轨道勾选框统一垂直居中；Timeline 数字框模板 RepeatButton 使用 250ms 初始延迟、30ms 重复间隔，单击步长不变。

偏移采用既有 Additive 语义（基础值加轨道值）；保留秒数只影响 FPS 修改时的重定时，帧/时长按新旧 FPS 比例调整，未勾选则保留帧号。这次不修改两者的数据契约。

初轮模板断言误枚举整个面板的 RepeatButton，包含 Slider 内部按钮，因此读到缩放条的 300ms 延迟。沿实际视觉祖先定位 NumericUpDown 后再检查，保持缩放条原速度；不把这个测试定位错误当作 NumericUpDown 模板缺陷。

修正已验证：13 项 Timeline 聚焦通过；同源码解决方案构建成功（53 警告、0 错误），完整受影响回归 Editor 169、Architecture 45、Presentation 243，共 457 项通过、0 失败。Presentation TRX 另有 6 项 NotExecuted，缺少原生 LibVLC/视频环境；本次未修改媒体功能。whitespace/IDE0005 verify、源码指纹和 diff 检查通过。真实指针断言验证拖动 12 次期间实际帧/属性行不变，松手只请求一次定位，Escape 和版本失效不会提交；同时检查 NumericUpDown 真实模板按钮的 Delay/Interval 和复选框对齐。截图 `p6e2-feedback-alignment.png` 已检查。

修正证据同目录：`validate-p6e2-feedback.ps1`、`p6e2-feedback-final-solution-build.log`、`p6e2-feedback-final-GalNet*.log/trx`、`p6e2-feedback-final-format-verify.log` 和 `p6e2-feedback-final-source-sha256.json`。此结果独立于上方 P6e2 原提交的 901 项回归记录。
