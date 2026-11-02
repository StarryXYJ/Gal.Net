# Sample 布局与自适应优化总结

日期：2026-11-02。

## P9 设置导航的两层区分

- 一级分类沿用 GroupHeadingSize，选中改为半粗体，取消持续灰底和下划线；hover/pressed仍用轻灰反馈。操作页设备切换沿用较小的 ActionFontSize、常规字重和浅灰选中底，两层都随字号档位调整。
- 复用原TabControl，仅添加用途样式类与Sample标题模板；透明半粗体占位避免英文大字号切换时相邻标题移动，不增加测量逻辑或缓存。共享默认标题使用DataTemplates，允许Sample样式覆盖。去除设备Tab正文默认内边距，表单与分类区域左对齐，整个设置区域继续居中。
- 141项相关Headless回归全部执行通过，包括新增四组中英文、宽窄屏和字号组合；实际检查选中字重/背景、hover、标题位置稳定、键盘设备切换及编辑器复用，既有手柄、录入、滚动、字号、按钮、模态回归通过。已查看操作页截图；证据为 output/settings-hierarchy/regression.trx 和 verified/。移动真机未专项验证。
- 本次用户授权只替代P8的一级设置分类选中规则；工具栏仍不使用加粗或下划线。正式规范与玩家手册已同步。

## P8 去除分类缩进与多余交互效果

- 设置Tab左右Padding改为0，分类间距改为Margin24，首项文字与页标题/正文左边界相同；selected使用灰底，隐藏原下划线，不使用加粗。
- 普通按钮hover/pressed分别使用轻/重中性背景，文字不反白；Mono主要按钮按下从近黑改为灰色。禁用按钮不响应hover，保留键盘细边框；下拉列表按下状态也采用中性底色和稳定文字。
- 游戏工具栏合并重复样式并移除adaptive-target标记；hover、pressed、checked仅变灰底，不变字色/字重，不加下划线。文本阴影仅作用于文字，背景不再带阴影。取消Fluent底层按压缩放和变换过渡，不改变收纳和播放命令。
- 137项相关回归全部执行通过。新增三套配色的实际按下像素与变换检查，工具栏覆盖五种窗口下的鼠标移入/按下/选中和键盘焦点；既有播放切换、跳过确认、模态及卡片回归通过。已检查设置与工具栏截图；证据在 output/button-feedback/verified.trx、captures/和themes/。移动真机未专项验证。
- [基础主题按压变换经验](../../.agents/lessons/L-20261102-11-inherited-button-press-motion.md)。

## P7 设置区域居中、内部左对齐

按用户澄清，设置页的标题、分类面板、正文和页脚共用最大宽800的居中容器，标题从面板左边界开始。移除误解时添加的正文宽度绑定，继续使用原Grid布局。90项相关回归全部执行通过，已检查中英文、宽窄屏及大字号截图。证据：output/settings-left/verified.trx与centered/。配色只分析，建议及Linear/Things参考来源记录在design，尚未实施。

## P6 分类标题与卡片统一

- 设置分类 Tab 使用已有 GroupHeadingSize，默认较小档18→22，不新增字号体系。
- 存档与图片/视频/场景鉴赏共用 AdaptiveCardGrid 和 CardColumns/CardHeight；高度210、外边距7、列距6、行距8，底部标题区域最小高84。
- 三类鉴赏的重复卡片 XAML 收敛为同一个 GalleryCardTemplate。保留标题居中、名称提示、锁定状态、各自打开命令与命中；移除旧 AdaptiveGalleryPanel，原存档网格改为通用名称。
- 113项相关检查全部执行通过，含360/1000/1920宽度下逐一卡片边界对比及图片/标题点击。已检查实际渲染截图；TRX和截图位于 output/card-layout/，不提交Git。移动真机未专项验证。

## P5 极简排版与代码精简

用户收敛为保持现有极简风格，仅改善排版并精简逻辑：

- 普通页面统一为单行标题、右侧返回和分隔线，去除设置/存档/鉴赏页的通用副标题。设置页只将分类、表单与提示限宽为 800，页头和其他页同位。
- 标签列 260、列距 32，行高至少 44、行间距 16；数值框缩为 160，下拉框与滑条保持输入列起点，窄屏上下排列；全宽设置不再留空标签行。
- 页面标题、返回/分类和页脚复用已有 Heading、Action 和 Auxiliary 字号。删除独立封顶和冗余字号别名，不修改玩家预设值。
- 固定布局值集中到 Dimensions.axaml；移除 PlayerView 在 Measure 中对静态资源的写入、备份恢复、字体缓存及视口缓存。PlayerTypography 不再保留无人使用的独立窗口缩放参数；删除失效模板部件选择器，未增加模板或布局服务。
- 构建及相关 132 项集成回归全部通过，无跳过。新增跨页页头位置、实际 Tab 文字字号和内部编辑器列对齐验证，保留分类滚动、鼠标/键盘、编辑器实例、剧情工具栏、模态与存档预览验证。首轮两项旧尺寸上限失败和下拉框列内居中的修正见 phase-plan。
- 已检查中英文、默认/大字号、横竖窗口截图；证据为 output/layout-refinement/regression.trx、after/ 与 themes/。这些是 Headless 渲染和输入证据，不代表移动真机或用户主观视觉验收。
- 最后检查确认借用 shell 的移除及订阅释放仍保留；git diff --check 通过，文档校验通过 72 页与 321 处仓库引用。
- 正式说明已同步 [页面规范](../../docs/spec/presentation/game-view.md)、[Sample 手册](../../docs/manual/sample/gameplay-and-settings.md)与[定制手册](../../docs/manual/extensions/pages-and-themes.md)。经验：[限宽编辑器列对齐](../../.agents/lessons/L-20261102-10-bounded-editor-alignment.md)。

## P4 存档图片与模态背景修复

用户报告存档预览丢失、模态下首页文字几乎不可见，已复现并修正：

- 存档图片源有效，但实际被其后的激活按钮面色覆盖。预览 Image 改为在点击按钮之上绘制，并关闭图片自身命中；保存/读取、名称编辑、高亮继续有效，存档格式和预览文件不变。
- PlayerSurface 在退出/返回标题确认期间给底层区域添加 modal-background。Sample 只覆盖此状态下首页菜单的禁用前景色，保留单一遮罩；原 IsEnabled、播放阻断、焦点循环和 Esc 取消机制不变。正常非模态禁用样式不变。
- 新增三套配色实际预览像素+点击、模态文字颜色+鼠标/键盘阻断四项回归：修复前全部失败，修复后及相关测试共 106 项全部执行通过。已检查实际截图；证据位于 output/player-layout-polish/modal-preview/。
- 经验记录：[预览与点击层绘制顺序](../../.agents/lessons/L-20261102-09-preview-hit-target-paint-order.md)。

## P3 最终修订

用户认为首版首页和设置比原强制比例布局更差，最终明确只有首页根据横竖屏改变水平对齐，其他页面继续自适应。首版测试通过不能视为用户接受。

### 实际修改

- 普通页面用同一个 Viewbox 统一缩放同一个 GameShell，以 1600×900 为参考并限制为 0.65–1.25；移动端下限 0.7，低 DPI 增加 8%。页面设计空间保持实际纵横比，内容仍自然自适应，没有其他页面的横/竖版。
- 首页恢复标题在上、菜单靠下的原构图，仅横屏靠左、竖屏居中。按钮保持 380 设计宽和同一字体/高度关系，不再在方向切换时分别收缩标题与菜单。正文容器显式 Stretch，避免居中时菜单退化为最短内容宽度。
- 设置收拢为居中的 800 设计单位区域，页头/分类/表单对齐，标签列 260、间距 32，行宽不足 600 才自然上下排。数字与下拉输入宽度接近，滑条与输入列起点一致。保留分类滚动恢复和原编辑器。
- 游戏页及场景鉴赏使用实际视口，保持原工具栏及截图语义。页面缩放模式随导航变化，解绑取消订阅；原页面和状态实例不被重建。
- 移除旧 compact 样式分支；验证缩放后的起点与终点实际坐标，补充原按钮旋转保持、低 DPI 小幅放大及真实鼠标导航用例。

### 验证状态

最终 183 项回归全部执行并通过，0 failed、0 notExecuted；包含缩放后真实鼠标导航/分类/返回、旋转按钮保持、低 DPI 限幅与启动/清理。文档校验 72 页、318 处仓库引用通过。详细证据在 P3 phase-plan 中记录。截图目录为 output/player-layout-polish/revision/captures/、themes/；TRX 为 output/player-layout-polish/revision/regression.trx。已查看横屏/竖屏首页、设置以及大字号截图；真机 DPI/触控和软键盘仍未专项验证。几何及交互通过不代表用户已经认可视觉。

## P1/P2 首版结论（历史，视觉被用户否定）

保留当前配色、背景装饰、按钮外观和菜单顺序，先以统一内容边界、尺寸上下限、原地重排改善普通玩家页面。普通界面按 Avalonia 逻辑尺寸布局，不再随窗口连续缩小正文，不重复乘 DPI；用户已有 Larger/Large 字号档位修改原样保留。

## 已实现

- 设置移除额外灰底卡片和外层滚动。分类固定为单行，必要时横向滚动；只有当前分类的表单正文滚动。保留原生键盘切换及分类滚动位置。
- 普通页主体最大宽 1480；边距、页头和内容间距随实际空间有界变化。返回、分类、副标题、页脚分别限制字号，大字号下保留清楚层级和有效内容区。
- 首页主体最大宽 1440、高 560；菜单宽按主体的 28% 计算并限制为 240–320，按钮同宽。标题按可用宽高收紧、最多两行；菜单字号/高度/间距分别有上下限，矮窗口优先收紧后再滚动。
- 设置标签列从固定 220 改为 136–220 自适应；行宽 <420 才上下排列，保留同一编辑器和草稿。数字输入最大宽 160、下拉框最大宽 360、滑条最大宽 520，并保护可用轨道。
- 存档卡高度按视口限制在 156–210，列数继续随宽度减少；预览及名称/时间保留。
- 临时布局资源每键仅保存一次基线，解绑恢复；没有新增布局框架或页面状态服务。

## 验证

`dotnet test test/GalNet.IntegrationTests/GalNet.IntegrationTests.csproj --no-restore`，过滤 PlayerLayoutPolishTests、ResponsiveLayoutTests、PlayerTypographyTests、SampleTemplatePagesTests、GameThemeTests、SampleLocalizationTests、SampleInputSettingsTests、SampleSaveSessionTests、PageBackInputTests、PlayerInputTests。

- 最终 148 executed/passed，0 failed、0 notExecuted。构建使用本地 SDK、缓存和默认 bin/obj。
- 新增 11 个行为/布局用例：320×480、360×800、640×480、800×360、1280×720、1920×1080、3840×2160，分类滚动恢复、编辑器保持、原生键盘切换，以及中英文 Large/Larger 字号。
- 已检查普通/大字号与 Mono/Ocean 等渲染截图，修正竖屏长标题、滑条宽度及副标题层级。现有测试也覆盖三套配色、剧情操作、存档重命名、设置和返回行为。
- TRX：output/player-layout-polish/regression/regression.trx；日志：output/player-layout-polish/regression.log；截图：output/player-layout-polish/captures/、themes/。均不提交 Git。
- 编译仍报告已有 analyzer/Avalonia 运行时加载警告，本轮未全局压制。

## 发现和修正

初次分类滚动恢复只监听 AttachedToVisualTree，切换后丢失 120 的偏移。行为证据确认 ScrollViewer 被复用；现同步观察 DataContext/Offset，恢复期间防止过渡重置覆盖位置，延迟回调校验版本及身份。见 [lesson](../../.agents/lessons/L-20261102-07-tab-scroll-content-recycling.md)。

首轮几何验证和截图还发现数字框样式约束没有落到实际编辑器、左对齐的滑条会收缩成圆点；现通过项目现有编辑器注册入口设置限宽，滑条继续 Stretch，增加实际轨道宽度断言。没有改变字段提交和资源释放机制。

## 范围与后续

本轮完成普通玩家页面的首轮优化，不代表所有 UI/UX 问题已经解决。剧情对白、角色名、选项和 NVL 仍处于固定 1920×1080 画布，小屏有效字号偏小的问题保留；独立竖屏阅读区需要同时设计作者坐标、演出遮罩和截图合成。Android/iOS 真机安全区、软键盘、实际触摸与跨显示器 DPI 行为未专项验证，不能用 Headless 截图替代。

正式说明：[页面规范](../../docs/spec/presentation/game-view.md)、[玩家手册](../../docs/manual/sample/gameplay-and-settings.md)。
