# Android 更新先核对实际签名证书

- 状态：confirmed
- 来源：F-20261029-05-cross-platform-applications / P6e
- 范围：本机 .NET Android 36.1.69 的开发候选更新；不涉及生产密钥分发

现象：新的正常源码 Mono AOT APK 已通过完整内容/native/对齐检查，但 `adb install -r` 返回 `INSTALL_FAILED_UPDATE_INCOMPATIBLE`，明确报告新旧签名不同。

确认：官方 apksigner 对实际 APK 的证书检查显示，手机对应旧候选的 SHA256 以 d2c39063 开头，新构建默认候选以 9b82c724 开头。既有开发 keystore 中的证书与旧候选一致。显式设置 AndroidKeyStore、AndroidSigningKeyStore、AndroidSigningKeyAlias 及相应口令后，新候选实际证书恢复为 d2c39063。没有断言默认密钥差异究竟由哪个环境变量或 SDK 路径造成。

正确做法：覆盖安装前用 `apksigner verify --print-certs` 核对实际 Signed.apk 与已安装候选的证书；需要保留数据时使用既有密钥签名，并重新检查实际输出。APK hash、包名和 Signed 文件名都不能代替证书身份。口令从合适的本地配置/安全来源传入，不把私钥或口令加入源码、日志示例或发行清单。

禁止做法：为了绕过签名不匹配直接卸载或清空玩家数据；因本机都叫 debug.keystore 就假定不同构建上下文必然使用同一把密钥；未经核对就把这种失败归因于 USB 安装权限。

证据：`out/cross-platform-p6e/android-install.log`、`android-existing-signer.log`、`android-default-key-certificate.log`、`android-explicit-signer.log`。设备安装与运行结果独立记录在 feature execution。
