# 验证与偏差记录

使用本机既有 SDK、NuGet 缓存和默认 bin/obj，未创建隔离环境。用户已明确允许共享工作区并行实施，提交分开。

- 修改前采集桌面 PNG 与工具栏几何，保存在 output/mobile-layout/before/；初版和最终修正后的桌面图均与之相同。
- 初轮编译纠正：AdaptiveSaveGrid 的 FindResource 缺 using；Grid.ColumnDefinitions 不是可样式化 AvaloniaProperty，标题列定义改在 Measure 中设置。未关闭编译检查。
- 首轮移动用例 5 passed / 4 failed：Headless 窗口未加载主题时没有 popup OverlayLayer；视口缩窄改用容器宽度测试，避免把异步窗口 resize 尚未 arrange 当成布局错误。修正测试夹具后通过。
- 首轮相关回归 121 passed / 4 failed：3 项播放按钮查询同时命中隐藏移动按钮，改为查可见控件；1 项音乐提示测试仍引用演出库已移除的 AudioToast。迁移到 TextCard API 后，通过现有 UI dispatcher 等待发布再检查时长。
- 增加延迟单击清理测试时，夹具的默认 CommitAsync 不会应用输入绑定；显式接入 Apply 并断言绑定提交成功，确保真正测试待双击判定的单击。
- 用户指出按钮过大后，移除整条底板、缩小视觉文字与标签背景，保留透明 48×48 命中区。初轮修正的 4 个测试失败定位为菜单挂载后 DynamicResource 覆盖当前字号，以及旧跨黑边夹具不再需要扩展；改为拥有菜单显示字号并缩短测试视口，最终专项 21 passed。
- Presentation 最终 255 passed，TRX total 263 / executed 255；8 项 Explicit 未运行。相关 Integration 129 passed；用户反馈后的专项 21 passed。记录位于 output/mobile-layout/{presentation-final,verified-revision,revised-final}/。
- 最终样本 PNG 与基线 SHA256 同为 98ef3b947f3a128a523f2852ff371c4d44b61c0cf0ca0ee8395c7bde26e7b4bb，几何一致。
- 未进行原生移动构建/安装、真实刘海/手势区域、完整竖屏阅读或全解决方案 CI。现有 AVLN3001 提示保留，不当作平台验收通过。

- 用户进一步指出文字底色难看、对白与底部间距偏大：删除文字底色，浅色文字加轻微黑影；命中高度 48→32，按钮纵向 margin 与工具栏底部 margin 归零，对白净空收紧到 4。新增文字透明、贴底和横屏净空断言。最新专项 21 passed / 0 failed，证据 output/mobile-layout/plain-tests/，截图 output/mobile-layout/plain/；桌面 PNG 与 before 基线逐字节相同。

- P4 用户要求桌面共用自适应：移除 IsMobile，统一实际视口 shell 与宽/高紧凑断点。新增无平台标记的桌面窄→宽→窄→宽测试，验证同一 shell、标题按钮、局部资源恢复以及完整工具栏恢复时关闭更多。
- 首轮跨平台专项发现 SaveColumns 资源缺失返回 UnsetValue，改用类型判断回退；原桌面状态背景断言同步为统一无底色/前景反馈。截图弹窗原专用尺寸预算不能再套普通控件 48 高，保留原有专用适配。
- 扩展回归中的点击失败经真实鼠标事件定位：工具栏测量后字号资源可覆盖 SetCurrentValue，WrapPanel 临时换行使坐标改变。按钮字号改由 GamePage 直接拥有，样式类与 margin 提前在 XAML 声明，完整栏改为单行 StackPanel，统一由宽度测量决定收纳；没有用直接调用命令代替真实点击回归。
- 最终相关 Integration 143 passed / 0 failed，证据 output/responsive-layout/regression/，日志 output/responsive-layout-regression.log。桌面 1280×720 与连续宽度变化截图保存在 output/responsive-layout/，已检查桌面标题画面。docs check 72 页/316 仓库引用通过；git diff --check 通过。
- 初版桌面整页像素一致要求已由用户的统一自适应方向取代，场景设计尺寸与对白横向构图仍保持。最新统一布局未重新打 Android 包；output/android-mobile-20261102/ 是此次跨平台改动前、与上一版证书一致的包。

- P5 用户要求全 UI/UX 复查：确认桌面脱离固定画布后直接使用设计字号、普通控件一刀切 48 高、toolbar hover 与 idle 同色、70 设计单位对白下限及竖向黑边造成距离、弹窗按钮模板状态层混用。对照 Ren’Py GUI 与 Windows 有效逻辑像素文档，见 ui-audit.md。
- 引入外部字号 .75–1 折算，Dialogue/Speaker/SceneBody 保留设计字号，ChoiceList 显式使用 SceneBody；项目按钮模板统一 36/紧凑 40、主次操作、焦点和 hover/pressed。工具栏 hover 增加颜色与预留下划线，锚定场景下沿，净空约 2。退出/返回/跳过确认和截图样式一起核对。
- 截图发现修改内层样式间距未真正生效：外层 Styles.Resources 合并 Dimensions 后覆盖 Player.axaml 重复定义。去除重复键并统一修改 Dimensions；设置标签换行、220 标签列/16 行距、分类跟随操作字号，标题装饰背景保持原 1920×1080 比例覆盖。
- 前置截图 28 项通过。整体相关 Integration 最终 173 passed /0 failed，TRX output/ui-audit/final/；最后固定画布字号/三配色补充 13 passed /0 failed，output/ui-audit/scene-fonts/；标题装饰比例修正后视觉与页面专项 46 passed /0 failed，output/ui-audit/visual-final/。这些用例有重叠，不相加作为独立测试总数。Docs check 72 页/316 仓库引用通过，git diff --check 通过。
- 视觉证据 output/ui-audit/{before,after}/，已查看三配色页面截图、实际宿主桌面设置/确认、移动更多菜单、标题/弹窗前后对照。审查范围为玩家页面、夹具媒体和合成场景，未进行 Editor 改造、真实 GameTestCase 全流程、原生 GPU/DPI 多显示器/真机触摸验收或新 Android 打包。用户原有 Larger/Large 配置值保持，提交仅包含 Apply 实现，不夹带原有档位改动。

- 最后逐行测量发现无错误的语言/字号字段仍各占 56 高：Context.Issue 为 null 时嵌套 IsVisible 绑定返回 UnsetValue，转换器没有执行，错误 TextBlock 保持默认可见并占 20 高。为该绑定增加 FallbackValue=False；实际语言行高回归约束 <=44，保留有错误时的消息绑定。此前只是缩 Margin，不能解决隐藏错误行的空白。探针已移除，最终相关用例重新验证于 output/ui-audit/complete/。

- 全部修正后的最终完整相关集成回归再次 173 passed /0 failed（output/ui-audit/complete/），包含无错误行高、固定画布选项字号、五视口确认/返回/跳过及 hover。最终设置截图已目视复查；文档检查和 diff check 通过。
