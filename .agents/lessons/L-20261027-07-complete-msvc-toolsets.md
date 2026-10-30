# Windows 原生构建选择完整的目标工具集

状态：confirmed
日期：2026-10-27
来源：跨平台后端 ARM64 构建复验（独立实现）

## 现象与根因

用户补装了 ARM64 MSVC 14.44，原生音频构建仍报告缺少 ARM64 libcmt.lib。默认 vcvarsall 选择 14.51；该版本只有 ARM64 的部分 Clang 辅助库，完整的 ARM64 编译器、头文件和 CRT 位于并存的 14.44。默认工具集不完整不等于机器上没有可用工具集。ATL/MFC 的 ARM64 安装目录也不能证明编译工具和 CRT 已装齐。

## 发现与正确做法

检查实际 lib/arm64/libcmt.lib 和 bin/Hostx64/arm64/cl.exe；不要只看目录存在或安装器显示的 ARM64 关键词。按目标架构筛选同时具有编译器、头文件、静态运行库的工具集，按版本排序后通过 vcvarsall -vcvars_ver=<完整版本> 显式选择。不同目标架构可以使用不同工具集。

## 验证

native/build.ps1 的 ARM64 构建选择 MSVC 14.44.35207 并链接成功，PE Machine 为 0xAA64，8 个 gn_* ABI 导出齐全；x64 构建仍选择 14.51.36231，PE Machine 为 0x8664。此结果证明交叉编译与产物身份，不代表 ARM64 实机播放通过。

## 适用范围与禁止做法

适用于多版本 MSVC 并存的原生构建。不要在未检查并存版本前反复要求用户安装组件，也不要为迎合默认工具集混用不同版本的头文件和 CRT。当前接入事实见 docs/manual/extensions/media-and-platform-backends.md。
