# Contributing to DotCraft

Thank you for considering contributing to DotCraft!

## Development Environment Setup

### C# / .NET Core
- .NET 10 SDK (preview)
- Recommended editors: VS Code or Visual Studio 2022

### Rust / TUI (Optional)
- Rust 1.75+ (`rustup`)
- See `tui/README.md` for dependencies

### TypeScript / Desktop (Optional)
- Node.js 20+ LTS
- See `desktop/README.md` for dependencies

## Quick Start

```bash
# Clone and build
git clone https://github.com/xxx/dotcraft.git
cd dotcraft
dotnet build dotcraft.sln

# Run tests
dotnet test tests/DotCraft.Core.Tests
```

## Ways to Contribute

### 1. Code Contributions

#### Using AI-Powered Tools
If you're using AI coding assistants, load the skill from `samples/skills/dev-guide/`:
- The AI will automatically follow code style and module conventions
- Example prompts: "Help me create a new Discord channel module"

#### Manual Development
Reference the development guidelines:
- **Code style**: `samples/skills/dev-guide/SKILL.md`
- **Module spec**: `samples/skills/dev-guide/references/module-development-spec.md`
- **Reference implementations**: `sdk/typescript/packages/channel-qq/` (external channel), [dotcraft-unity](https://github.com/DotHarness/dotcraft-unity) (ACP runtime tools)

### 2. Documentation Contributions

- Chinese docs: `docs/*.md`
- English docs (root): `docs/*.md`
- Chinese docs: `docs/zh/*.md`
- Sample projects: `samples/` (each sample needs `README.md` + `README_ZH.md`)

### 3. Test Contributions

- Location: `tests/DotCraft.Core.Tests/`
- Framework: xUnit + coverlet
- New features should include unit tests

### 4. Issues and Feature Requests

- Use GitHub Issues for bug reports or feature requests
- Bug reports should include: reproduction steps, expected behavior, actual behavior, environment info

## Pull Request Process

1. **Fork and create a branch**
   ```bash
   git checkout -b feature/your-feature-name
   ```

2. **Write code and test**
   - Follow code style guidelines
   - Add necessary tests
   - Update related documentation

3. **Submit PR**
   - Fill out the PR template
   - Link related issues
   - Wait for code review

4. **Code Review**
   - Requires at least one maintainer approval
   - CI must pass before merging

## Code Style Quick Reference

| Stack | Style Guide | Key Requirements |
|-------|-------------|------------------|
| C# | Official conventions + modern features | file-scoped namespace, sealed class, XML doc |
| Rust | Idiomatic Rust | `snake_case`, `anyhow::Result`, `#[tokio::main]` |
| TypeScript/React | Standard React | Functional components, Zustand, Tailwind CSS 4 |

See `samples/skills/dev-guide/SKILL.md` for complete guidelines.

## Pre-Submission Checklist

### C# Code
- [ ] Follows C# official style conventions
- [ ] Code placed in correct module (Core, App, or channel module)
- [ ] XML documentation comments added for public APIs
- [ ] Documentation provided in both English and Chinese
- [ ] Configuration validated with appropriate error messages
- [ ] Tested manually in real environment

### Rust Code
- [ ] `cargo clippy` passes without warnings
- [ ] `cargo fmt` applied
- [ ] New modules have documentation comments

### TypeScript/React Code
- [ ] ESLint passes without errors
- [ ] Components use TypeScript types
- [ ] Styles use Tailwind CSS

### Documentation
- [ ] Both language versions updated in sync
- [ ] Links are valid
- [ ] Code examples are runnable

## Need Help?

- **Development guidelines**: `samples/skills/dev-guide/`
- **Code examples**: `sdk/typescript/packages/channel-qq/`, [dotcraft-unity](https://github.com/DotHarness/dotcraft-unity)
- **User docs**: `docs/`
- **Questions**: Open a GitHub Issue

## License

By contributing to DotCraft, you agree that your contributions will be licensed under the Apache License 2.0.
