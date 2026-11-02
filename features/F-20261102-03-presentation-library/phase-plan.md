# 阶段计划

## P1 — Shader 与遮罩资源
状态：verified。增加 8 个 shader（默认库合计 16 个）及 5 个天气纹理用途；metadata/SkSL、默认恒等、羽化/反向/开闭像素检查通过。contact sheet 已输出并目视检查。原生 GPU/移动设备另列未验。

## P2 — PCM 效果
状态：verified。增加 Delay、Pan、EQ、Distortion、Bitcrusher、Compressor。Media.Audio 45 项通过；覆盖尾音、频率响应、声像、压缩、默认参数契约、极值、无分配、重置及 duck。2 项既有原生设备 Explicit 未运行。

## P3 — 文本服务与遮罩呈现
状态：verified。九宫格文本原语/服务、App 主题和音乐迁移完成；覆盖面透明合成，系统确认在上。呈现测试及三套配色音乐卡集成通过，覆盖替换/取消/等待和动画方向。原生 UI 层级人工验收未执行。

## P4 — 音频编排
状态：verified。六类 Timeline 音频事件、audioTracks 与 audio.duck 完成。共享帧回调、跳过不补声音、逻辑值快照、Headless 循环首帧/停止清理及源声音暂停后的恢复均有测试；作者用 Timeline 数据配置音频轨道，未增加专用音频曲线面板。

## P5 — 转场和剧情预设
状态：verified。9 个预设/组合 Entry 与 4 个转场完成。强调从当前值衔接，显式目标列表、视差和音频轨道引用在复制时重映射。Builtins 450 项通过，包含编译、快进和整组状态执行/存档/清理。

## P6 — GameTestCase 与收尾
状态：verified。GameTestCase 新增独立 presentation-library 组，接在 ending 后；139 个作者 Entry、54 条中英文说明，编译为 143 个原语。项目完整校验零诊断、目录导出成功；三个契约 DLL 与 Sample 目标逐一验 SHA256。Builtins 450、Editor.Shared 621、Presentation 250、Media.Audio 45、Architecture 56、音乐卡集成 3 项通过；另显式运行预览 1 项。文档/lessons/总结已同步，原生与性能验证边界见 summary。
