# 原生音频的 UTF-8 ABI 与 Windows 文件打开

状态：confirmed
日期：2026-10-27
来源：跨平台引擎后端适配（独立实现，未建立 feature 工作流）

## 现象与根因

把 C ABI 路径统一为 UTF-8 后，不能直接用 Windows 下 miniaudio 的 ma_decoder_init_file / ma_fopen 代替原来的宽字符入口。已固定的 miniaudio 0.11.23 在 Windows 的窄字符 ma_fopen 内调用 fopen_s，使用本机 ANSI 代码页，不保证按 UTF-8 解释路径；跨平台 API 的同名窄字符串入口不等于统一编码语义。

## 发现与证据

检查 src/Infrastructure/GalNet.Media.Miniaudio/native/miniaudio.h 的 ma_fopen 实现确认上述行为。本轮将边界统一为 UTF-8，在 Windows 通过 MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS) 转换，再调用 ma_decoder_init_file_w / _wfopen；其他系统使用窄字符路径。Unicode WAV 与 Vorbis 解码回归通过；真实设备回调、暂停重建设备和释放测试通过。

## 正确做法

- 在 C ABI 明确字符串编码，平台内部再转换；保留中文路径的真实解码测试。
- 路径编码和回调签名变化时提升原生 ABI，并同时部署托管适配器和原生库。通过版本入口拒绝混用；不能只替换托管 DLL。
- AOT 回调使用静态 UnmanagedCallersOnly 入口和显式上下文。原生设备销毁等待回调退出后，才释放 GCHandle；系统暂停不释放声音租约或修改剧情 Pause。

## 范围与禁止做法

适用于原生媒体库的字符串互操作和回调生命周期。不要把 CMake 编译成功当作移动设备验收，也不要把静态回调改造当作全引擎 AOT 支持。当前反射 JSON/metadata 的 IL 警告仍存在，详见 docs/manual/extensions/media-and-platform-backends.md。
