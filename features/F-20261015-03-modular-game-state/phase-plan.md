# 实施阶段

1. verified：注册式状态表、v4 快照封套、Runtime/运行代隔离。Core 111、Runtime 85 项通过，包含自定义状态 round-trip、版本/重复载荷和快照别名隔离。
2. verified：视觉状态、类型化循环动画与模块恢复迁移，删除旧镜像与中央分支。Builtins 114、Presentation 191 项通过，包含视频替换 identity、动画首帧事件、依赖排序、恢复门禁与部分失败清理。
3. verified：音频轨道/SEffect 统一状态、暂停/队列、恢复与后端隔离。Runtime 和 Integration 覆盖 headless/fake host、自然结束、旧代 Reset、同步 Stop 终态事件、空闲效果与未来 voice 继承。
4. verified：Sample 实际文件、正式文档、自审与总结。相关七个测试项目共 665 项通过；Storage.FileSystem 21、Architecture 38 项在其中。解决方案构建 0 错误；CLI 全项目完整校验与 intro 实际编译成功（41→41 entries）；真实作者文件经文件存档服务创建新档再恢复通过。git diff --check 通过。

原生媒体 opt-in 测试与 GUI 人工全流程未执行；这是验证限制，不宣称实际 LibVLC 播放已验证。独立 Editor feature 的工作树改动保留。详见 summary.md / review.md。
