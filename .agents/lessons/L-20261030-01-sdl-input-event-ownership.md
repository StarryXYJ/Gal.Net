# SDL 状态轮询必须处理自有输入事件队列

- 状态：confirmed
- 来源：F-20261029-05-cross-platform-applications，P5 SDL3 正式适配
- 范围：SDL 3.4.18、Windows 原生 Avalonia 探针

## 现象与证据

仅调用 SDL_PumpEvents / SDL_UpdateGamepads 读状态，即使关闭 gamepad/joystick 普通事件，虚拟设备热插拔后 SDL_HasEvents(0x600, 0x6ff) 仍返回 true。正式探针的按键、焦点和断连行为已通过，但事件队列检查失败；见 out/cross-platform-validation/20261030-sdl3/host-windows-first.json。

## 修正

状态源明确独占 joystick/gamepad 子系统及其事件范围；同进程 GalNet scope 共享主线程租约。每次更新后和最后一次释放时，仅 FlushEvents(0x600, 0x6ff)。已有外部 SDL input owner 时返回可诊断的不可用，不擅自接管或退出外部子系统。禁用普通状态事件不能替代残留输入队列的处理。

后续原生探针同时检查输入队列为空、无关 SDL user event 仍保留、共享租约和外部 owner 不被释放。禁止清空整个 SDL 队列或调用全局 SDL_Quit 来掩盖问题；其他 SDL 输入消费者需显式协调所有权。
