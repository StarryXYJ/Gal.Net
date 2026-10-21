# P1a：独立调试协议与本机连接

状态：verified（协议子阶段）。

新增可选 GalNet.Debugging 组件，Runtime 不反向引用。Editor 侧 DebugListener 随机 loopback 端口与会话凭据，App 侧 GameDebugHost 显式连接；握手检查协议、项目与内容指纹。长度帧有上限，双向请求有并发上限，断线取消连接所属工作。

App 连接独立于 IDebugGameRun，Attach/Dispose binding 控制当前运行；可选开始回调。状态查询不进入长命令队列，取消独立投递，执行命令在队列中重新检查运行身份和代次。旧 binding 再次释放不能解绑新运行。

已有真实 TCP 测试覆盖连接后的绑定/解绑/替换、旧运行拒绝、可选开始回调和错误身份不能占用监听。此子阶段尚不表示 Editor 菜单或 Sample 默认适配已完成，P1b 继续。

验证：Debugging.Tests 4 项通过，Architecture.Tests 47 项通过；新增项目依赖锁已生成。不把同进程 TCP 测试声称为完整跨进程 GUI 验收。
