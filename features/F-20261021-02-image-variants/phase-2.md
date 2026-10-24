# P2：资源编译与发布

日期：2026-10-21
状态：verified。

## 交付

- 注册 `.variants` / `image-variants` / `ImageVariantsAssetMeta`。作者 JSON 与编译 payload 分离，严格检查重复/未知字段。
- Assets 的 `LocalAssetCompiler` 接收文件和目录快照，调用 P1 纯构建器；LocalFileProvider 在返回归档前编译图集，保留正式资源 ID、metadata 和逻辑路径，统一校正过时的 metadata 路径。
- 编译定义只包含维度、默认变体和 sprite ID 映射；普通 PAK 可直接打包和解码，无需原始文件夹。新增 decoder 与显式注册扩展，接入默认 Avalonia 宿主和每次重建的 EditorAssetManager。
- 磁盘枚举拒绝链接/重解析点，逻辑目录解析拒绝 Assets 越界；缺 sidecar/图片、错误类型、重复变体、坏 default 等拒绝归档，不自动修复。目录扫描和编译发布检查代次，并提供只读索引失效入口 Invalidate。
- 项目 validate/build 检查图集并报告 variants.invalid。导出包内校验修正为使用调用方相同 catalog，防止自定义类型在包验证时丢失。
- 资源事务移动时维护 folder，复制完整目录生成独立 ID 并指向副本，导入同批创建 sidecar。投影后快照复用同一编译器预检；破坏默认图的重命名/删除在写盘前拒绝。修改与原操作一起撤销/重做，不创建额外历史。

没有添加图集 show/select 指令、Layer 选择状态或专属编辑 UI，后续仍为 P3–P5。

## 设计落点

运行时 decoder 不扫描目录。已有 LocalFileProvider 就是开发资源的文件系统适配层，因此作者准备在此进行；导出沿用其编译 IGameFile，通用 PakBuilder/Archive 格式不变。事务预检直接使用逻辑文件到保留备份文件的映射，无需复制全部图片到临时目录。

发行 payload version=1，kind=image-variants，字段为 dimensions、default、variants；每个变体保存 key 和 assetId。选项在解码验证时推导，避免存储两份独立真源。目录扫描索引缓存按代次失效，已经 acquire 的定义仍遵循 AssetManager 一般句柄缓存语义；Editor 通过项目/资源刷新重建 manager。

## 验证

最终相关回归共 **633 项通过，0 失败，0 跳过**：

| 测试项目 | 通过 |
| --- | ---: |
| Core | 148 |
| Assets | 54 |
| Storage.FileSystem | 21 |
| Architecture | 45 |
| Editor.Shared | 365 |

本阶段新增 **30 项**：Assets 20、Editor.Shared 10。覆盖实际 LocalFileProvider → PakBuilder → PakFileProvider → AssetManager decoder，移开作者目录后解析与图片获取；路径分隔符/父路径/越界/链接；缺 metadata/图片、类型错误、重复/大小写变体和坏 default；索引失效与取消/销毁；移动描述或目录的身份保留、大小写改名、复制、导入、撤销重做；显式 metadata 类型在改扩展名后仍被验证；坏定义保护旧输出；自定义 catalog 完整导出和包内验证。

完整解决方案在 `artifacts/image-variants-p2/build` 隔离目录构建通过（0 错误；保留既有 Avalonia AVLN3001 警告）。普通输出目录构建受运行中的 Editor/.NET Host 文件锁影响失败，未关闭这些程序，也未将其计为源码编译错误。格式检查只针对本阶段 C# 文件；检查中发现的对象初始化换行已修正。后续最终隔离构建使用同一份源码，未夹入其他任务的业务修改。

主要命令：

```powershell
dotnet build GalNet.slnx --artifacts-path artifacts/image-variants-p2/build --verbosity quiet -m:1 -p:WarningLevel=0 -p:NuGetAudit=false --ignore-failed-sources
dotnet build GalNet.slnx --no-restore --artifacts-path artifacts/image-variants-p2/build --verbosity quiet -m:1 -p:WarningLevel=0
dotnet test test/GalNet.Editor.Shared.Tests/GalNet.Editor.Shared.Tests.csproj --no-restore --verbosity quiet -p:WarningLevel=0 --logger 'trx;LogFileName=editor-shared-p2-final.trx' --results-directory artifacts/image-variants-p2
```

其余四项目使用隔离产物，以 `dotnet test --no-build --no-restore --artifacts-path artifacts/image-variants-p2/build` 验证。TRX 与构建日志保留在忽略的 artifacts/image-variants-p2；未把日志、构建产物或测试临时项目提交到 Git。测试具备真实本地和发布路径，不将手工序列化的定义替代 producer 验证。

## 过程中修正

第一次资源回归发现提取元数据读取后，错误消息丢失了 sidecar 文件名；已恢复按文件包装异常并通过原回归。自定义 catalog 新测试的最初两次失败均为测试夹具配置错误（非法扩展名以及辅助读取未传入自定义 catalog），修正后新测试和完整 Shared 回归均通过。

测试与格式工具继续在允许本地进程通信的执行环境运行，沿用 P1 的沙箱限制结论。普通 Editor 输出锁与既有 `.agents/lessons/L-20261013-01-running-editor-output-lock.md` 一致，后续保持隔离构建，不重复尝试写入已锁输出。

## 文档与后续

已同步[资源规范](../../docs/spec/assets.md)的已验证事实，并更新[设计](design.md)和[阶段计划](phase-plan.md)。无匹配算法偏差。

P3 接续运行指令、Layer 唯一状态、CatchUp 和存读档；图集指令的 wire 名届时核定。本阶段不宣称游戏显示链或专属 GUI 已完成。已有其他 feature 的 phase-plan.md 改动保留，不混入提交。
