# Android RID 回退会收集桌面 Linux 原生资产

- 状态：confirmed
- 来源：F-20261029-05-cross-platform-applications，P6 共享玩家与 Android 接入
- 范围：SDK 10.0.401、Microsoft.Android.Sdk.Windows 36.1.69、SDL3-CS 3.4.18.1

共享 net10.0 库传递 SDL3-CS.Linux 时，Android SDK 的 RID 回退从 linux-x64 选择 glibc libSDL3.so，首轮 APK 生成成功但报告 XA0141（16 KB 页大小），project.assets.json 明确列出该 Linux native。编译成功不能证明该库可在 Bionic 上运行；正式 SdlGamepadSource 也明确拒绝 Android，因此不应携带此桌面库。

Android 宿主对 SDL3-CS.Windows/Linux/MacOS 显式添加同版本顶层 PackageReference，ExcludeAssets=all、PrivateAssets=all，阻止其桌面 native/build 资产进入当前移动宿主；保留中立动作和托管适配程序集。复建后无 XA0141，签名 APK 的 28 个 native 条目中没有 SDL3、桌面 VLC 或 miniaudio。桌面消费者不受该宿主局部规则影响；后续移动输入必须部署真正的 Android ABI 库和平台初始化，不能据此声称移动手柄已支持。

禁止通过屏蔽 XA0141、直接复制 glibc so 或仅检查 APK 是否生成来判断原生部署成功。检查目标资产图及实际 APK/native 清单。

证据：out/cross-platform-current-sdk/android-debug-build.log（初次）、android-debug-build-2.log（修正）；签名 APK 位于 CrossPlatform.Android/bin/Debug/net10.0-android/。阶段完成后精简归档在跨平台 feature 的执行记录索引。