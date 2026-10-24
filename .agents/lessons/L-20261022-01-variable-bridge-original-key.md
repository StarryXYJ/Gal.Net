# 变量回写保留桥接服务原键

状态：confirmed。来源：F-20261021-03 的实际 Sample 跨进程变量验收。

Runtime 的 VariableStore 将 player./save. 前缀归一化，但 PlayerVariableService 可以保留 `player.galnet.*` 系统键。加载进 Runtime 后再按归一化 name 回写，会在服务中生成第二个别名，旧系统键仍是旧值。仅查询 Runtime 显示新值不能证明重开/存储正确。

修正：已有变量按 UID 找到桥接服务的原键再通知，不改变已建立的存储身份；新变量沿用既有命名规则。不要直接给服务写一个猜测前缀，也不要只更新服务而漏掉当前 Runtime。

验证：FileVariableServiceTests 的恢复前缀变量修改后，服务只有一项、UID 不变、重新加载值正确；原生 Sample 修改 Player 值、停止、重开、正常退出并读取 profile。实际落盘还依赖宿主刷新，不能以 Runtime 新值代替持久化证据。
