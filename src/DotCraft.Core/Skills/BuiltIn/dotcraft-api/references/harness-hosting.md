# Harness hosting

Use `DotCraft.Harness` when a .NET application owns the agent runtime, workspace, configuration, lifecycle, and user experience. Harness is not an AppServer client.

## Ownership boundary

The application constructs the effective `AppConfig` and passes it to Harness. Harness does not implicitly load the user's DotCraft configuration files or environment. The application also chooses workspace and data paths, starts and stops the Generic Host, renders streaming events, and provides approval interactions.

Start at `https://www.dotcraft.net/developing/harness/`. Use the focused pages for:

- package installation: `https://www.dotcraft.net/developing/harness/nuget-package`
- host ownership: `https://www.dotcraft.net/developing/harness/hosting-lifecycle`
- configuration and paths: `https://www.dotcraft.net/developing/harness/configuration-paths`
- threads and turns: `https://www.dotcraft.net/developing/harness/threads-turns`
- tools and approvals: `https://www.dotcraft.net/developing/harness/tools-approvals`
- model providers: `https://www.dotcraft.net/developing/harness/model-providers`

Confirm service-registration signatures and public service types from the installed `DotCraft.Harness` package. Do not require the consumer to locate DotCraft source files.
