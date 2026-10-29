# 自审

结论：pass-with-follow-up。打包范围内未发现阻止交付代码的问题；桌面原生字体崩溃的跟进仍开放，不宣称所有桌面验收通过。不声称独立 reviewer 审核。

- 核对固定入口与六个文档、文档专用 GPAK catalog、project/build 身份和 content/index 文件集合一致性。正常资源覆盖无法进入文档加载入口。
- 核对内置文本按作者相对路径 ordinal 合并、每文化一个条目、空字符串保留与父文化回退；非 JSON 作者说明和 External 不导出。默认语言完整性先于外置来源加载，损坏外置文件以诊断跳过。
- 核对普通解压、目录导出、安装三路径的空目录保证，以及安装复用保留外置文件、损坏入口/载荷拒绝和暂存发布行为。v1/v2 与旧 content v2 读取测试保留。
- 核对默认 profile 稳定 projectId 摘要；InvalidDataException 在身份探测处回退，使正式启动内容校验继续报告损坏。
- 修改旧测试时仍验证自定义契约编译产物、自定义资源 metadata 和未知配置往返；仅将旧 ZIP 根路径与散 JSON 入口改成当前格式。CLI inspect 不要求项目/契约且只读。
- 检查模块引用方向与新增 DTO 字段；Core 继续无平台依赖，文档编码归 Assets，文件定位/安装/加载归 Storage.FileSystem，Sample 只选择启动位置与用户 profile。
- 隔离快照完整 solution 构建成功，7 个相关测试项目共 969 项通过。修改文件的 whitespace/IDE0005 检查及 git diff --check 通过。发布保护 7 项通过。
- 七个发行 ZIP 的载荷验收 passed，实际两种 GameTestCase 内容加载 2 passed；PowerShell 5.1/7 使用真实分发 CLI 的 galpak 整合均通过。native-comparison/results.json 显示两种新版玩家均初始化完成且运行时正确。

## 待跟进的原生验证问题

组合桌面 smoke 在 SelfContained 玩家初始化时出现过 `SkiaSharp.SKStream.Read → SkiaTypeface.TryGetStream → TryCreateSyntheticGlyphTypeface` 的 0xC0000005，见 desktop-smoke-confirmed/SelfContained/player-stderr.log。原封不动的相同发行文件在后续对照中初始化成功；旧版对照也成功，因此尚不能断定属于旧问题或已修复。不修改字体行为，也不因后续单次成功删除失败证据。风险是发布环境仍可能遇到字体初始化崩溃，正式发布前应单独定位并复核。

另一次组合 smoke 的 CLI 导出暂存目录曾报 Access denied，同路径独立重试成功，见 desktop-smoke-final.log / desktop-export-retry.log；未确认根因，不当作代码修复完成。验证脚本补充了成功/失败两分支的进程输出与 profile 日志保留，测试仍正常清理自己的唯一 profile。

范围与限制：压缩容器不是加密；当前整包缓冲与按语言逻辑条目不等于按需流式读取。没有新增更新/合并覆盖协议，没有 BSON、AOT 或体积专项优化。并行工作区中的对话历史和 Editor 默认契约改动未纳入本次源码快照或自审。
