# 可卸载契约 DLL 的 Windows 文件锁

状态：confirmed
来源 Feature：F-20261013-01-editor-cli-operations

## 现象与根因

ProjectSchemaContextTests 首轮测试中，Dispose/Unload 后删除临时项目仍报 UnauthorizedAccessException。AssemblyLoadContext.LoadFromAssemblyPath 会保持 DLL 文件映射；Unload 是可回收请求，不能保证 GC 此时已释放文件。异常或 catalog/delegate 的栈引用也可能延长寿命。

## 正确做法与证据

项目契约主 DLL 和自有依赖均使用 File.OpenRead + LoadFromStream，并及时关闭流；依赖位置仍通过 AssemblyDependencyResolver 和项目 Contracts 目录解析。重新测试全部通过；LoadedFiles_CanBeReplacedWithoutWaitingForGarbageCollection 验证旧上下文仍可查询、文件可覆盖、新上下文读新契约。

## 适用范围与禁止做法

Windows 项目契约部署、关闭和回收操作。不把 Unload 当作同步释放所有文件锁，不通过强制 GC 或结束用户进程掩盖正常生命周期。共享框架契约继续复用默认上下文类型身份，流式加载不是将 Core 重新加载一份。
