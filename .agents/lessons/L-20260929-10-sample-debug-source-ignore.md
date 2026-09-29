# Sample Debug 源码不应被构建目录忽略规则吞掉

状态：candidate
来源：2026-09-29 Sample Avalonia 调试菜单扩展

## 现象

向 `src/Samples/GalNet.Sample.Avalonia/Debug/` 新增 C# 文件后，项目能编译，但 `git status` 不显示这些文件，因此常规提交会遗漏实现。

## 根因

仓库根目录的 Visual Studio 通用忽略规则 `[Dd]ebug/` 会匹配任意层级名为 `Debug` 的目录；Sample 恰好把调试源码放在该目录中。

## 正确做法

在该通用规则之后，为源码目录及其 `.cs` 文件添加精确反忽略规则：

```gitignore
!src/Samples/GalNet.Sample.Avalonia/Debug/
!src/Samples/GalNet.Sample.Avalonia/Debug/*.cs
```

之后用 `git status` 和 `git diff --check` 确认新增源码可见；不要依赖一次性的 `git add -f`。

## 适用范围

所有位于名称为 `Debug` 目录下、需要版本化的源码目录。

## 不要做什么

不要删除全局 `[Dd]ebug/` 规则，它仍用于排除正常构建输出。

## 证据和关联文档

- `.gitignore`
- `src/Samples/GalNet.Sample.Avalonia/Debug/`
