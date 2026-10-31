# 单项目可定制 Sample：实现总结

状态：P1–P5 verified；P6 桌面及交付验收已实施，Android 本轮真机验收待环境。实现已落地，不能据此宣称全部平台验收完成。阶段原始证据与失败记录见 [execution.md](execution.md)，分阶段退出条件见 [phase-plan.md](phase-plan.md)。

## 最终结构

- 唯一 `GalNet.Sample.CrossPlatform` 应用项目拥有全部普通页面 XAML、页面 VM、菜单/工具栏、导航策略、截图及全局语言/确认/公告布局。平台外壳仅保留入口、原生部署和生命周期接线；项目契约仍独立交付。
- 内置 `GamePresentationState` 拥有场景、对白/NVL、选项等待、自动/快进、历史及交互阻塞。`SaveSlotCollection` 和 Gallery 状态拥有存档行、预览、媒体、取消和资源释放；Sample 组合这些对象，不复制运行机制。
- 宿主通过 `IPlayerApplication` 接入项目 UI 与启动/返回策略，继续负责 scope、初始化取消与释放。`GameLaunchFlow` 不选择具体应用页面；`PlayerSurface` 提供无页面布局的行为基类。
- 删除 `IGameLayoutTheme`、Classic/Minimal/Paper 三套整页主题、旧默认页面 VM 及过渡适配。`GalNet.Avalonia.Themes` 只保留配色/字体，无 GalNet 项目引用。应用尺寸、装饰和样式由 Sample 源码拥有；控件库保留默认控件模板及必要部件契约。
- 普通 VM→View 一条映射自动注册双方 Scoped 服务；只校验实际启用的运行控件契约，不要求固定页面全集。Sample 游戏 surface 解析实际注册映射，并与页面工厂共享同一个实例。

## 开发者定制成本

改按钮只编辑页面 XAML；增加业务行为再编辑对应 VM。新页面添加 View/XAML、VM、映射及导航调用。配色改 `PlayerPalette`，单按钮可直接声明局部 Avalonia `ControlTheme`；游戏工具栏及承载布局也直接改项目 XAML。未来新设计通过新的源码模板提供，不建立运行期模板切换协议。

仓库外探针使用真实本地 NuGet 包，五项定制共修改/新增 8 个应用文件，10 项测试通过。内置库源码零修改，没有派生旧默认页面 VM 或复制播放器状态机。步骤与边界见[定制手册](../../docs/manual/extensions/customize-sample.md)、[页面与配色](../../docs/manual/extensions/pages-and-themes.md)及[探针说明](probes/README.md)。

按基线 `d2d8665a` 到实现 `ba1fd764`，统计 GameView、GameHost、Themes 与 Sample 主项目全部 C#/XAML（含空行/注释，不含测试、锁图、文档和平台入口）：13,215 → 12,086 行，净减 1,129 行（8.5%）。其中 Themes 2,899 → 262，Sample 832 → 2,559。Sample 增加的是开发者可以直接编辑的应用代码；复杂机制仍在内置层。不能用删除三套布局的总行数当作净收益。

## 验证与交付

- P5：呈现 219 passed；集成 334 passed / 1 既有 Editor dirty 失败；架构 56 passed；真实 Sample/媒体 6 passed。默认、R2R 和 14 组平台/调试锁图通过。Browser 两份隐式 SDK 包哈希差异如实保留，详见执行记录。
- P6：六个实际 NuGet 包及标准独立消费者通过；仓库外模板 10/10。Windows、原生 Linux 的 Editor/CLI/Player/Developer 发布与各自严格契约闭包通过，没有上传或发布公开版本。
- 定制模板真实 Windows HWND 与 Ubuntu 24.04.3 / WSLg XID 窗口均通过剧情渲染、261,359 字节存档预览、存档/改名/读取、暂停重挂载、scope 重建后读档、退出确认取消/接受。驱动调用实际控件绑定命令，不是操作系统鼠标全流程自动化。
- Linux 未改造的 Player/Developer 发布结果均通过无 SDK 发现的独立启动、会话、进度落盘与窗口关闭确认；加载私有 CoreCLR、VLC 和 SDL。WSLg 不等于原生 Linux 桌面发行验收。
- Android Release 正常 NDK、双 ABI 各 114 Mono AOT、完整 galpak 字节一致、所需原生库架构及所有 ELF LOAD 至少 16 KB 对齐通过。未安装或覆盖设备，`deviceTested=false`。
- 完整 Release CI：15 个测试项目合计 2,538 passed / 3 failed，2,541 executed / 2,562 total。失败为历史 Editor 文案白名单、复选框居中及保存后 dirty；不宣称全绿，也不跳过它们。21 项未执行与显式原生专项分开记录。

## 偏差、限制与后续

1. 为保持架构测试方向，依赖 Sample 的页面测试从 Presentation 移到 Integration，没有放宽依赖规则。删除旧三布局重复参数，保留单模板 × 配色/尺寸/交互断言。
2. 跨 OS 原生源码发布与 Windows NuGet 包的共享契约不保证字节一致；Linux 包消费者显式使用已冻结的项目契约与对应内容，Linux 原生 CLI/发布使用另一套内部一致的契约。严格校验未关闭。
3. Linux 探针曾遗漏正式入口的 Inter 接线，随后在 WSLg 出现原生崩溃；补齐正式字体配置和已有 d3d12 环境配置后完整流程通过。保留中间失败，未修改产品渲染策略，未将崩溃根因过度归结为单一配置。
4. Linux 媒体专项首轮有一次音频暂停位置为 0 的失败；独立两项音频重跑通过，完整五项复跑也通过。原因尚未确认；保留这次瞬态异常，不将重跑当成根因已修复或长期稳定性证明。
5. 当前 ADB 无设备；P6 Android 迁移后的真实挂载、后台重进、返回和暂停落盘仍须完成。此前旧 UI 的真机记录不替代本轮结果。macOS/iOS、原生 Linux 桌面与真实手柄沿用既有延期，未扩大承诺。
6. 模板源码归开发者维护，旧主题 API 消费者需迁移到项目页面；没有模板自动升级合并器。PAK、存档、profile 身份和契约格式未改变。

稳定架构事实已同步 [GameView](../../docs/spec/presentation/game-view.md)、[GameHost](../../docs/spec/presentation/game-host.md)、[模块](../../docs/spec/architecture/modules.md)及[生命周期](../../docs/spec/architecture/lifecycles.md)。正式文档以当前实现为准，历史设计保留过程与取舍。
