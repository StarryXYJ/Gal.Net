# 设计

这是涉及归档布局和多入口发行的普通分阶段实现，不采用快速通道压缩验证。

## 内容

galpak v2 全部 ZIP 内容位于 pak/；manifest 本身位于 pak/<name>.galnet，但 Files 中路径仍相对内容根。PAK/content/save 格式与构建身份算法不因运输外层改变。v1 平铺包继续读取。显式安装目标始终代表内容根；v2 默认目标为归档旁 pak，返回实际内容根，不追加第二层 pak。目录导出的显式输出同样代表内容根，发行脚本传入 <app>/pak。

剧情分片保持独立分发；整合发行将匹配的外部 StoryPatches/ResourcePatches 合入 pak。所有内容校验和拒绝覆盖原则保留。新版归档拒绝 pak 之外载荷、混合布局和重复 ZIP 路径。

## 启动与玩家数据

Sample 显式分离应用根与托管程序集根；发布后依赖目录是 lib，pak 相对应用根定位，与工作目录无关。开发版可覆盖内容与 profile；玩家版不编译通用参数解析。EnableLaunchArguments 默认跟随 EnableGameDebugging，两个符号保持独立，调试接入要求参数能力。

默认 profile 位于用户 LocalApplicationData/GalNet/<project identity digest>；已打包内容使用 content.json 的 projectId，不包含 buildId 或安装路径；开发内容缺少身份时使用目录身份。无有效身份的启动失败使用独立默认日志目录，内容错误由窗口原有启动错误流程报告。旧 profile 不自动迁移。平台共享会话默认策略不变，由 Sample 组合根显式传入 profile。

## 发行

使用 SDK CreateAppHost 任务生成目标为 lib/<app>.dll 的原生启动器，整个依赖树移动至 lib，不引入第二个进程或托管解析器。Editor/CLI 两个入口共用同一个 lib，保留编译产物契约字节。DefaultContracts 随程序集位于 lib；作者 Skill 保留根 Skills。docs 保存 LICENSE、README、release-manifest（路径仍相对应用根）。

发布入口提供 Player/Developer 两种 SampleFlavor，均使用 Release 优化编译；Developer 开启调试与参数并保留 PDB，产物名称和清单显式区分。正式 Release 默认 Player。不更改三份契约 IL 的编译条件。

先验证 apphost 子目录定位及原生库，再验收两种部署，避免仅验证托管代码能编译。macOS/Linux 目录策略保留组合根可扩展性，本轮仅承诺 Windows 发行。
