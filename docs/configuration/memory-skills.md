# Memory and skills

Workspace memory, Dreams, and how skills load and self-learn. For the concepts, read [Memory & Dreams](../guide/memory) and [Skills & Self-Learning](../guide/skills).

## Memory

| Field | Description | Default |
|-------|-------------|---------|
| `Memory.Enabled` | Lets new sessions use and maintain memory. `Memory.AutoExtract`, Dreams, and welcome suggestions also require it | `true` |
| `Memory.AutoExtract` | Every few replies, new memory-enabled sessions check the conversation for anything worth remembering that was not saved | `false` |

Both switches apply to sessions created after the change.

## Dreams

| Field | Description | Default |
|-------|-------------|---------|
| `Dreams.Enabled` | Runs Dreams on a schedule to tidy memory. Requires `Memory.Enabled` | `false` |
| `Dreams.Interval` | Minimum time since a memory was last tidied before a scheduled run | `24:00:00` |
| `Dreams.MinSessionsSinceLastRun` | Minimum sessions that used a memory and completed a turn since it was last tidied before a scheduled run | `3` |
| `Dreams.AutoApply` | Applies each later successful run without review. Runs already waiting for review are unaffected | `false` |
| `Dreams.StartupDelay` | Delay before the first scheduled check after AppServer starts | `00:05:00` |
| `ConsolidationModel` | Top-level key. Model for Dreams runs. Empty uses the main model | Empty |

Dreams tidies the workspace memory and the separate memory of each Agent Profile that declares `memory: own`, one at a time. Manual runs skip `Dreams.Interval` and `Dreams.MinSessionsSinceLastRun`.

## Skills

| Field | Description | Default |
|-------|-------------|---------|
| `Skills.DisabledSkills` | Skill names disabled for this workspace. A disabled skill stays on disk but is left out of agent context | `[]` |
| `Skills.IncludeSharedSkills` | Also loads personal skills from `~/.agents/skills`, the directory shared with other agent tools. Takes effect on restart | `true` |
| `Skills.IncludeBuiltInSkills` | Deploys the skills DotCraft ships, such as `dotcraft-guide`, into the workspace on start. A host that brings its own skills turns this off. Takes effect on restart | `true` |
| `Skills.SelfLearning.Enabled` | Master switch for agent skill self-learning; off hides skill editing from the model | `true` |
| `Skills.SelfLearning.VariantMode` | Skill variant write mode: `enabled` routes self-learning updates to workspace-local skill variants, `disabled` turns variants off | `enabled` |
| `Skills.SelfLearning.MaxSkillContentChars` | Max chars for a single `SKILL.md` written through self-learning | `100000` |
| `Skills.SelfLearning.MaxSupportingFileBytes` | Max bytes for a single supporting file written through self-learning | `1048576` |

Self-learning example:

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

`SkillManage(action, ...)` reference:

| Action | Required parameters | Purpose |
|---|---|---|
| `create` | `name`, `content` | Create a new workspace skill |
| `patch` | `name`, `oldString`, `newString` | Local patch of `SKILL.md` or supporting file |
| `edit` | `name`, `content` | Replace an existing workspace skill's `SKILL.md` |
| `write_file` | `name`, `filePath`, `fileContent` | Write a supporting file |
| `remove_file` | `name`, `filePath` | Delete a supporting file |

`create` triggers a `kind: skill` approval, and destructive deletes require approval too. Self-learning writes only to the current workspace's skill directory. System and personal skills are read-only, supporting files may only live under `scripts/` or `assets/`, and absolute paths or `..` traversal are rejected.
