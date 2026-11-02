---
status: confirmed
feature: F-20261102-03-presentation-library
---

# Shader 颜色与颜色图层的八位十六进制顺序不同

现象：ShapeMask 使用 #FF000000 作为黑色，完全闭眼像素测试却没有产生不透明黑色。

根因：TextureEffects 的 shader color 参数解析为 #RRGGBBAA，#FF000000 是透明红色；Layer 采用 #AARRGGBB，Particle 与 shader 一样采用 #RRGGBBAA。两种格式当前并不统一，不能从颜色图层直接复制八位值到 shader 参数。

修正：不透明颜色优先用六位 #RRGGBB；需要透明度时按各自契约传值。ShapeMask 默认改成 #000000，文档明确两种规则，不改写既有 shader 色彩契约。

验证：PresentationShaderPixelsTests 的闭眼测试要求全部像素 alpha=255 且 red=0，修正后通过；contact sheet 已目视检查。

适用：shader color 参数、遮罩颜色、从 Avalonia/粒子值生成 shader JSON。禁止把 #AARRGGBB 默认假定为全项目通用格式。

2026-11-02 天气验收更正：原记录将 Particle 归到 ARGB 是错误推断。实际 ParticleEmitter.TryParseColor 八位值为 RGBA。错误粉色在预览中呈黄绿色，现已用实际渲染像素检查 R>B>G 与透明雨滴 alpha 回归确认；不得只断言作者颜色字符串。见 F-20261102-05 的 P3。
