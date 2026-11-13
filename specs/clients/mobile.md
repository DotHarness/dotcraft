# DotCraft Mobile

| Field | Value |
|---|---|
| Version | 0.10.4 |
| Status | Draft |
| Date | 2026-11-07 |
| Parent spec | [Hub Architecture](../architecture/hub-architecture.md) |
| Related Specs | [Device Gateway](../protocols/device-gateway.md), [AppServer Protocol](../protocols/appserver-protocol.md), [Desktop Client](desktop-client.md), [TypeScript SDK](../sdk/typescript.md) |

## 1. Overview

DotCraft Mobile is a phone app for keeping up with the agent while away from the desk. The agent
keeps running on the user's computer. The phone shows what is running, answers approvals and
questions, sends follow-up messages, stops work, and starts new chats in the computer's projects.

The phone is an ordinary AppServer client and a paired device of the computer's
[Device Gateway](../protocols/device-gateway.md), which owns pairing, device records, the relay, and
how a device reaches a computer. This spec owns the phone's behavior.

## 2. Goals

- Pair a phone with one scan, and let the user see and revoke every paired phone on the computer.
- Pair one phone with several computers and switch between them, while work on each stays
  connected and can reach the user.
- Read a chat as it streams, answer its approvals and questions, add a message, or stop it.
- Start a new chat in any project on the computer, including one whose runtime is not running.
- Compose like on Desktop: attach photos and files, switch to plan mode, and insert
  skills.
- See what a chat changed, open a file it refers to, and check remaining context and account usage.
- Keep a dropped connection invisible apart from a status line: reconnect, catch up, and replay
  anything still waiting for an answer.

## 3. Non-goals

- Running the agent on the phone, or in a cloud service.
- Editing settings, providers, plugins, skills, automations, or Agent Profiles from the phone.
- Editing files, browsing the computer's folders, typing into terminals, the in-app browser, or
  Desktop extension surfaces.
- Controlling a computer that belongs to someone else. A pairing grants the phone the authority of
  the computer's signed-in user, and the product says so.
- iOS. The app ships for Android; the wire contract stays platform-neutral.
- A push service. While work is running or waiting, the phone keeps a live session in the background
  and posts its own notifications (§6.5).
- A hosted public relay. Users who want access from other networks run their own relay
  ([Device Gateway §10](../protocols/device-gateway.md#10-off-network-access)).

## 4. Architecture

| Part | Owner | Role |
|---|---|---|
| Mobile app | `mobile/` in this repository; application id `com.dotharness.dotcraft` | Pairs with one or more computers, keeps one gateway connection per running project of each, renders chats. Uses the `@dotcraft/sdk` contracts, wire client, and gateway client over the pinned transport. |
| Device gateway | Hub ([Device Gateway](../protocols/device-gateway.md)) | Opt-in TLS listener on the computer: pairing, device credentials, project list, AppServer relay. |
| Workspace AppServers | Hub-managed, unchanged | Own threads, turns, approvals, and history exactly as for Desktop. |
| Control this PC segment | Desktop Connections settings ([Desktop Client §6.15](desktop-client.md#615-control-this-pc)) | Turns the gateway on and off, shows the pairing code, lists and revokes devices. |

## 5. Pairing

Phones pair through the ceremony in [Device Gateway §8](../protocols/device-gateway.md#8-pairing-and-devices).
Scanning the computer's QR code is the phone's only pairing path. The phone stores the credential in
the Android Keystore and the computer record in app storage.

## 6. Mobile app

### 6.1 Computers and connections

The app pairs with any number of computers. Scanning an unpaired computer adds and selects it once
the computer allows it; scanning one already paired, recognized by `computerId`, replaces its
pairing once allowed and removes the old pairing from the computer. Abandoning a pairing changes nothing. Each computer
keeps its own credential, record, status, projects, and chats, and one computer's failure,
revocation, or removal leaves the others alone. The app remembers the selected computer; every
screen below Home belongs to the computer it was opened from.

In the foreground, and during a background live session (§6.5), the app keeps for every paired
computer its `/m/events` socket and one relayed AppServer connection per running project; otherwise
it closes them in the background. Each relayed connection initializes as an approval-capable client
that supports user-input requests, streaming, and background terminal notifications, and opts out
of `terminal/outputDelta`, reading terminal output from snapshots instead.

- **Status.** A computer is **online** when a gateway route answers, **connecting** while trying
  addresses, and **offline** otherwise. Offline shows its last known projects and chats read-only.
- **Reconnect.** Each computer reconnects independently with exponential backoff and jitter from 1
  to 30 seconds, restarted immediately when the app returns to the foreground or the network
  changes (a repeated report of the same network is not a change). Only launch, foreground return,
  a network change, or a dropped connection shows connecting; background retries keep an offline
  computer offline until one succeeds.
- **Catching up.** After reconnect, resume the visible active chat and chats retained by live-session owners. Keep saved history visible while applying the current state and five-Turn summary page in feed order. Archived chats use read/paged history.
- **Refresh.** Pulling down Home, a project, or a chat refreshes its computer until the refresh
  settles. Online, the phone reloads the project list, connects newly started projects, relists
  chats, rereads the workspace approval default, and refreshes an active chat through the ordered resume boundary, or reads header/pages for an archived chat. Offline, with access off, or waiting for a backoff retry, it reconnects at once and settles
  with that attempt (or with one already running). A computer whose identity changed does nothing.

### 6.2 Screens

| Screen | Content |
|---|---|
| Pair | Camera scan, waiting for the computer to allow the phone (with a note when it replaces an existing pairing), a declined outcome, and a connected confirmation. |
| Home | The selected computer: its status and a menu with **Add computer** and **Settings**; one chip per paired computer to switch selection, preserving the chip row's horizontal scroll position and updating the selected chip in place while the content below switches computers; **Projects**, marking those not running; **Recent** chats of running projects with their state, omitted when empty; search over chat titles; and a compact composer that expands in place into New chat for the most recently used project, keeping any draft when folded back. Switching computers resets search and new-chat state so drafts and project choices do not cross computers. |
| Project | The project's chats, newest first, above the compact composer. Opening it starts a project that is not running, so its chats and model controls load; Home never starts a project by itself. |
| New chat | The expanded composer, which is the chat's composer, so model, reasoning, approval policy, and Plan mode are set before the first message; a project switcher lists the computer's projects, most recently used first. Expanding it starts a stopped project. Send keeps the message while the chat is created, then opens the chat in place; a failed send returns the message with the error. |
| Chat | Top bar with Back, title, project and computer, the context ring (opens Status), and the chat menu. The transcript follows Desktop's turn layout: a finished turn folds work before its final reply into one **Worked for** row (keeping steering messages, the last plan card, and generated images visible); consecutive tool calls group as on Desktop, into one row naming each kind of work done without counts, with reasoning between them never splitting a group and the trailing group of a running turn naming the call in progress; `CodeMode`, `CodeModeWait`, and `SendUserMessageAsync` calls are hidden as on Desktop, while the calls a script makes still show; a running turn ends with Desktop's working line: the thinking glyph loops while the turn works, the label shows the turn's latest reasoning title as on Desktop (its latest heading line, else the first line of its latest written paragraph), including a finished reasoning's title, for as long as the turn works, and otherwise shows Thinking when reasoning yields none; when the turn stops, or while a decision waits, the glyph fades out at once, and the line keeps its space until the next message appears; Copy follows a completed turn's final reply; a completed turn that changed files ends with an **Edited** card carrying Desktop's changes icon. While a decision waits, the decision card ends the transcript and the composer is hidden. The composer carries Add, the approval policy, and model controls; during a turn it steers and offers Stop. Pills above the composer open Changes (while a running turn has changed files), reading **Changes** with the line counts, and Background tasks (while any of the chat's background tasks runs); centered when they fit, the row scrolls sideways when they do not, so neither pill shrinks. A jump-to-latest control appears when scrolled away. History follows [Thread History Loading](../features/thread-history-loading.md): a five-Turn summary opening page applies to every Turn status. Detail hydration reads at most one 100-Item page per selected Turn with at most four workers, preview hydration at most two, preserving per-Turn cursors. Scrolling and explicit expansion drive further Turn/Item pages while retaining the reading position, loading indicators and retry state. Restore never drains all cursors; saved history retains summary/partial state. |
| Add menu | **Photo**, **File**, and **Plan mode** (checked while on). |
| Picker | System actions and skills matching what follows `/` or `$` in the composer. |
| Changes | Every file the turn changed with additions and deletions, each expandable to its diff. Paths are relative to the chat's `effectiveWorkspacePath`, which is also the base for relative file links in the transcript. |
| File | One read-only file from the computer with **Download** to the phone's `Download/DotCraft/` folder. Images open full screen. |
| Background tasks | **Running** (expanded) and **Finished** (collapsed, counted). Each task shows its title above a metadata row containing its kind, elapsed or total duration in tabular figures, and an agent's model and tokens, marked Stopped or Failed when it did not finish normally. Duration changes do not take space from the title. Running tasks offer Stop; finished tasks expand to their detail: a shell task's last output lines, an agent's role, or a workflow's agent count. |
| Status | Context left, account usage when the provider reports it, the project folder, and the chat ID to copy. |
| Decision card | One card for every decision a chat waits on: an approval, a question, or a plan confirmation. It is the last transcript entry and scrolls with it; the composer stays hidden until the decision is made. |
| Settings | Every paired computer with its status and Remove, **Add computer**, and app information. The app version reads **DotCraft &lt;version&gt;** without an appended build number. |

Chat states use one vocabulary: **running**, **needs approval**, **needs answer**, **done**,
**failed**, from `thread/list` runtime snapshots and `thread/status/changed`. A stopped turn is done
and shows a Stopped line. Thread summaries do not carry the last turn's result, so a list shows
**failed** only for a failure the phone has seen.

Equivalent states and actions use Desktop's current wording and punctuation in every supported
locale, including thinking, tool activity, approvals, background tasks, and shared controls.
A chat paused for permission uses **Awaiting approval** (**等待批准** in Simplified Chinese),
distinct from permission-policy requirements and **Allow** or **Reject** actions.
Phone-only pairing, connection, and notification instructions retain their own wording; references
to Desktop settings and buttons use their current labels.

Chat rows on Home and Project show an unread dot from the chat's server read receipt
([Thread Read State](../features/thread-read-state.md#6-client-behavior)), hydrated from
`thread/list` and updated by `thread/readState/changed`. Opening a chat marks it read, and so does
a new result, or the app returning to the foreground, while its chat screen has navigation focus; a
chat left mounted beneath another screen is not marked. A failed mark is retried. A host that
returns no receipts shows no dots and receives no marks.

Screens paint Desktop's conversation surface: in dark, its vertical gradient from the background
lifted by 0.5% of the ink to the background darkened by 20% black; in light, its white surface. Bars,
the composer dock, and the system bars take the surface colour at their edge, and the jump-to-latest
control uses that surface at 70% with a hairline and no shadow, so it reads as the background.

### 6.3 Working with a chat

- The phone lists and starts chats under the Desktop channel identity, so a chat started on either
  can be continued on the other.
- New chat ensures the project's AppServer, registers local capture, starts a thread with the project's defaults (attaching automatically), then calls `turn/start`. Turn admission itself does not attach; clients expecting output ensure observation first.
- A message sent during a turn uses `turn/steer` when the server accepts steering and `turn/enqueue`
  otherwise; Stop calls `turn/interrupt`.
- The decision card shows one request at a time and counts the others waiting. Options are choice
  rows: tapping selects, tapping a selected row answers, and **Submit** answers with the selection.
  An option's description shows under its label, since a phone has no hover to reveal it.
- An approval uses Desktop's question for shell, file, remote-resource, or computer-use operations;
  computer-use questions name the target application. It shows its type, operation, target, and
  reason, with **Allow once**, **Allow for
  session**, and **Reject**, answering `item/approval/request` with `accept`, `acceptForSession`, or
  `decline`. The phone never offers `acceptAlways`; permanent grants belong on the computer. An
  approval has no dismiss.
- Restored approvals/questions retain `method + threadId + turnId + requestId`. Park them until the selected view is applied, and gate submission until the current request is available. They remain pending across navigation and observation release.
- A question follows Desktop's format: options as rows plus **Other** for typed text, paging through
  multiple questions; **Submit** answers `item/tool/requestUserInput` with every answer.
  **Dismiss** answers a non-blocking request with no answers and interrupts the turn for a blocking
  one, as Desktop does.
- The chat menu offers Copy session ID, Rename (`thread/rename`), Fork (`thread/fork` into the same
  project, opening the copy), and Archive (`thread/archive`, returning to the list). Worktree forks,
  pinning, and actions that open things on the computer stay on Desktop.
- The composer shows the chat's model, reasoning effort, speed, and approval policy (`prompt` or
  `autoApprove`). Any other chat policy, and New chat until the user picks one, shows the project's
  `Permissions.DefaultApprovalPolicy` (`autoApprove`, else `prompt`), read with `config/read` when the
  AppServer advertises `workspaceConfigManagement` and again on `config/changed` for that key.
  Changing a setting sends the whole configuration with `thread/config/update`, effective from the
  next turn. New chat starts on the provider's `isDefault` model from `model/list`, and switching
  provider selects that provider's `isDefault` model; `thread/start` names provider and model and
  carries reasoning, speed, and approval policy only when the user changed them, so AppServer fills
  the rest from the provider's preference. Models and their options come from `model/list`, never
  from app rules, and each control is hidden when the server lacks its capability. The phone never
  changes a chat's Agent Profile.
- **Photo** picks images, scales each to at most 2048 px on its longer side, and re-encodes it as
  JPEG. Before sending, the phone writes each photo to
  `<project>/.craft/attachments/images/<id>.jpg` with `fs/createDirectory` and `fs/writeFile` and
  sends it as a `localImage` part, so the agent can reach it by path later. **File** picks any
  document up to 2 MiB, writes it under `<project>/.craft/attachments/<id>/`, and sends a `fileRef`.
  Attachments wait in the composer until sent. A failed send or upload returns the draft to the
  composer with a notice (naming the file for a failed upload) and **Retry**. A file over the limit
  is refused when picked. **Photo** and **File** are hidden without `capabilities.fileSystem`.
- **Plan mode** toggles with `thread/mode/set` (`plan` or `agent`); New chat in plan mode starts the
  thread with `mode: plan`. A successful `CreatePlan` shows as Desktop's plan card, expandable to
  the full body and to-dos, with Copy. A pending plan confirmation appears in the decision card with
  an implement option and a row to describe adjustments. This local choice is not an RPC request: it requires current Plan mode, the latest completed Turn's successful CreatePlan result, the matching current plan/Turn identity and unconsumed confirmation state. Approvals/questions take precedence and a running Turn cannot revive it. Implementing confirms a successful scoped change to `agent` against the latest observed mode before sending "Implement the plan." A target switch or superseding mode decision prevents sending. An adjustment is explicit feedback that stays in plan mode. **Dismiss** returns the composer in plan mode. If the switch to `agent` fails, nothing
  is sent and the failure is reported. A chat running an Agent Profile has no Plan mode, because its
  agent keeps a fixed capability scope.
- Typing `/` at the start of a word opens the Picker with system actions, then enabled skills from
  `skills/list`; `$` opens skills only. Matching follows
  Desktop: query characters match in order, each skip lands on a word start, a mid-word start needs
  three characters, and within each group prefixes rank above word starts above mid-word matches. A
  skill is inserted inline as `$name`, deleted as a whole, and sent as a `skillRef`.
- The system actions are **Plan mode**, shown when the chat offers it and toggling it as the Add menu
  does, and **Compact**, shown when `capabilities.manualCompaction` is set, the chat has a turn, and
  nothing is running or compacting. Choosing one removes the typed `/query`. Compact calls
  `thread/compact/start` with a long wait while the send button shows progress; a skipped or failed
  result is reported above the composer.
- The changes pill shows the file count and added and deleted lines from `turn/diff/updated`, or
  from the turn's `fileChange` results until a diff arrives, and goes when the turn ends. A
  completed turn that changed files ends with an **Edited** card with the same counts, rebuilt from
  history's `fileChange` results on reopen; stopped or failed turns have none. Both open Changes for
  that turn. The phone offers no Undo.
- A file chip or a file in Changes opens File with `fs/readFile`. Text shows syntax-highlighted with
  wrapping, images as images, and Markdown as a rendered preview (leading YAML frontmatter as a
  key/value card) switchable to source. Other types, or files over the read limit, show the name and
  path to copy. Without `capabilities.fileSystem`, a chip only shows its path.
- The context ring starts from `contextUsage` in `thread/read` and follows `contextUsage` in
  `item/usage/delta` and terminal compaction events, as on Desktop; without a snapshot it is empty.
  Status shows context left with tokens used and window size, and, for the active provider, usage
  from `provider/auth/usage`: each limit window with share left and reset time, each prepaid balance
  amount when the provider reports balances (as DeepSeek does), and an insufficient-balance warning.
- Download writes through Android's shared Downloads collection and needs no storage permission; on
  Android 9 and earlier saving there is not supported.
- Replies render GitHub-flavored Markdown as on Desktop. Images show inline: web images load
  directly; images on the computer, including those referenced by `localImage` from any client, are
  read with `fs/readFile`. Local file links show as file chips that open File; web links open the
  browser. File and skill references in user messages show as chips, and sent photos as thumbnails.
  Line and file comments a message carries ([Desktop 5.5](desktop-client.md#55-input-rules)) show
  as a file chip with their location followed by the comment, never as the raw comment section.
  Math is not typeset: formulas show their TeX source as code.
- The phone renders neither Generative UI blocks nor visualizations. A top-level `dotcraft-ui` block
  shows as a tool activity line whose details hold its source; a visualization reference line is
  omitted; Copy keeps only prose.
- A tool activity line fits one line with the tool kind's icon; tapping shows the full command or
  target and output. A running tool reads in the present tense using Desktop's wording, with
  elapsed time in localized whole-second units, refreshed once per second, with zero-padded trailing
  unit values and tabular figures. The timer does not shrink; the tool text truncates to fit. Its natural
  width may grow when the leading digit count or displayed units change; no fixed large time slot
  is reserved. In-progress text (running tools, streaming reasoning, generating images) uses
  Desktop's shimmer treatment, still under reduced motion.
- After a question is submitted, its decision card closes and a collapsed question tool row shows
  the question count. Desktop and Mobile use the same speech-bubble question-mark icon. The row
  opens to each original question paired with its answer, in request order, without truncating the
  expanded answer. Unanswered questions are marked as such; secret answers stay hidden, as do
  answers whose request metadata has not loaded. The row is separate from adjacent tool groups and
  is restored from `userInputRequest` and `userInputResponse` paired by Turn and request id.
- Desktop's runtime thread tools read as Desktop names them, in progress, done, or failed, with no
  details; a line for reading or messaging a chat opens that chat when it is on this computer's list.
- A file tool call marked as a Skill operation ([Skills](../architecture/skills.md#51-presentation))
  reads as Desktop words it, naming the Skill. Tapping the line shows its target and output, and
  the Skill name opens the `skillFile` it carries in File.
- A user message the computer sent shows Desktop's origin line above it: sent from a workflow,
  automation, app, goal auto-continue, or DotCraft from another thread (`subagentFollowupTask`,
  `subagentInput`). The first message of a chat started from another chat
  (`source.spawnedFromThreadId`) says so too, and opens that chat when tapped. A message with
  `sentAsGoal` is marked as sent as a goal.
- The timeline shows Desktop's dividers for `SystemNotice` items: forked from a conversation,
  connected to or disconnected from a remote host, execution switched back to the computer,
  continuing or stopped continuing in a channel, and context compacted (automatic, manual, or
  recovered from overflow; `micro` shows nothing). Successful Desktop `CreateThread` and
  `SendMessageToThread` calls show as **Chat created** or **Message sent** cards that open the
  chat.
- Live states follow Desktop: `system/event` `compacting` shows a compacting divider after the last
  turn (manual without a `turnId`, automatic with one) until a terminal compaction event or turn
  change; a repeated `streamError` shows the reconnect attempt until the turn ends; a turn the phone
  stopped reads **You stopped** until the chat reloads.
- Failures render as on Desktop: each `error` item shows in place as an error line with its message
  and **Copy**, and a failed turn whose `error` matches no error item adds one at its end. A
  `usageLimitExceeded` failure shows Desktop's usage-limit divider instead; `serverOverloaded` and
  `rateLimitExceeded` show Desktop's overload wording while Copy keeps the original message. When
  the chat's last turn failed with `serverOverloaded` and nothing is running, its last error line
  offers **Retry**, which starts a turn with no input; consecutive capacity retries count down 10,
  30, 120, then 300 seconds before retrying automatically, after which Retry waits for a tap. Of a
  run of turns that hold only identical errors, only the latest shows.
- A background task notification (`triggerKind` `terminal` or `subagent`, or `workflow` with a
  `triggerStatus`) is never a user message. When it starts a turn, the turn has no user message and
  its **Worked for** row starts with the task kind's icon; its accessible name says how the task
  ended, in Desktop's wording. A subagent notification drained into a running turn adds its own line
  at its place naming the subagent and whether it finished or was interrupted, in Desktop's wording.
  Do not render a successful ending line while the child has an active Turn or its live task tree
  has active terminals, running or paused Workflows, or working descendants. Keep the existing running
  activity surfaces, without introducing a replacement notice or label. The original finished line
  becomes visible when the work settles; wake accessible descriptions meanwhile use the existing
  generic subagent origin. Both update without new transcript items. Resolve children by their
  canonical agent path, never by display name, and isolate projects.
  Failed, cancelled, and `TaskStop` outcomes do not change. Other notifications drained into a running
  turn add nothing. Mobile keeps the subagent icon where Desktop shows the subagent's avatar, since
  avatars render only in a DOM view there.
- Background task tools read as Desktop names them: an `Exec` still running after its result stays a
  running line while its turn runs and afterwards reads as a started (or stopped) background
  terminal; `SpawnAgent`, `SendMessage`, `ListAgents`, and `TaskStop` read as Desktop labels them,
  and `TaskStop` opens to the subagent's last message when its result carries one.
- **Background tasks** are the chat's work that outlives its turns, from three sources, each used
  only when `initialize` reports its capability:
  - Background shell commands (`capabilities.backgroundTerminals`): `terminal/list` for the thread,
    keeping terminals started with `runInBackground` and terminals still running after their turn. A
    running terminal of the active turn is left out until that turn ends, as on Desktop, and the
    list is reread then. A listed terminal stays listed once it ends. They follow
    `terminal/started`, `terminal/completed`, and `terminal/cleaned`; Stop calls `terminal/stop`.
  - SubAgents (`capabilities.subAgentSessions`): `subagent/children/list` without closed children,
    reread on `subagent/graph/changed` for the chat and `thread/status/changed` for a child. A child
    has an active turn per its thread runtime; Stop on that turn calls `turn/interrupt`, leaving it open. An
    agent's card adds its model and tokens used: the sum of its turns' `tokenUsage` from
    `thread/turns/list`, raised to `total` from each `thread/tokenUsage/updated`, never decreasing.
    A non-running child reads how its latest turn ended (completed, failed, or stopped when
    cancelled). A child that disappears while running counts as stopped.
  - Workflow runs (`capabilities.extensions.dynamicWorkflows`): `workflow/run/list` for the thread,
    reread on `workflow/run/updated`; paused runs count as running; Stop calls `workflow/run/stop`
    when the run's controls allow it and the capability lists `stop`.

  The phone loads a chat's tasks and recursively discovers its open SubAgent descendants when it
  opens or attaches to the chat. The existing Running Tasks entry and task sheet aggregate the chat's
  and descendants' work in one list without source labels or grouping. Descendant tasks remain visible
  when their parent Agent has no active turn. Opening a child chat exposes its own tasks through the
  same entry. Each task retains its actual owner thread for controls and state updates; stopping a
  command stops only that session, and stopping a Workflow uses its owner thread. An idle Agent is
  not counted again as a running task just because its descendants or background commands are active.
  Reconnecting reloads the observed task tree. Lifecycle and graph notifications update descendants
  even if they were never opened as chats; repeated refreshes of one source for one thread coalesce
  into one request at a time. Background tasks never mark the chat's model runtime as running.

### 6.3.1 Runtime update budget

Text deltas are losslessly coalesced per thread in fixed 50 ms windows; subsequent deltas do not
postpone the window. Each batch updates history once. Item and turn lifecycle changes, diffs,
approvals, questions and terminal states flush earlier text before taking effect immediately.
Restoration stages live updates and requests until the authoritative snapshot is applied, preserving response/feed order across the client boundary. A resolved RPC promise is not UI readiness. Applying restored state does not re-notify completion or replay execution effects; queues from a disconnected or replaced connection never enter newer state.

The renderer subscribes to the state slices it displays. Unrelated threads and account-usage
updates do not rebuild a chat's transcript. Unchanged item and turn projections retain their
identity, with invalidation for associated results, turn state, history replacement and workspace
path changes. Changes summaries and details share parsed diffs; text-only updates do not parse
unchanged diffs, and a closed Changes sheet does not derive its details.

Offline snapshots retain complete Turns, including an oversized newest Turn. Only changes to
persisted data schedule a save: after 400 ms idle, or at most two seconds after the first unsaved
change during continuous updates. Terminal states and moving to the background request an immediate
save. Writes run asynchronously, one at a time, coalescing waiting work to the newest snapshot;
clearing or removing a pairing cannot be undone by an older write. Serialization remains on the
JS thread, but unchanged detail projections are reused and unrelated notifications do not trigger it.
Snapshots are written to a temporary file before replacing the previous file.

The thinking glyph retains its shape, three-second cycle, colour, filter and reduced-motion state.
On Android its per-frame SVG properties run on the UI animation runtime, without React state
updates on the JS thread. An unfocused chat or backgrounded app stops its invisible glyph animation.

### 6.4 Mascot

The app renders the DotCraft mascot with the shared `@dotcraft/avatar` package, so it matches
Desktop. It marks moments, not every screen:

| Moment | Mascot |
|---|---|
| Home | Reflects the selected computer: idle when online, looking around while connecting, asleep when offline or phone access is off, and holding up its question sign while any chat needs the user. |
| Pair | Greets on the scan screen, waits while reaching the computer, and celebrates when pairing completes. |
| Transitions | Opening a chat with no saved history while history loads, a project starting, and reconnecting show the mascot working with one line saying what is happening, in place of skeleton rows. |
| Chat | A new chat's empty transcript greets with the mascot; a chat running an Agent Profile shows that profile's avatar instead. |
| Empty and error states | Empty lists, the revoked notice, and the identity-changed notice pair one sentence with a matching expression. |

The mascot uses the original brand appearance, not the Desktop pet's outfit. Motion follows the
reduced-motion setting, and the mascot is decorative to screen readers.

### 6.5 Live session

When the app moves to the background while any chat on any computer is running, waiting on the
user, or has a background task running, it keeps its connections in an Android foreground service of
type `connectedDevice` with one ongoing notification. The notification follows one chat across all
computers — the most recent one waiting on the user, else the most recently updated running one —
showing its title and its pending request, latest tool step, or that it is thinking or replying. Its
header names the followed chat's computer, or counts running and waiting chats when there are
several; tapping opens the followed chat. On Android 16 and later it requests promotion to a Live
Update with the followed chat's state as its status chip. A chat followed only for its background
tasks shows their count and does not count as a running chat in the header.

- A live session retains independent observations for every actual running/waiting chat and chat with a running background task, not just the notification's followed chat. Start/fork attachment is reused; reconnect uses metadata-only resume without binding overrides for background owners. Pending requests and subsequent Turn results arrive on these attached feeds ([AppServer Protocol](../protocols/appserver-protocol.md) §7.6), including a Turn started by a finished Workflow. Owners release observations when their live-session need ends, with final detach only after the last actual owner releases it. The phone does not resume every historical chat; tasks started while it was away in a chat not opened since do not keep a live session.
- A new approval posts a heads-up notification naming the chat (and its computer when more than one
  is paired) with **Allow once** and **Reject** actions that answer without opening the app; its body
  opens the chat.
- A new question posts a notification that opens the chat.
- A turn that ends while the app is in the background posts one done or failed notification.
- The session ends, closing its connections and its ongoing, approval, and question notifications,
  when nothing has been running, waiting, or running in the background for two minutes, when the user
  chooses **Disconnect** on the ongoing notification, or when every computer with work has been
  unreachable for two minutes. Done and failed notifications stay.
- Android cannot request permission from the background, so the app requests notification
  permission the first time a chat is running or waiting while it is open. Without it, the app
  closes connections in the background as outside a live session.

### 6.6 Localization

The app ships the same locales as Desktop and follows the phone's language.

## 7. Attention notifications

Turns that start waiting on an approval or a question raise the `approvalRequested` and
`inputRequested` notifications defined in
[Hub Architecture §10](../architecture/hub-architecture.md#10-tray-and-notifications), so the user
learns that work is blocked whether the answer then comes from the computer or the phone.

## 8. Multi-client rules

A phone and a Desktop window may watch the same thread. Pending approvals and questions belong to
the thread: both receive them, the first answer resolves each one and both dismiss it, and a
connection that goes away never resolves one. A request waits without a timeout until answered or
its turn ends, so a phone that reconnects or opens the chat later can still answer it. The AppServer
owns these guarantees; see [AppServer Protocol](../protocols/appserver-protocol.md) §7.6.

## 9. Failure behavior

| Situation | Behavior |
|---|---|
| Certificate mismatch | The phone refuses that computer and shows that its identity changed, with Remove as the only action. Other computers are unaffected. |
| Device revoked | The phone forgets that computer with a notice and selects another paired computer; with none left it returns to Pair with the notice. |
| Project AppServer stops | The relay closes; open chats of that project become read-only with a notice and a Start action, and the phone reconnects when `/m/events` reports it started. |
| Computer asleep or unreachable | The computer shows offline. |
| Gateway turned off | Connections close with `gatewayOff`; the phone keeps the last synced chats read-only and shows that phone access is off, remembering that reason until a connection succeeds again so a restarted app does not report it as merely offline. |
| Project cannot start | Opening a stopped project while the computer is offline or access is off, or when its start fails, reports that the computer can't start that project right now. |
