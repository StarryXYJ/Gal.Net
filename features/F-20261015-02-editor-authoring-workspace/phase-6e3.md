# P6e3：Timeline 插值、切线与曲线

日期：2026-10-19。前置 P6e2b verified。按可验证切片实施，不纳入游戏实时预览。

## P6e3a：关键帧详情与批量切线

状态：verified。关键帧详情区保留布局空间，无选择时禁用，避免框选/拖动过程中画布换位。选择已有关键帧后显示数值、出段 Step/Linear/Hermite、入/出切线。多选支持一次统一设置一个字段，混合值留空；首帧可改值/切线但沿用既有移动/删除保护。数字使用 NumericUpDown，Enter/失焦提交，Escape 撤销未确认输入；插值立即确认。

原始切线单位为值/帧，可切换按秒显示，换算为值/秒 = 值/帧 × FPS，保存始终值/帧。平坦、线性、平滑、对齐为一次性明确动作，不引入持久自动切线模式。切到 Step/Linear 保留未使用切线。每个多键动作是一次根 JSON patch/项目历史，保留大小写、未知字段、键身份和其他参数。选择/Entry/帧/根版本改变拒绝旧详情提交，保存和切语言保留有效草稿。

详情是专属 Timeline 控件，延续既有 NumericUpDown/ComboBox 布局，不新增运行时 DTO/schema。同一键的插值属于其后区间；末键仍可保存插值但无出段效果。有限数值和已有属性端点范围沿用现有校验，不在 Editor 单方面加入不同于编译/Runtime 的 Hermite 极值拒绝规则。

验证：实际关键帧选择/详情控件、混合值与批量单历史、切换插值保留切线、按秒换算/FPS、一次性预设、过期/取消、四语及保存重开，受影响回归/解决方案构建/格式/源码指纹。

## P6e3b：值曲线与拖柄手势

状态：planned。前置 P6e3a。共享 Avalonia 曲线控件与 Editor 适配分离；选中属性的 Step/Linear/Hermite 曲线与关键帧共用同一数据和选择，展开曲线、缩放/平移/采样、值拖动和入/出切线柄，支持单次手势一次历史、取消/源失效、批量键手势。绘制与运行时 sampler 一致，不用显示钳位掩盖 overshoot。

实施前核对属性有限范围的区间极值诊断与共享校验边界；若要改变编译/运行时接受范围，需同时修改相关中立规则与消费者。加权 Bezier、持续自动切线和实时游戏预览不在当前范围。

## P6e3a 实施记录

`TimelineDocument.Curves` 在原始树上批量修改选中键，沿用现有字段大小写、新增字段使用 camelCase；整次 patch 构造成功后才提交。`TimelineKeyEdit` 捕获文档、代次和选中键集合，`TimelineEditSession` 管理当前详情草稿；成功修改后按键位置重新关联选择，其他根替换/选择/帧改变使旧对象失效。详情初次挂载和默认值不写作者数据，避免控件初始化事件补写默认参数。

平滑使用前后邻键连线斜率，端点使用相邻区间；线性分别计算入/出区间斜率；对齐取现有入/出斜率平均。中间运算使用 double，保存前检查 float 有限性；任何一个目标失败均不写入。秒制仅用于显示/输入换算，FPS 修改保留原始值/帧。

聚焦回归 4 项通过：真实 Semi 主题的鼠标多选、NumericUpDown/ComboBox/预设按钮、混合值、单历史/首帧保护、大小写/未知字段、秒制/FPS、过期/取消、四语/保存重开；混合轨道范围整批拒绝；推导斜率溢出拒绝；原关键帧画布指针手势。截图 timeline-key-details.png 已检查。测试中修正了 Headless 输入修饰键的 API 类型，以及对新写入字段 camelCase 的错误预期，未通过修改产品 JSON 规则迎合测试。

最终隔离解决方案构建成功，85 条分析器/构建警告，0 错误；同源码快照 Editor 176、Core 111、Architecture 45，共 332 项通过，0 失败、0 跳过。whitespace/IDE0005 verify 与全部 11 个变更源文件 SHA-256 一致，四语各 705 keys。没有改动 Core/Runtime/编译的 Timeline 接受规则，也没有运行与本切片无关的原生媒体用例。

证据目录：`C:/Users/Starry/.codex/visualizations/2026/10/15/01a13e35-d53c-7270-b753-7b67a4eee31c/timeline-validation`。脚本 validate-key-details.ps1；构建 key-details-final-solution-build.log；TRX/日志 key-details-final-GalNet*；指纹 key-details-final-source-sha256.json；格式 key-details-final-format-verify.log；真实控件截图 timeline-key-details.png。聚焦结果 key-details-focused.trx 包含新建的 3 个用例及原画布指针用例。

正式行为见 [Group/Timeline](../../docs/spec/editor-groups.md)、[i18n](../../docs/spec/i18n.md)。未修改开工时已有的 feature.md 与 group-property-editor-design.md 设计改动。P6e3a 退出条件满足；P6e3 整体仍 in-progress，下一步 P6e3b 共享图形曲线与拖柄手势，之后 P6e4 事件/剩余语义与 P6f animate 自定义曲线。
