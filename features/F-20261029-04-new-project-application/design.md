# 设计

- `NewProjectPanelViewModel.ApplicationPath` 驱动自动定位，仅契约输入为空时填充；同时支持浏览与文本赋值。只检查当前 EXE 同级和 lib 两处固定位置，不递归扫描、不猜测多份结果。
- Shared 的 `ProjectContractSource` 使用 PE 元数据递归解析程序集引用，排除当前 .NET 平台程序集；其余引用必须存在于来源目录。用 `ProjectRawReader` 保留目录链接与路径逃逸限制。
- 自定义来源在创建前后台制作独立临时契约快照，并以既有 `ProjectSchemaContext` 验证。既有 `IProjectService.CreateAsync` 接收精简快照，公共创建接口及 CLI 的目录复制行为不变。快照在成功或失败后释放。未选 App 且未指定契约时沿用默认目录：该目录仅含入口 DLL，其 Core/Builtins 由 Editor 共享，不按外部来源要求完整闭包。
- 自包含 Editor 的平台程序集按运行时 deps 清单中 `runtimepack.Microsoft.NETCore.App.Runtime.*` 的 runtime 资产识别；框架依赖部署使用独立框架目录中的平台程序集。不能以自包含部署的共享目录排除业务 DLL。
- 创建并打开项目后、导航到工作区前，以既有 `ExternalGameDebugClient.SaveSettings` 保存 EXE。工作目录采用既有默认值（EXE 目录），无额外启动参数。没有选 App 时不生成调试配置。
- 选 App 不代表判断其是否启用了调试协议；普通 Debug Sample 默认支持，玩家发行包需使用 Developer 构建。契约更新仍沿用严格指纹校验。

限制：仅部署静态托管引用闭包，动态加载、原生执行不属于作者契约。文件读取/校验失败先于项目创建；项目创建成功后的调试配置 I/O 失败沿用创建面板错误展示，不回滚已经创建的项目。
