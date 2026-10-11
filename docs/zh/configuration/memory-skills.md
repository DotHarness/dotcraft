# 记忆与技能

工作区记忆、梦境，以及技能的加载与自学习。概念见[记忆与梦境](../guide/memory)和[技能与自学习](../guide/skills)。

## 记忆

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Memory.Enabled` | 让新会话使用并维护工作区记忆。梦境和欢迎页建议也依赖此开关 | `true` |
| `ConsolidationModel` | 梦境运行使用的模型，空值使用主模型 | 空 |

## 技能

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Skills.DisabledSkills` | 当前工作区禁用的 skill 名称列表。被禁用的 skill 仍留在磁盘上，但不会注入 Agent 上下文 | `[]` |
| `Skills.IncludeSharedSkills` | 同时从 `~/.agents/skills` 读取个人 skill，该目录与其他 agent 工具共用。重启后生效 | `true` |
| `Skills.IncludeBuiltInSkills` | 启动时把 DotCraft 自带的 skill（如 `dotcraft-guide`）部署进工作区。自带 skill 的宿主可以关掉它。重启后生效 | `true` |
| `Skills.SelfLearning.Enabled` | Skill 自学习主开关。关闭后模型看不到 skill 编辑能力 | `true` |
| `Skills.SelfLearning.VariantMode` | Skill variant 写入模式。`enabled` 把自学习更新写入工作区本地 skill variant，`disabled` 关闭 variant | `enabled` |
| `Skills.SelfLearning.MaxSkillContentChars` | 通过自学习写入单个 `SKILL.md` 的最大字符数 | `100000` |
| `Skills.SelfLearning.MaxSupportingFileBytes` | 通过自学习写入单个 supporting file 的最大字节数 | `1048576` |

Skill 自学习示例：

```json
{
  "Skills": {
    "SelfLearning": {
      "Enabled": true,
      "MaxSkillContentChars": 100000,
      "MaxSupportingFileBytes": 1048576
    }
  }
}
```

`SkillManage(action, ...)` 参考：

| Action | 必填参数 | 用途 |
|---|---|---|
| `create` | `name`, `content` | 创建新的工作区 skill |
| `patch` | `name`, `oldString`, `newString` | 局部修补 `SKILL.md` 或 supporting file |
| `edit` | `name`, `content` | 完整替换已有工作区 skill 的 `SKILL.md` |
| `write_file` | `name`, `filePath`, `fileContent` | 写入 supporting file |
| `remove_file` | `name`, `filePath` | 删除 supporting file |

`create` 会触发 `kind: skill` 审批，破坏性删除也需要审批。自学习只写当前工作区 skill 目录。系统和个人 skills 视为只读，supporting files 只能写在 `scripts/` 或 `assets/` 下，绝对路径和 `..` 路径穿越会被拒绝。
