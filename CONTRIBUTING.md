# Contributing to DotCraft

DotCraft welcomes focused fixes, features, tests, and documentation improvements.

## Proposals

Start a [GitHub Discussion](https://github.com/DotHarness/dotcraft/discussions) before opening a pull request for a new feature, protocol change, or material architecture change. Explain the problem, who it affects, and the proposed behavior.

Use [GitHub Issues](https://github.com/DotHarness/dotcraft/issues) for reproducible bugs and other concrete defects. Small, self-contained fixes may be submitted directly as pull requests.

## Development

DotCraft requires the .NET 10 SDK. Some Desktop and TypeScript changes also require Node.js 20 or later; see [desktop/README.md](desktop/README.md).

```bash
dotnet build dotcraft.sln
dotnet test
```

Follow [AGENTS.md](AGENTS.md) for repository conventions. If you use an AI coding agent, install the official `dotcraft-dev` plugin and load its `dev-guide` skill.

## Pull requests

Keep each pull request focused on one independently reviewable change. Add tests when observable behavior changes, and update affected specifications and English and Chinese documentation together.

Describe the problem and the resulting behavior in the pull request. Include the validation performed and link the relevant Discussion or Issue when one exists.

By contributing, you agree that your contribution is licensed under the [Apache License 2.0](LICENSE).
