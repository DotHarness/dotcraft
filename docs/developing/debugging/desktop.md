# Debug DotCraft Desktop

Chrome DevTools Protocol (CDP) lets an agent inspect and exercise the DotCraft Desktop renderer. Once Desktop is running with CDP enabled, the debugging skill manages the attached session and detaches without closing the application.

![A developer starts DotCraft Desktop with CDP enabled, then an agent loads the Desktop debugging skill and drives the running application through one reusable Playwright session](/desktop-debugging-flow.svg)

## Install the debugging workflow

The `$dotcraft-desktop-debugging` skill ships with the official `dotcraft` plugin. Install that plugin into the workspace before starting a debugging task:

1. Open **Plugins** in DotCraft Desktop.
2. Find **DotCraft**, published by DotHarness, and select **Install**.
3. Review the confirmation, then select **Add to DotCraft**.

For the complete plugin installation flow, see [Plugins and tools](../../features/agent-system/plugins-tools).

## Start Desktop with CDP

### Development environment

From `desktop/`, run:

```powershell
npm run dev:debug
```

To open a specific workspace, forward the workspace argument to Electron:

```powershell
npm run dev:debug -- -- --workspace <workspace-path>
```

### Packaged environment

For a packaged Windows build, start the executable with the debugging port:

```powershell
DotCraft.exe --remote-debugging-port=9222
```

Both commands expose the fixed loopback endpoint `http://127.0.0.1:9222`. CDP is enabled when the process starts.

## Confirm CDP is enabled

![The DotCraft Desktop home screen with the CDP status indicator and its enabled tooltip visible in the lower-right corner](https://github.com/DotHarness/resources/raw/master/dotcraft/developing/desktop-cdp-debugging.png)

<p class="caption">The lower-right status indicator confirms that this Desktop process accepts CDP connections.</p>

Hover or focus the blue status indicator in the lower-right corner. It reports **CDP debugging is enabled.**

## Hand the session to the agent

Describe the Desktop behavior you want inspected and name the debugging skill explicitly:

```text
$dotcraft-desktop-debugging Inspect the current Desktop window and capture evidence for the issue.
```

The agent attaches to the existing loopback endpoint, selects the DotCraft window, waits for the workbench to finish restoring, and reuses one debugging session throughout the task. The attach commands and readiness checks live in the skill so they stay current without being duplicated here.
