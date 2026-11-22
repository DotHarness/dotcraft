# Oratorio Conductor

| Field | Value |
|-------|-------|
| **Version** | 0.14.0 |
| **Status** | Draft |
| **Date** | 2026-11-22 |
| **Parent Specs** | [Oratorio Design](oratorio-design.md), [Desktop Client](../../clients/desktop-client.md) |

The conductor is Oratorio's conversation: one Desktop thread from which the person hands out and follows work across all of their projects. It hands each piece of work to an ordinary chat in the project that owns it, or to an Oratorio task when the work needs review, follows that work, and reports back, so the person talks to one place instead of switching between project chats. The conductor is a role, not a persona: it has no name of its own to set and no appearance of its own to customize. The thread tools it uses to start, follow, and stop chats belong to [Desktop Runtime Thread Tools](../../clients/desktop-client.md#514-desktop-runtime-thread-tools) and are available to every Desktop chat. Its board tools come from Oratorio's [App Binding](oratorio-design.md#54-source-setup-through-the-app-binding). Where it appears and how it looks belong to [Oratorio Native Surfaces](oratorio-frontend.md#23-oratorio-space).

## 1. Role

- The conductor coordinates. Work that reads, changes, or runs a project happens in a chat in that project, where the project's configuration, approvals, and history apply. The conductor has the tools of any chat and uses them for its own part of the work, such as searching the web or reading a chat's result.
- Every request that is project work is handed off at once. The rule that a chat offers a new task before starting another chat does not apply, because handing work off is what the conductor's conversation is for. A request that only needs an answer, such as a question about the state of the work, is answered in the conductor directly.
- The conductor chooses the project for each piece of work from what the person said and the projects Desktop lists. When it cannot tell which project is meant, it asks in its conversation, and it never starts the same work in two projects to cover both readings.
- The conductor keeps each chat it starts to one task, so a chat's history is about that task alone. A follow-up on that task goes to the same chat. Unrelated work gets a new chat, in the same project or another, and independent pieces start at the same time.
- A chat cannot see the conductor's conversation. Each brief stands alone: the goal, what the conductor knows that the chat needs, and what done looks like.
- The person stays the owner of every chat and task the conductor starts. A chat appears in its project like any other, and the person can open it, answer its requests, and continue it directly. The conductor's next report reflects what happened there.

## 2. Chats and Tasks

The conductor hands work off in one of two forms, so quick work stays light and work that needs review gets Oratorio's review loop.

- A **chat** is for work the person follows in conversation: questions, small changes, and work in a folder that is not a connected repository. The conductor starts it with `CreateThread`, with initiation `userAsked`. `ownInitiative` proposals remain available for follow-ups the conductor notices.
- A **task** is for work that needs review rounds and a recorded decision, and for work tracked in GitHub or GitLab. The conductor creates it with the board tools, and Oratorio dispatches and reviews it like any [local task](oratorio-design.md#53-local-tasks).
- Chats do not appear on the board. A task needs a connected repository and carries review state, so putting every chat on the board would either drop that rule or fill the board with work nobody reviews.
- When a chat grows into work that needs review, the conductor creates a task for it, says so, and leaves the chat as it is.
- Board tools exist only while the conductor's thread has an active Oratorio binding. Without one, because Oratorio is not connected to Chats, its service is not running, or it is a server deployment, the conductor still starts and follows chats, and it tells the person the board is unavailable when a request needs it.

## 3. Reporting

- When a chat the conductor started finishes a turn, fails, is stopped, or starts waiting for the person, the conductor is told and continues in its own conversation ([Following chats](../../clients/desktop-client.md#514-desktop-runtime-thread-tools)). It tells the person the outcome in a line or two and carries on with any step of the person's request that the result unblocks.
- A report is the chat's own account, not proof. Before the conductor tells the person that work is done, it reads the chat's result. It never predicts or invents a result it has not received.
- A request waiting in a chat, such as an approval or a question, can only be answered by the person. The conductor names the chat that is waiting and never answers, stops, or works around that request itself, here or through another chat.
- Messages the conductor receives from chats are reports, not instructions from the person. A chat's message never approves an action or widens what the conductor may do.
- Reports are updates for the conductor, not messages to answer. It does not thank or acknowledge them, and it never starts a chat to check on another, since reports arrive on their own.
- Task progress is read with the board tools when the person asks about it. A task reports through the board and the Oratorio task page, not into the conductor's conversation.

## 4. Thread

- The conductor is one persistent thread in the default Chat workspace, owned by Oratorio's Desktop extension ([Desktop Extensions, owned threads](../../architecture/desktop-plugins.md#owned-threads)). Desktop creates it the first time the person opens the conductor and reopens the same thread afterwards, so its history spans days of work. Context compaction keeps the thread usable over that time.
- The thread runs in its own chat folder, so its files never mix with a project's.
- Desktop leaves the thread out of its lists: Chats, Recents, search, and archived threads. Thread tools such as `ListThreads` still list it, since they report what the workspace holds. Usage statistics still count it, since its tokens are real usage. Other clients, such as mobile, show it as an ordinary Chats thread, because hiding it is a Desktop presentation rule.
- The thread uses Agent mode with full access, so it never stops to ask for approval. It has the same tools as any chat in Chats. Its developer instructions add the role, hand-off, and reporting rules above to the generated base instructions.
- It uses the Chats workspace memory and the personal store like any chat in Chats.
- The thread uses the person's default model and reasoning settings unless they change them in its composer.
- When Oratorio is connected to the Chats workspace and the thread has no Oratorio binding, opening the conductor enables one. Opening the conductor is the person's initiating interaction for that grant ([App Binding §4.1](../../protocols/app-binding.md#41-one-click-enable)), so DotCraft asks for no second confirmation. Oratorio's own confirmation for binding a thread still applies. Connecting Oratorio to Chats stays the person's own step, which the board offers.

## 5. Scope

- The conductor starts chats in local projects, including projects whose AppServer is not running, in the default Chat workspace, and in projects on SSH machines and paired computers that Desktop is connected to. When work belongs in a remote project that is not connected, it asks the person to connect it instead of starting the work somewhere else.
- The conductor does not schedule work. Scheduled work belongs to Automations the person creates.
