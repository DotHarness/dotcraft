# Contributing to DotCraft

DotCraft welcomes focused fixes, features, tests, and documentation improvements.

## Proposals

Start a [GitHub Discussion](https://github.com/DotHarness/dotcraft/discussions) before opening a pull request for a new feature, protocol change, or material architecture change. Explain the problem, who it affects, and the proposed behavior.

Use [GitHub Issues](https://github.com/DotHarness/dotcraft/issues) for reproducible bugs and other concrete defects. Small, self-contained fixes may be submitted directly as pull requests.

## Development

DotCraft requires the .NET 10 SDK. Some Desktop and TypeScript changes also require Node.js 20 or later; see [desktop/README.md](desktop/README.md).

Follow [AGENTS.md](AGENTS.md) for repository conventions and build commands. Use the [development guide](desktop/resources/plugins/dotcraft-bundled/plugins/dotcraft/skills/dotcraft-dev-guide/SKILL.md#validation) to select checks for the affected .NET, Desktop, or SDK code, and the [documentation guide](desktop/resources/plugins/dotcraft-bundled/plugins/dotcraft/skills/dotcraft-docs-guide/SKILL.md) for documentation and README work. Both skills are included in the bundled `dotcraft` plugin.

## Pull requests

Keep each pull request focused on one independently reviewable change. Add meaningful tests when an observable contract changes, and update affected specifications and English and Chinese documentation together.

Use an English title in the form `type: short description`. Common types are `feat`, `fix`, `refactor`, `docs`, `test`, `perf`, `build`, `ci`, `chore`, and `revert`.

Start the body with `## Summary`, usually followed by one to four short bullets describing the final change and its effect. Keep file inventories, implementation chronology, and conversation history out of the description. Link an applicable Discussion or Issue at the end; use `Closes #123` when the change resolves that issue.

```markdown
## Summary

- <Describe the resulting behavior and why it matters.>
- <Add another distinct change only when useful.>

Closes #<issue number>
```

Omit unused bullets and the issue line when they do not apply. Complete the relevant validation and report it in the delivery notes or CI; the PR body does not require validation results, a Testing section, or a checklist.

By contributing, you agree that your contribution is licensed under the [Apache License 2.0](LICENSE).
