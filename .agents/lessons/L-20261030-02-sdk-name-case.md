# .NET SDK 标识大小写必须由原生宿主验证

- 状态：confirmed
- 来源：F-20261029-05-cross-platform-applications，P4/P5 应用发布矩阵
- 范围：.NET SDK 10.0.401；Windows 与 Ubuntu 24.04.3 / WSL

## 现象与根因

Editor 工程声明 Microsoft.NET.SDK。Windows 的 SDK 路径不区分大小写，Windows 构建及 Windows 到 Linux 的 cross-publish 均成功；实际 Linux SDK 的大小写敏感目录为 Sdks/Microsoft.NET.Sdk，SDK resolver 无法找到大写 SDK，报 MSB4236。

## 验证与修正

以官方 SHA512 校验的隔离 Linux SDK 和无 Windows bin/obj 的源码快照实跑，首次 Editor restore 失败；改为标准 Microsoft.NET.Sdk 后，locked restore 与 Editor/CLI/Player/Developer 四种普通自包含发布通过，原生库架构、私有 SDL/许可、同宿主共享契约检查通过。

证据：out/linux-sdk-work/run-matrix.log 的首次错误，最终归档在 out/cross-platform-validation/20261030-applications/；来源记录包含 SDK 元数据、源码快照 SHA256 及明确的单行补丁。跨目标成功只验证目标载荷，不替代目标操作系统作为构建宿主的验证。不要放宽 SDK 版本锁或自动降级为其他 SDK 来绕过解析问题。
