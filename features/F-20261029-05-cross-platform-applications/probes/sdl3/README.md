# SDL3 / Avalonia 共存探针

这是 P0 的隔离探针，尚未替换正式 GameHost 的 XInput。固定 SDL **3.4.18**，窄绑定使用源生成 `LibraryImport`；只覆盖 Windows/Linux x64，不声称已验证 AOT、macOS 或真实手柄。

`native-sources.json` 的 SHA256 对照官方 GitHub release asset digest；下载使用 SDL 官方镜像。准备脚本校验归档，保存原生文件 SHA256、来源、构建选项及 SDL zlib 许可。Linux 在独立临时目录构建输入专用库，自动清理源码/构建目录，不安装系统包。当前 WSL 环境的 HIDAPI/虚拟设备启用，libudev/libusb 未启用，不能据此推断完整物理设备兼容性。

从仓库根运行，输出目录须为空：

```powershell
$probe = 'features/F-20261029-05-cross-platform-applications/probes/sdl3'
python "$probe/prepare-native.py" --target win-x64 --output out/sdl-probe/native-win
dotnet restore "$probe/Sdl3Probe.csproj" --locked-mode
dotnet publish "$probe/Sdl3Probe.csproj" -c Release -r win-x64 --self-contained true --no-restore -o out/sdl-probe/app-win
python "$probe/verify-probe.py" --app out/sdl-probe/app-win --native out/sdl-probe/native-win --output out/sdl-probe/windows-results
```

Linux 准备需要 Python 3.11+、CMake、Ninja 和 C/C++ 编译器。在 Linux 主机准备原生库；发布可在 Windows 宿主交叉构建：

```sh
probe=features/F-20261029-05-cross-platform-applications/probes/sdl3
python3 "$probe/prepare-native.py" --target linux-x64 --output out/sdl-probe/native-linux
dotnet restore "$probe/Sdl3Probe.csproj" --locked-mode
dotnet publish "$probe/Sdl3Probe.csproj" -c Release -r linux-x64 --self-contained true --no-restore -o out/sdl-probe/app-linux
python3 "$probe/verify-probe.py" --app out/sdl-probe/app-linux --native out/sdl-probe/native-linux --output out/sdl-probe/linux-results
```

WSL 从 `/mnt/g/program/GalDotNet` 调用 Linux 验证脚本，脚本自动复制到含空格的 `/tmp` 路径，使用自包含运行时与私有 SDL；默认设置进程级 d3d12（可由已有环境变量覆盖）。Windows 隔离副本留在所选输出的父目录。两者都在结束时删除副本；SDL 准备结果、许可和日志保留供审计。

每次运行在 Avalonia UI 主线程完成两个初始化周期，通过 SDL 自己的虚拟设备接口发送按键/轴，验证标准 gamepad 识别、更新、释放、断连和重新初始化。只初始化 gamepad（隐式 joystick/events），断言 SDL video/audio 始终未启用，因此未创建 SDL 游戏窗口；焦点测试短暂创建第二个普通 Avalonia 窗口。SDL 采集后台状态，由探针的 Avalonia 焦点 gate 抑制输出。正式适配仍须实现动作取消、重新武装、布局提示和多 scope 所有权，不能直接把探针 gate 当正式动作层。

验证器连续运行三次，并确认坏 native SHA256 在 UI 启动前以 exit 1 拒绝。每次正常运行记录检查、线程、子系统 flags、native 来源和 exit 0。独立探针注册开源 Inter 字体，避免依赖宿主是否装有默认字体；不新增专有字体。

本机最终证据：`out/cross-platform-validation/20261029-sdl3/windows-font-fixed/`、`linux-fixed/`，各 3 次 × 52 项通过及坏哈希拒绝。保留 `windows-before-refinement.*` 的启动期间同步退出重入和 `linux-final/` 的缺少探针字体注册失败，不计为通过。

官方依据：[SDL release](https://github.com/libsdl-org/SDL/releases/tag/release-3.4.18)、[InitSubSystem](https://wiki.libsdl.org/SDL3/SDL_InitSubSystem)、[PumpEvents](https://wiki.libsdl.org/SDL3/SDL_PumpEvents)、[虚拟手柄描述符 ABI](https://github.com/libsdl-org/SDL/blob/release-3.4.18/include/SDL3/SDL_joystick.h)。
