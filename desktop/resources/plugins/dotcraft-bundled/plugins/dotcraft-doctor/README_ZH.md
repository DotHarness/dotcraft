# DotCraft Doctor 插件

一个内置的 DotCraft 插件，帮助用户理解并反馈错误。当某个回合出错时，composer 里的小机器人可以安装此插件并新建会话来诊断问题；如果用户愿意，还能据此起草一份给开发者的 issue 反馈。

包含的 skills：

- `error-diagnosis` —— 只读地根据本地 `.craft` 证据（状态库 + 线程回放）还原失败的回合并定位根因。
- `context-handoff` —— 搜索 DotCraft 会话 / trace，并导出清理后的 Markdown 交接材料，包含 rollback 与 compact 的连续性说明。
- `report-issue` —— 把诊断结果（或一段 bug 描述）整理成发往 `DotHarness/dotcraft` 的清晰 issue，并生成可供用户审核、提交的预填「新建 issue」链接。
