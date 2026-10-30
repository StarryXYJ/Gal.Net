# Android 设置、交互截图与包体复验

- 日期：2026-10-30。
- 范围：用户报告截图无弹窗、设置崩溃和双 ABI 包体偏大；随后确认原修复可用，并要求移动截图省去路径选择。
- 状态：本次 Android 16 / ARM64 切片已验证；不代表 P6d/P6e/P7 全部完成。

## 修正

1. CrossPlatform 根从普通 ContentControl 改为 UrsaView，提供 OverlayDialogHost。截图按请求 Owner 的 TopLevel 身份定位弹层，避免缺宿主静默返回或显示到其他窗口。
2. 真机历史崩溃明确为 ReadTextColorEditor 动态 StyleInclude 找不到 ColorPicker 预编译 XAML。改用具有编译期 StyleInclude 的 ReadTextColorPickerStyles；没有关闭裁剪或 AOT。
3. Android 注册 IAutomaticGameScreenshotDestination，通过 MediaStore 自动命名并写入系统相册。三套内置布局隐藏目录和文件名，限制移动预览尺寸并响应屏幕变化；桌面保持原行为。失败删除本次未完成条目并允许重试；旧系统存储权限分支仍待设备验收。
4. 支持单数 android-arm64 RID，并同步到当前固定 VLC 包读取的 RuntimeIdentifiers。首个候选因为漏带 VLC 被载荷门禁拒绝，没有安装或计为体积优化。
5. 包检查脚本支持显式 --abi，并验证实际 ABI 集合、完整内容、媒体/音频库、AOT 和 ELF LOAD 对齐，新增体积统计。

## 证据

证据根：`out/android-ui-fixes-validation/`。

| 检查 | 结果 |
| --- | --- |
| 设置 | Release 既有显示/已读颜色回归 3/3；真机进入设置、开关及 ColorView 弹层通过，用户确认可用 |
| 截图 | 截图相关自动化 9/9，含三主题 853×384、384×853、700×320 布局、平台保存重试、完整 PNG 输出及原桌面行为 |
| 真机 | REDMI K80 Pro / Android 16 / ARM64，覆盖安装保留 profile；设置、截图弹窗、剧情显示及相册保存通过 |
| 相册 | MediaStore 条目 1000012911，is_pending=0，PNG 1920×1081 / 3,078,082 字节；已拉回检查画面，无对白或截图弹窗叠入 |
| 内容 | 嵌入 galpak SHA256 保持 c5e41778083ba7c5e4c77bf6bd525acb1693309692d9cab71714d7a22fb89bee |
| APK | 最终 SHA256 74c26f1c68f3d8dcde755b6d721472d1b035adde4253eea68da3d38922326e98，ARM64 114 程序集 Mono AOT，媒体/音频库与 16 KB ELF/zipalign 检查通过 |
| 包体 | 旧双 ABI APK 134,155,142 字节，ZIP 未压缩载荷 299,305,745 字节；新 ARM64 APK 78,191,374 字节，载荷 163,229,249 字节 |

载荷报告保留 deviceTested=false，独立真机证据另存；不把包检查标记为设备验收。包体指 APK 与 ZIP 载荷，不是 Android 设置页统计的实际安装/缓存占用。

## 验收边界

- 本机 NDK 缓存此前已移除。本次设备诊断包用显式临时 targets 复用与旧已验证 APK 逐字节核对的 ARM64 miniaudio，SHA256 为 02b9fb0a54dc4d29112d5b7c474b3c8ac97b62927da5d7ac185c2f264af2b0f3；未修改原生源码，不宣称完成新的 NDK 源码构建。
- Android 6–9 权限允许/拒绝流程、x86_64 新 UI、iOS 相册与完整移动矩阵仍待验收。
- 使用实际 Ursa/Semi 动画模板的两个早期 headless 弹窗探针曾挂起，已结束自有进程并以无动画状态测试与真机验证分层取代；没有把超时计为通过。
- 早期横竖屏断言在窗口尺寸事件完成前读取旧坐标；排空事件后同一三尺寸用例通过。
- 单架构发布会给共享锁图带入 RID；本次恢复正常 Debug 图并验证 locked restore，未提交这些临时 RID 节点或改动包版本。

## 防回归

- 运行时构造的 URI 样式加载不能由桌面无裁剪测试证明可用于移动发布；优先编译期引用并复验实际 AOT/裁剪包。
- 单架构产物必须查实库和 ABI，不能只比较 APK 长度；当前 VLC 包的原生筛选只读取复数 RID。
- UI 截图必须等待页面或系统窗口动画完成；Android 熄屏截图不能作为应用黑屏证据。
