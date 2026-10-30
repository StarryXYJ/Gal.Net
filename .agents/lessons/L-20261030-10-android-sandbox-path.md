# Android 沙盒路径先规范化，再执行内容目录检查

- 状态：confirmed
- 来源：F-20261029-05-cross-platform-applications / Android 真机基础 smoke
- 范围：Android 平台提供的应用私有目录；本轮 REDMI K80 Pro / Android 16 / .NET 10

现象：APK 安装与 Activity 启动成功，初始内容安装报 `Extension directories cannot be symbolic links`，尚未进入语言选择。此前桌面测试和 APK 静态检查都通过，不能替代真机内容安装。

发现与修正：Android 宿主从 `FilesDir.AbsolutePath` 改为 `FilesDir.CanonicalPath` 后，同一设备的新 Release Mono AOT 候选进入语言选择和剧情视频，用户确认声音与触摸正常。目录保护仍逐级拒绝重解析点，没有关闭检查或放宽内容契约。原失败未记录具体祖先，不推断所有 Android 版本的系统别名布局；把平台根规范化放在宿主边界。

诊断规则：内容准备失败可能早于 profile logger 创建，此时必须有标准错误/logcat 回退；目录链接异常应包含触发检查的具体路径。保留祖先链接拒绝的回归测试，不能用路径规范化代替不可信内容内部的链接检查。

证据：feature execution.md 的 P7b；`out/android-device-validation/first-launch.png`、`path-fix.png`、`path-tests.log`、`device.json`。本轮仅验证 ARM64 设备基础运行，不代表 Gallery、存读档、生命周期、x86_64 运行或商店发布完成。
