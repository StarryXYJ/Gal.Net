# L-20261030-22-apt-mirror-download-plan

- 状态：confirmed
- 来源：features/F-20261029-05-cross-platform-applications/release-integration-20261030.md

GitHub Ubuntu runner 的 apt --print-uris 可输出 mirror+file 伪 URI；只筛选 http/https 会丢失整个计划。按 apt 四字段下载记录解析，再用同一已签名索引中的 Filename、SHA256 与 size 从 Ubuntu 归档下载；不能跳过索引校验。远端已完成媒体准备和原始发布。libEGL/libGLES 等图形 loader 是宿主前置条件，CI 须安装后检查 native 闭包，不把构建机 GPU 驱动打包进去。
