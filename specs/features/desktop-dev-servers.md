# Desktop Dev Servers

| Field | Value |
|---|---|
| Version | 0.14.0 |
| Status | Draft |
| Date | 2026-11-20 |
| Parent specs | [Desktop Client](../clients/desktop-client.md), [In-App Browser](desktop-inapp-browser.md) |

A dev server is a long-running local process, declared by the project in a launch file, that serves a web page on a known port. Desktop starts, watches, and stops dev servers itself, so a server has a stable identity (name, port, address) that the conversation Summary, the browser, and the agent all share. This spec owns the launch file, the server lifecycle, the Summary's Servers section, the browser's server controls, and the agent's server tools. Shell commands the agent runs in the background stay [background tasks](../architecture/background-tasks.md), even when they serve a page: only a declared server is a dev server, because only a declared server has an identity Desktop can trust.

## 1. Availability

Dev servers exist for local projects only. Desktop's trusted host process runs them on this computer, and a remote project's processes run on another machine that Desktop cannot start processes on. A remote project shows no Servers section, no browser server controls, and declares no server tools.

## 2. Launch file

### 2.1 Location

The launch file is `.craft/launch.json` in the thread's execution workspace: the project folder, or the worktree for a worktree thread, or the primary folder of a multi-folder project. Each worktree reads its own copy, so a branch can change its servers. Desktop re-reads the file when it changes on disk. A launch file that resolves outside the workspace, for example through a link, is ignored.

### 2.2 Format

```json
{
  "version": "0.0.1",
  "autoVerify": true,
  "configurations": [
    {
      "name": "web",
      "runtimeExecutable": "npm",
      "runtimeArgs": ["run", "dev"],
      "port": 5173
    },
    {
      "name": "api",
      "program": "server.js",
      "args": ["--verbose"],
      "cwd": "api",
      "port": 8080,
      "autoPort": false,
      "env": { "LOG_LEVEL": "debug" }
    },
    {
      "name": "docs",
      "url": "http://localhost:4000"
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `version` | Informational. Any string is accepted. |
| `autoVerify` | Whether the agent verifies its changes in the browser (§7.3). Defaults to `true` when the file exists. |
| `configurations[].name` | Required, unique within the file, compared case-insensitively. It is the server's identity in every surface. |
| `runtimeExecutable`, `runtimeArgs` | The command and its arguments. |
| `program`, `args` | A script and its arguments. With `runtimeExecutable`, the program and args follow `runtimeArgs`. Without it, the program runs with Node. |
| `cwd` | Working directory relative to the workspace. It must stay inside the workspace. Defaults to the workspace. |
| `port` | The port the server listens on. When absent, it is read from `env.PORT` or a `--port`/`-p` argument. |
| `autoPort` | `true` lets Desktop move the server to a free port. `false` requires exactly `port`. Absent means Desktop moves the port only when it can (§3.3). It must be a JSON boolean. |
| `env` | Extra environment variables. |
| `url` | An attach configuration. Desktop starts nothing and opens this address. A local address must be an origin without path or query. |

A configuration has a command (`runtimeExecutable` or `program`) or a `url`. A configuration with neither, or with a malformed field, is skipped and reported by name, and the rest of the file stays usable. A file that does not parse, or whose every configuration is skipped, needs attention: surfaces that list servers say why and offer to open the file.

### 2.3 Writing

Desktop writes the launch file only to add one configuration (§6.2) or to change `autoVerify`. Both edits keep the file's other content and formatting. Adding a configuration whose name already exists fails without writing. A file that does not parse is never rewritten.

## 3. Lifecycle

### 3.1 Identity and ownership

A running server belongs to a workspace folder and to the thread that started it. Starting a configuration whose name already has a starting or running server in the same folder and thread reuses that server instead of starting a second process, and two concurrent starts of one name settle on one server. A thread keeps its servers when it moves to another folder, such as the worktree its first message creates, so they stay listed and controllable. A thread can stop only its own servers. The user can stop any server from the Summary or the browser.

A folder runs at most five servers at a time, across all its threads, so a forgotten loop cannot exhaust the machine. Starting a sixth fails and names how many belong to other threads. Desktop handles one start at a time, so the limit and the port choice hold when several threads start servers together.

A server started from the Welcome draft's browser belongs to that draft and moves to the thread its first message creates, together with the panel's other live resources.

### 3.2 States

| State | Meaning |
|---|---|
| `starting` | The process runs and its port does not accept connections yet. |
| `running` | The port accepts connections. |
| `error` | The process could not start, exited, or never became ready. |
| `stopped` | The server was stopped. |

Starting retries a failing launch up to three times. A launch whose command cannot be found or whose `cwd` does not exist fails at once, because retrying cannot fix it. A server that stays `starting` past its readiness timeout becomes `error` and is recorded as never ready.

Desktop keeps the recent output of each server, stdout and stderr together, bounded in size, and keeps it after the server ends, so the reason a server failed stays readable. The record of an ended server is dropped when its thread is archived or deleted.

### 3.3 Ports

Every launched server receives its port in `PORT`. When the configured port is taken, Desktop picks the next free port that no other configuration in the file uses, when `autoPort` is `true`, or when it is absent and the port comes only from the configuration itself (`port`, `env.PORT`, or a `--port`/`-p` argument, which Desktop rewrites). A port written into a package script cannot be moved, so a server with such a port keeps it.

When the port cannot be moved and is taken, starting fails with a reason that says what holds the port: another server of this thread (stop it), another thread's server (pick a different `port`, or stop it from that thread), or an unknown process (find and stop it, or pick a different `port`). A failure caused by a port the server hardcodes despite `PORT` says so and suggests removing the hardcoded port.

The surfaces show the port the server actually uses, and the configured port when they differ.

### 3.4 Stopping

Stopping ends the server's whole process tree, including processes the start command leaves running after it exits. Archiving, deleting, worktree removal, and quitting wait for starts in progress, so no server outlives what it belonged to. Desktop stops a thread's servers when the thread is archived or deleted, a folder's servers when its worktree is removed, and every server when Desktop quits. Rewinding or switching the thread's model does not stop servers, because they belong to Desktop, not to the agent's runtime. A dev server never keeps a thread working, so it never shows the thread as busy.

## 4. Summary Servers section

The conversation Summary shows a **Servers** section above **Background tasks** while the thread owns a server in `starting`, `running`, or `error`. Its header reads "n running". It lists at most four rows and folds the rest behind a control that shows them.

Each row shows the server's name, or its `localhost:port` address when the name is empty, then its state: "Starting…", its address while running, or "Failed to start". A starting or running row has a stop control, and while stopping reads "Stopping…".

Selecting a running row opens the thread's browser tab bound to the server (§5.1), or focuses it when it is already open. Selecting a failed row opens the bound tab on its failure state (§5.2).

## 5. Browser

### 5.1 Bound tabs

A browser tab opened for a server is bound to it. Opening a server focuses the thread's tab already showing the server's address before opening another. A tab opened while its server is still starting loads the page once the server is running. A bound tab keeps the browser's ordinary navigation, and loses its binding when it navigates to another origin.

An attach configuration (`url`) opens its address in the same way, without a process. A server that failed because another server of the same thread, or a process outside Desktop, holds its port has no page of its own: opening it says why instead of showing the holder's page.

### 5.2 Server states in a tab

A bound tab never shows a browser error page for a server problem. Instead its page area says what happened and offers the way forward:

| Situation | Message and action |
|---|---|
| Starting | The server is starting. The page loads when it is ready. |
| Stopped | "The dev server stopped." with Restart. |
| Failed to start | "Dev server failed to start", the last line of its output, Restart, and Show logs. |
| Port held by another thread's server | Names the port and says to stop that server from its own thread. |
| Nothing listening | "Nothing listening on localhost:{port}", with Restart when the address belongs to a configuration. |

A bound tab can show the server's output in a logs area below the page. The logs follow live output and can be copied.

### 5.3 Servers menu

The browser toolbar has a **Servers** menu while the project is local. It lists every configuration in the launch file, one row each with its port, or "Starting…" while it starts, followed by running servers of this thread whose configuration has since left the file. Selecting an idle configuration runs it, and selecting a running server or an attach configuration opens its bound tab, so every row has one action and the rows stay aligned. Actions for the current tab's server follow: Stop it, and Show or Hide its dev server logs. Stopping any other server happens from its own tab or the Summary. The menu also offers Stop all servers, Add server (§6), Edit configuration (opening the launch file in the file viewer), and the auto-verify switch (§7.3). Without a launch file the menu says "No dev server configured" and offers Detect dev server. A launch file that needs attention appears as one row that opens it.

### 5.4 New tab

A blank browser tab lists the project's configurations, each with Run, or Open for a running server. Without a launch file, it offers Detect dev server (§6.1). Without configurations it shows its ordinary start hint.

## 6. Adding servers

### 6.1 Detection

Detect dev server reads the workspace's `package.json` and offers one candidate for each of its `dev`, `start`, `serve`, and `develop` scripts. A candidate runs the script through the workspace's package manager (npm, Yarn, or pnpm, from its lockfile), and takes the default port of the framework whose configuration file the workspace has (Next.js, Nuxt, and Remix 3000, Vite and SvelteKit 5173, Astro 4321, Angular 4200, Gatsby 8000), or 3000 when none matches. The user picks a candidate, and Desktop adds it (§6.2) and starts it.

Detection runs only the fixed package managers with a single safe script name, so a crafted `package.json` cannot make Desktop run an arbitrary command. When nothing is found, Desktop says no dev server was found and offers Set up with agent.

### 6.2 Add server

Add server asks for a name, a command, and a port, and appends one configuration to the launch file, creating the file when it does not exist. It does not start the server.

### 6.3 Set up with agent

Set up with agent opens a side chat, as [Local Environments](local-environments.md) does for environment setup. Its first message asks the agent to inspect the project and write the launch file in the §2.2 format, and names the file's absolute path in the thread's execution workspace.

## 7. Agent tools

### 7.1 Declaration

Desktop declares four server tools in its `desktop` namespace of [runtime thread tools](../clients/desktop-client.md), for threads in local projects: `StartServer`, `StopServer`, `ListServers`, and `ReadServerLogs`. They act with the calling thread's ownership (§3.1), never another thread's.

| Tool | Input | Result |
|---|---|---|
| `StartServer` | `name`: a configuration name | `serverId`, `name`, `port`, `url`, and whether an existing server was reused. It opens or focuses the bound tab without taking focus from the user after the first time. |
| `StopServer` | `serverId` | The stopped server. |
| `ListServers` | none | The thread's servers with `serverId`, `name`, `port`, `url`, and state, plus the launch file's configuration names. |
| `ReadServerLogs` | `serverId`, optional `lines`, `search`, and `errorsOnly` | Matching recent output lines, newest last. |

Failures carry the reasons of §3, written so the agent can act on them: a missing or unusable launch file names the fields to fix, and a port conflict names the server holding it.

### 7.2 Guidance

When the tools are declared, Desktop adds runtime context telling the agent to run dev servers with `StartServer` instead of a background shell, to create `.craft/launch.json` when the project has none, and to verify a page through the in-app browser at the address `StartServer` returns.

### 7.3 Auto verify

While the launch file's `autoVerify` is on, the runtime context also asks the agent, after a change the browser can show, to start the server, check the page, and share what it saw before ending its turn. The browser's auto-verify switch writes `autoVerify` to the launch file, so the choice is shared by everyone working on the project.
