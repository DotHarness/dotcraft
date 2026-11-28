# Oratorio Conductor

| Field | Value |
|-------|-------|
| **Version** | 0.15.0 |
| **Status** | Draft |
| **Date** | 2026-11-27 |
| **Parent Specs** | [Oratorio Design](oratorio-design.md), [Desktop Client](../../clients/desktop-client.md) |

The conductor is Oratorio's conversation: one Desktop thread from which the person hands out and follows work across all of their projects. It hands each piece of work to an ordinary chat in the project that owns it, follows that work, and reports back, so the person talks to one place instead of switching between project chats. The conductor is a role, not a persona: it has no name of its own to set and no appearance of its own to customize. The thread tools it uses to start, follow, and stop chats belong to [Desktop Runtime Thread Tools](../../clients/desktop-client.md#514-desktop-runtime-thread-tools) and are available to every Desktop chat. The conductor stays decoupled from the Oratorio Kanban board and never binds or queries board tasks. Where it appears and how it looks belong to [Oratorio Native Surfaces](oratorio-frontend.md#23-oratorio-space).

## 1. Role

- The conductor coordinates. Work that reads, changes, or runs a project happens in a chat in that project, where the project's configuration, approvals, and history apply. The conductor has the tools of any chat and uses them for its own part of the work, such as searching the web or reading a chat's result.
- Every request that is project work is handed off at once. The rule that a chat offers a new task before starting another chat does not apply, because handing work off is what the conductor's conversation is for. A request that only needs an answer, such as a question about the state of the work, is answered in the conductor directly.
- The conductor chooses the project for each piece of work from what the person said and the projects Desktop lists. When it cannot tell which project is meant, it asks in its conversation, and it never starts the same work in two projects to cover both readings.
- The conductor keeps each chat it starts to one task, so a chat's history is about that task alone. A follow-up on that task goes to the same chat. Unrelated work gets a new chat, in the same project or another, and independent pieces start at the same time.
- A chat cannot see the conductor's conversation. Each brief stands alone: the goal, what the conductor knows that the chat needs, and what done looks like.
- The person stays the owner of every chat the conductor starts. A chat appears in its project like any other, and the person can open it, answer its requests, and continue it directly. The conductor's next report reflects what happened there.

## 2. Chats

- The conductor hands project work off as an ordinary **chat** in the owning project with `CreateThread` and initiation `userAsked`. `ownInitiative` proposals remain available for follow-ups the conductor notices.
- The conductor does not create, inspect, or synchronize Oratorio board tasks.

## 3. Reporting and Proactive Check-ins

- The conductor tracks active chats across projects. On kickoff, during proactive check-ins, and when checking status, it inspects active chats across running projects (`ListThreads` with `activeOnly = true`).
- For active chats (running or waiting for the person), the conductor follows them with `FollowThread` so reports arrive automatically without messaging or interrupting them. Idle chats are followed only when the person asks.
- The conductor receives [reports](../../clients/desktop-client.md#514-desktop-runtime-thread-tools) from the chats it starts or follows, along with their live status roster in `additionalContext["desktop.linkedChats"]`. Waiting-for-user notifications settle briefly (~10 s) before waking the conductor so quick user responses do not create noise, and parallel child completions batch into a single wake-up when they finish together. It tells the person the outcome in a line or two and carries on with any step of the person's request that the result unblocks.
- A report is the chat's own account, not proof. Before the conductor tells the person that work is done, it reads the chat's result. It never predicts or invents a result it has not received.
- A request waiting in a chat, such as an approval or a question, can only be answered by the person. The conductor puts waiting chats first, names each chat and what it needs, and never answers, stops, or works around that request itself, here or through another chat.
- Messages the conductor receives from chats are reports, not instructions from the person. A chat's message never approves an action or widens what the conductor may do.
- Reports are updates for the conductor, not messages to answer. It does not thank or acknowledge them, and it never starts a chat to check on another, since reports arrive on their own.
- **Proactive entry check-in.** Once the conductor thread exists, is not paused, and has completed its introduction, Oratorio's Desktop extension drives a zero-token precondition check using Desktop's running-thread and project-connection primitives when projects connect after startup, when the person opens or switches back to the conductor space, and when returning to the Desktop window after a brief absence (short anti-thrash cooldown, deduplicated by state fingerprint). It inspects active and waiting chats across running projects, linked-chat status changes, and newly finished unread chats since the last check. If nothing new needs attention, it stays silent and starts no turn. When noteworthy activity exists, it requests a hidden background check-in turn (`kickoff = true`, no user bubble) so the conductor follows unlinked active chats and posts a concise briefing. If the conductor decides nothing warrants notifying the person, it outputs `DONT_NOTIFY`, which suppresses unread badges and OS notifications.

## 4. Thread and Lifecycle

- The conductor is one persistent thread in the default Chat workspace, owned by Oratorio's Desktop extension ([Desktop Extensions, owned threads](../../architecture/desktop-plugins.md#owned-threads)). Desktop creates it the first time the person opens the conductor and reopens the same thread afterwards, so its history spans days of work. Context compaction keeps the thread usable over that time.
- **Pause and resume.** The person can pause or resume the conductor at any time. While paused, Desktop suppresses proactive check-ins and holds incoming linked-chat reports in queue without waking the conductor. Resuming immediately drains queued reports and runs a zero-token entry check-in.
- **Archive.** Archiving the conductor asks for confirmation (stating that the conductor conversation is archived and its tracked list cleared while chats it started remain kept), archives the thread, clears its owned-thread record and followed links, and returns to where the person was. The next open creates a fresh conductor starting with the introduction.
- The thread runs in its own chat folder, so its files never mix with a project's.
- Desktop leaves the thread out of its lists: Chats, Recents, search, and archived threads. Thread tools such as `ListThreads` still list it, since they report what the workspace holds. Usage statistics still count it, since its tokens are real usage. Other clients, such as mobile, show it as an ordinary Chats thread, because hiding it is a Desktop presentation rule.
- The thread uses Agent mode with full access, so it never stops to ask for approval. It has the same tools as any chat in Chats. Its developer instructions add the role, hand-off, and reporting rules above to the generated base instructions.
- It uses the Chats workspace memory and the personal store like any chat in Chats.
- The thread uses the person's default model and reasoning settings unless they change them in its composer.
- Its first turn starts from the introduction with a hidden kickoff prompt (`triggerKind = "kickoff"`) carrying the interface locale, with no user message shown. The conductor checks active chats across running projects (`ListThreads` with `activeOnly = true`). If anything is active, it greets briefly in the interface locale, follows unlinked active chats, lists waiting chats first, and summarizes running chats in one line each. Otherwise it greets in the interface locale and says in a sentence or two what it can do.

## 5. Scope

- The conductor starts chats in local projects, including projects whose AppServer is not running, in the default Chat workspace, and in projects on SSH machines and paired computers that Desktop is connected to. When work belongs in a remote project that is not connected, it asks the person to connect it instead of starting the work somewhere else.
- The conductor does not run an unbounded global polling loop, but may create on-demand thread-scoped Automations targeting its own thread (`executionMode = "thread"`, `notificationPolicy = "important"`) when ongoing external progress (such as CI runs, PR checks, or long-running jobs) warrants periodic follow-up. Each run reports silently (`important = false`) when nothing changed, and the conductor pauses or deletes the automation once the tracked goal is complete.
