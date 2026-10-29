# WSLg 存在 GPU 设备不代表默认使用硬件渲染

状态：confirmed
来源 Feature：F-20261029-05-cross-platform-applications

## 现象与验证

Ubuntu 24.04.3 / WSLg 本机存在 /dev/dxg、Mesa d3d12 驱动与 /usr/lib/wsl/lib 的 D3D12 库，但默认 glxinfo 为 llvmpipe、Accelerated: no。进程级 GALLIUM_DRIVER=d3d12 对照显示 Intel UHD Graphics、Accelerated: yes。实际 Sample 进程 maps 加载 libd3d12.so / libd3d12core.so，会话初始化与确认退出 exit 0 通过。

## 做法与边界

- 收到 shader/blur 卡顿时先记录实际渲染驱动，不把 /dev/dxg 存在或 GL 版本足够当作硬件加速证据。
- WSL 开发入口可显式、仅对本次进程选择已验证的 d3d12，尊重调用者已有 GALLIUM_DRIVER；不改系统驱动或全局 shell 配置。
- glxinfo 和已加载库仅证明驱动路径，不能证明应用内每帧都走 GPU，也不能替代实际 blur 帧耗时或原生 Linux 桌面验收。
- 这是当前宿主的事实，不能断言全部 WSLg 都默认使用 llvmpipe。

入口：scripts/samples/run-wsl-sample.sh（--check 仅检查，不启动游戏）。
证据：out/cross-platform-p3b-current/smoke/46625ce92974451c933742fb39b9764e/。
用户最新反馈声音已经正常，并在使用新入口后确认 blur 人工复测“ok了”；不再保留无声或 blur 仍未复测的过时状态，尚无帧耗时测量。
