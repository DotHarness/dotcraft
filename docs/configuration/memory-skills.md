# Memory and skills

Workspace memory, Dreams, and how skills load. For the concepts, read [Memory & Dreams](../guide/memory) and [Skills](../guide/skills).

## Memory

| Field | Description | Default |
|-------|-------------|---------|
| `Memory.Enabled` | Lets new sessions use and maintain memory. `Memory.AutoExtract` and Dreams also require it | `true` |
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
| `Skills.IncludeSharedSkills` | Also loads skills from the `.agents/skills` directories shared with other agent tools: the workspace's, those in parent folders up to the repository root, and `~/.agents/skills`. Takes effect on restart | `true` |
| `Skills.IncludeBuiltInSkills` | Deploys the skills DotCraft ships, such as `dotcraft-guide`, into the workspace on start. A host that brings its own skills turns this off. Takes effect on restart | `true` |
