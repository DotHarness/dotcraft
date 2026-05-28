# DotCraft Automations Samples

These samples show DotCraft native Automations as a local-task system.

## Samples

- [example-local-task](./example-local-task): a local task folder with `task.md` and `workflow.md`.
- [config.template.json](./config.template.json): minimal workspace config enabling local Automations.

## Layout

```text
samples/automations/
  config.template.json
  example-local-task/
    task.md
    workflow.md
```

Copy the local task folder into your workspace `.craft/tasks/` directory and keep Automations enabled:

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

DotCraft native Automations covers local tasks only.
