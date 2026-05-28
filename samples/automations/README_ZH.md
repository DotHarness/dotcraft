# DotCraft Automations 示例

这些示例展示 DotCraft 原生 Automations 的本地任务能力。

## 示例

- [example-local-task](./example-local-task)：包含 `task.md` 和 `workflow.md` 的本地任务目录。
- [config.template.json](./config.template.json)：启用本地 Automations 的最小工作区配置。

## 目录结构

```text
samples/automations/
  config.template.json
  example-local-task/
    task.md
    workflow.md
```

将本地任务目录复制到工作区 `.craft/tasks/`，并保持 Automations 启用：

```json
{
  "Automations": {
    "Enabled": true,
    "LocalTasksRoot": "",
    "PollingInterval": "00:00:30",
    "MaxConcurrentTasks": 3
  }
}
```

DotCraft 原生 Automations 仅覆盖本地任务。
