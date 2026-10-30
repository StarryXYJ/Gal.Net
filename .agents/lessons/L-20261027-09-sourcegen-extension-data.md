# 源生成 JSON 与 init extension data

- 状态：confirmed
- 日期：2026-10-27
- 适用范围：System.Text.Json 源生成 metadata，含 JsonExtensionData 的 DTO
- 来源：引擎 AOT 实现，GalleryAnnotation 未知字段保留回归
- 文档：docs/manual/publishing/native-aot.md、docs/spec/content/assets.md

## 现象与根因

GalleryAnnotation.ExtensionData 使用 init 时，源生成器为该类型生成初始化构造函数并试图绑定 extension data，触发不受支持的构造参数绑定。普通反射路径未暴露这一差异，原有未知字段保留测试在切换生成 metadata 后失败。

## 正确做法与验证

该可变字典属性使用 set，保持 JsonExtensionData 和 wire 不变。原有 Gallery 测试通过；禁用反射 JSON 的 NativeAOT 自检再验证未知 annotation 字段写出、读回。对类似 DTO 不应只验证已知字段，也不要只以成功编译判定生成 codec 可用。

本规则针对已复现的 extension data 形状，不推广为所有 init 属性均不支持源生成。
