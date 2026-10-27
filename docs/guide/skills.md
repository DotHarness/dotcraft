# Skills

A skill teaches the agent how to do something once, so the next time a similar job comes up it loads that write-up and follows it. Each skill is a Markdown file with frontmatter. Some ship with DotCraft, some you write yourself, and the agent can write or fix one when you ask.

![DotCraft skill sources overview](/skills-sources-overview.svg)

## Where skills come from

| Source | Description |
|---|---|
| **System** | Built-in skills that ship with DotCraft and are shared by every workspace |
| **Personal** | General-purpose skills you write once and reuse across workspaces |
| **Workspace** | Skills that belong to the current project and travel with the repository |
| **Plugin-bundled** | Skills distributed by a plugin, installed and removed with it |
| **Market-installed** | Third-party skills pulled in from SkillHub or ClawHub |

When you write a skill yourself, `~/.craft/skills/` makes it personal and the workspace's `.craft/skills/` makes it project-specific. Skills also come from `.agents/skills/`, the folder other agent tools share: the workspace's one is read as a project skill and `~/.agents/skills/` as a personal one, so what you keep there works in DotCraft without being copied — turn that off under **Settings → Personalization → Skills**. Every source is switched on or off from the Skills page, and a skill you find in a marketplace has to be installed before it joins the local list.

### Let DotCraft explain and configure itself

No need to read the docs or hand-edit config files. Ask in the conversation: "what can you do", "switch to Claude", "which MCP servers do you have". DotCraft answers, and when a setting has to change it makes the change for you and tells you when it takes effect. To integrate DotCraft into your own application, ask it to walk you through that too; it writes code against the SDK version you actually have installed.

## Ask the agent to write a skill

Say "turn what we just did into a skill" or "the deploy skill is missing a step, fix it", and the agent writes the skill files itself, following the built-in authoring guide and checking the result before it finishes. New skills go to the workspace's `.craft/skills/`. To adapt a system, personal, or plugin skill for one project, the agent copies it into the workspace first. The edits are ordinary file changes under your approval settings, and the conversation shows which skill was created or updated, with a link that opens it.

A new or renamed skill joins the skills list in a new conversation, or after you send `/reload-skills`.

### What's worth saving as a skill

- A reusable procedure that emerged from a complex task
- A fix for a problem that will likely come back
- A correction you made that settled into a stable way of working
- An existing skill you found to be stale, incomplete, or wrong

A one-off answer doesn't need a skill.

## Search and install

The Desktop Skills page searches your installed skills and the SkillHub and ClawHub marketplaces at the same time. The source filter (All / System / Personal / Marketplace) changes what you browse, not what is enabled.

![Skills page](https://github.com/DotHarness/resources/raw/master/dotcraft/docs/skills.png)

To install a skill from a marketplace:

1. Open it in the search results and read its README, description, and source links.
2. Select **Install with DotCraft**.
3. DotCraft starts an install agent that inspects your workspace, system, and available tools, and adjusts the installed copy when something in your environment differs, telling you what it changed.

![Skill marketplace results](https://github.com/DotHarness/resources/raw/master/dotcraft/docs/skill-hub.png)

<p class="caption">Installing a market skill through DotCraft</p>

Market skills land under the workspace's `.craft/skills/`. If a skill of the same name is already there, Desktop asks before overwriting it, so installing again is also how you return to the published version. System and plugin-bundled skills are trusted by default. SkillHub and ClawHub are external sources, so try anything you're unsure about on a branch or in a separate workspace first.

## Enable and disable

**Manage**, at the top right of the Skills page, is where you switch skills on and off in bulk. Search your installed skills there and use the toggle on each row. A disabled skill stays on disk but never enters the agent's context.

## Official workflow plugins

The official development skills are split into two plugins by scope. `dotcraft` covers DotCraft-specific development, documentation, release, simplification, troubleshooting, context handoff, and issue reporting. `harness-workflow` covers shared feature planning and isolated UI prototyping that follow the current project's conventions. Enable either from the Plugins page as the work requires. Substantial DotCraft features usually call for both. For how the two divide product rules from shared workflows, see [Spec-Driven Development](../contribute/spec-driven-development).

## Which one to use

| Scenario | Recommendation |
|---|---|
| A fixed procedure in one project ("run lint and tests before any PR") | Workspace skill, hand-written or agent-created |
| A preference that follows you everywhere (your code style) | Personal skill |
| Tools plus several skills you want to distribute | Build a [plugin](./plugins-tools) |
| Capturing a problem the agent just solved | Ask the agent to turn it into a skill |
| Reusing something the community already built | Search the marketplace from the Skills page, then install with DotCraft |

## Related docs

- [Plugins & Tools](./plugins-tools) — package skills and tools into a plugin you can distribute
- [Memory & Dreams](./memory) — memory keeps the facts, skills keep the procedures
