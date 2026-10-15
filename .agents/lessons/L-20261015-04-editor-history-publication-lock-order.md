# Editor 历史与发布必须采用一致的获取顺序

状态：confirmed
来源：F-20261015-02-editor-authoring-workspace / P1b

现象与根因：结构编辑在项目历史的执行门内运行，并需要获取文件发布门。如果 Save All 先占有发布门再等待历史门，就形成「保存等待编辑、编辑等待发布」的锁反转。不能用同步等待或把 I/O 放在 UI 线程中来掩盖该问题。

正确做法：捕获作者版本前先取得历史捕获租约，再尝试取得发布门；发布门忙时释放捕获租约，异步等候后重试。文件发布期间释放普通保存的历史租约，允许后续编辑；结构编辑已经拥有历史门时仅允许它重入结构捕获，不允许在一次编辑中捕获半成品 Save All。关闭先挂起并等待历史与保存队列，再提示/释放 scope。

验证：ProjectWorkspaceLifecycleTests.SaveWaitsForTheRunningEditWithoutDeadlockingItsStructuralPublication 让一个异步编辑暂停，在它仍持有历史门时发起 Save All，再让该编辑发布 Group 路径变换；两者必须在超时前完成，之后还能撤销结构。ProjectContentSessionTests.EditingDuringPublicationDoesNotMarkTheNewerContentSaved 验证普通保存释放捕获租约后仍保留新版本 dirty。

适用范围：Editor 的保存、撤销重做、Group 文件计划及后续 P4 文件事务。禁止在持有发布门时等待历史门，禁止持有捕获租约跨普通保存的后台 I/O。
