# Sample AOT 准备：自有 JSON 与静态访问路径

## 范围与授权

用户确认仅 Sample 需要移动端、iOS 为目标，并授权先改进自有 JSON 元数据等路径，即使 NativeAOT 尚未支持也保留可复用改进。本轮处理不改变产品语义的 JSON、主页面绑定和工厂发现；不迁移 Editor/CLI，不创建 iOS App，不为通过链接关闭警告或裁剪。桌面严格 NativeAOT 只用于发现共享代码问题，不替代 iOS 构建、签名和真机验收。

## 实现

- 输入设置增加私有源生成 JSON context，保持原有 PascalCase 属性、数字设备枚举、校验与无效输入恢复默认行为。
- 数值设置约束用已有源生成字典元数据，去除匿名 DTO 的反射序列化。
- 共享表单的 null、JSON 文本、对象/字典及数组操作统一走显式元数据 JsonCodec；数组临时列表转成已注册的 JsonElement[]，自定义 DTO 仍由字段描述的 JSON 元数据负责。
- 枚举选项和初始值使用 underlying-type 数组后 Enum.ToObject，避免运行时生成未知枚举数组；保留数值排序、别名和 ulong 值。
- GamePage 的八条运行时字符串路径换为类型化 CompiledBinding，继续跟随 DataContext 替换及属性通知。
- 删除 renderer 程序集扫描：当前唯一工厂需要 shader program 资源参数，不存在无参内置工厂。保留 DiscoverTextureFactories 公共入口返回空集；资源 shader 工厂仍由 GetProgramFactoryAsync 显式创建。

## 验证与边界

- 已在关闭 System.Text.Json 反射的测试进程运行 SampleAotPreparationTests，9/9 通过；涵盖旧 wire 格式、无效配置回退、数值上下界、ulong 枚举别名及页面 DataContext 替换。证据：`out/sample-aot-preparation/test-results/reflection-off.trx`、`reflection-off.runtimeconfig.json`。
- 普通 Presentation 完整回归 276 passed / 0 failed（设备与人工预览 Explicit 用例未执行）。证据：`out/sample-aot-preparation/test-results/presentation-regression.trx`。
- 改动文件的 dotnet format whitespace、IDE0005 imports 与 git diff --check 均通过。
- 最终严格 NativeAOT 未通过：ILC 报告 21 条最终链接诊断，不产出可发行的 AOT ZIP。日志为 `out/sample-aot-preparation/probe-final/logs/desktop-aot.log`，提取列表为同 probe 根的 `analysis-errors.txt`。本轮第一次成功进入 ILC 的探测为 23 条；移除动态枚举数组后，两条 IL3050 消失。该轮内差异仅用于确认枚举改动，历史约 60 条不作为固定基线比较。
- 初次受限环境的 MSBuild/ILLink task host 报 MSB4216，属于本地子进程/管道限制；放开本地验证执行后才进入 ILC 最终分析。测试夹具首次使用了其他测试类私有的 NoOp 导航类型，修正为真实无操作路径的 GameNavigationService 后通过。
- 当前工作区存在其他任务的共享源码修改，本轮没有覆盖这些文件。不能把历史约 60 条与本轮诊断数量直接当作固定源码的收益对比。

这些改动提高裁剪兼容性、消除对应反射调用；没有进行包体、启动或内存 A/B 测量，不宣称定量性能改善。

## 剩余阻塞

| 类别 | 当前最终诊断/位置 | 后续边界 |
| --- | --- | --- |
| 本地化与 XAML 模板 | PlayerTextExtension / PlayerTextBlock、GameShell 的 SkipPrompt 与多个编译 XAML closure 的反射 Binding，IL2026/IL3050 | 需要静态类型化的本地化接口与模板绑定上下文，不通过保留整个程序集解决动态代码要求 |
| 主题/调色板 | GameLayoutTheme、GamePalette 在 C# 中通过 URI 创建 StyleInclude/ResourceInclude，IL2026 | 为内置资源提供编译期引用/工厂，同时明确自定义主题的扩展接口，保留每个玩家根的资源隔离语义 |
| 页面构造与 DI | ViewTypes 集合的 Type 构造声明、Gallery 泛型注册、截图对话框的 Type 返回值，IL2072/IL2091 | 在注册和工厂边界传递正确的构造保留要求，不能只在消费端压警告 |
| 通用结构表单 | StructuredFieldEditor.InitialValue 仍对未知值类型 Activator.CreateInstance，IL2072 | 需要显式初始值工厂，兼容自定义 struct 的无参构造、字段默认值及现有编辑语义 |
| 第三方库 | Serilog 的对象属性解构、Ursa EnumSelector 的枚举字段反射，IL2072/IL2075 | 评估版本支持、可替换路径或准确的库级适配，不全局吞诊断 |

本轮有边界的准备与验证已完成，完整 Sample NativeAOT/iOS 支持仍未完成。Windows 编译通过本身也不能替代后续真实 UI/音视频、Mac/Xcode 与 iPhone 设备验收。
