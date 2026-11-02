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
