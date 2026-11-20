# Memory and skills

Workspace memory, Dreams, and how skills load. For the concepts, read [Memory & Dreams](../guide/memory) and [Skills](../guide/skills).

## Memory

| Field | Description | Default |
|-------|-------------|---------|
| `Memory.Enabled` | Lets new chats use and maintain memory. `Memory.AutoExtract` and Dreams also require it | `true` |
| `Memory.AutoExtract` | Every few replies, a new memory-enabled chat is checked for anything worth remembering that was not saved | `false` |
| `Memory.Personal` | Lets new chats use the personal memory in `~/.craft/memory`, which every workspace on this computer shares | `true` |
| `Memory.Types` | Kinds of memory, each a `Name` and a one-line `Description` of what belongs to it, and `Personal: true` for kinds saved to the personal memory. One to eight | `user` (personal), `feedback`, `project`, `reference` |
| `Memory.SaveGuidance` | What the agent saves on its own while replying, and how it writes a memory | Built-in guidance |
| `Memory.ExtractionGuidance` | What the automatic check saves from recent replies | Built-in guidance |
| `Memory.ConsolidationGuidance` | What Dreams keeps when it tidies memory, and how it merges and resolves contradictions | Built-in guidance |
| `Memory.ExtractionInterval` | Every how many replies the automatic check runs, from 1 to 50 | `7` |

The three switches apply to chats created after the change. The other settings apply from the next reply in every chat. Removing one of them restores the built-in default. The Memory Studio plugin lets you edit them under **Settings › Personalization** and compare changes with defaults before saving.

## Dreams

| Field | Description | Default |
|-------|-------------|---------|
| `Dreams.Enabled` | Runs Dreams on a schedule to tidy memory. Requires `Memory.Enabled` | `false` |
| `Dreams.Interval` | Minimum time since a memory was last tidied before a scheduled run | `24:00:00` |
| `Dreams.MinSessionsSinceLastRun` | Minimum chats that used a memory and completed a turn since it was last tidied before a scheduled run | `3` |
| `Dreams.AutoApply` | Applies each later successful run without review. Runs already waiting for review are unaffected | `false` |
| `Dreams.StartupDelay` | Delay before the first scheduled check after AppServer starts | `00:05:00` |
| `ConsolidationModel` | Top-level key. Model for Dreams runs. Empty uses the main model | Empty |

Dreams tidies the workspace memory and the separate memory of each Agent Profile that declares `memory: own`, one at a time. Manual runs skip `Dreams.Interval` and `Dreams.MinSessionsSinceLastRun`.

## Skills

| Field | Description | Default |
|-------|-------------|---------|
| `Skills.DisabledSkills` | Skill names disabled for this workspace. A disabled skill stays on disk but is left out of agent context | `[]` |
| `Skills.IncludeSharedSkills` | Also loads skills from the `.agents/skills` directories shared with other agent tools: the workspace's, those in parent folders up to the repository root, and `~/.agents/skills`. Takes effect on restart | `true` |
| `Skills.IncludeBuiltInSkills` | Deploys the skills DotCraft ships, such as `dotcraft-guide`, into the workspace on start. A host that brings its own skills turns this off. Takes effect on restart | `true` |
