# Linux 原生源码合包探针

使用 Linux 原生固定 SDK，以及同一源码/配置/版本发布的 CLI 和 Developer Sample，验证内容导出与源码合包。普通发布可用 `scripts/ci/publish-applications.py --flavors Developer` 准备；私有媒体目录按发行说明准备。

```sh
python3 verify-linux.py --source /path/to/clean/source \
  --cli /path/to/published/CLI/GalNet.Editor.Headless \
  --published-sample /path/to/published/Sample-Developer \
  --dotnet /path/to/sdk/dotnet --native-audio /path/to/libgalnet_miniaudio.so \
  --media-directory /path/to/prepared-media --output /path/to/new/evidence \
  --version 0.0.0-dev.1
```

探针覆盖 Sample 的 galpak/内容目录输入及一个仓库外独立 csproj；后者只消费相同 Core/Builtins/Project.Contracts 与安装内容协议，不引用 Sample 可执行程序。每个实际 build-compose 均重新执行 SDK、locked restore、publish、严格封装；解包后核对全部清单文件的大小/hash、完整文件集合、两个入口的执行位与三份契约的原始字节。独立宿主在 SDK 搜索隔离下启动并核对实际读取的 projectId/buildId；内容创建/导出也在 SDK 搜索隔离下运行。

输出包含每步退出码/日志、每个构建的完整结构化报告、契约 hash、归档 hash 与解包目录。不会自动清理这些证据或覆盖已有输出。此探针的 Sample GUI/媒体不自动运行，也不代表最低系统基线、真实 Linux 桌面或跨宿主契约工件可互换；对应运行证据单独记录。

原生 X11/WSLg 启动退出可另运行：

```sh
python3 verify-linux-window.py --application '/path/to/extracted Sample' --output /path/to/new/gui-evidence
```

该验证只匹配本次进程的 Sample 窗口，兼容重挂载窗口和 UTF-8 标题；从非应用工作目录、无 SDK 搜索且不继承开发 LD_LIBRARY_PATH 的环境启动，核对包内 CoreCLR/VLC/SDL、会话就绪，并发送关闭及退出确认。超时强杀只用于失败清理，不能计为通过；此项不代替完整剧情/媒体或原生 Linux 桌面验收。
