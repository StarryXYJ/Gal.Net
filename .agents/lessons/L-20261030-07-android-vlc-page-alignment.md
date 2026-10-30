# Android 官方 native 包版本不代表 16 KB 页支持

- 状态：confirmed
- 来源：F-20261029-05-cross-platform-applications / P7a
- 范围：SDK 10.0.401、Android SDK 36.1.69、VideoLAN.LibVLC.Android

现象：官方稳定 3.6.5 的两种 64 位 ABI 的 libvlc.so/libc++_shared.so 仍只有 4 KB ELF LOAD 对齐，APK 可生成但出现 XA0141。这是实际 Android native 的页支持问题，与此前 glibc SDL 回退不同，不能用“包来自官方”或本机 4 KB 设备可能运行来推断满足 16 KB 系统。

发现方式：保留首次构建 log，使用固定 NDK r29 llvm-readelf -l 核对所有 LOAD；检查最终 APK 里的实际 native，而非仅看 NuGet/工程版本。官方 3.7.0-beta 的内嵌 LibVLC 为 3.0.22-rc1，其两 ABI libvlc/C++ 均为 0x4000；miniaudio 显式 -z max-page-size=16384，实际 APK 每个 native 也核对架构和页对齐，并用 zipalign -c -P 16 4 检查归档。

正确做法：固定可复现版本并保留 SHA256/诊断；若选预发布依赖，明确记入交付限制并继续设备验证及稳定替换。ELF 与 ZIP 对齐、设备解码和商店发布是不同证据。禁止压制 XA0141、仅改 ZIP 对齐或只检查自己编译的音频库就宣称整个 APK 已支持 16 KB。

证据：out/cross-platform-p7-tools/android-build.log、android-mono-publish-final.log、android-package-verification.json；可复现检查为 feature probes/mobile/verify-android-package.py。该报告明确 deviceTested=false。
