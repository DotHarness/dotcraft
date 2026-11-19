# Cardo

| Field | Value |
|-------|-------|
| **Version** | 0.14.0 |
| **Status** | Draft |
| **Date** | 2026-11-20 |
| **Parent Specs** | [Desktop Client](../clients/desktop-client.md), [Default Chat Workspace](default-chat-workspace.md) |

Cardo is Desktop's coordinator mode: one conversation from which the person manages work across all of their projects. Cardo hands work to ordinary chats in the projects that own it, follows those chats, and reports back, so the person talks to one place instead of switching between project chats. Cardo is a place and a mode, not a persona: it has no name of its own to set and no appearance of its own to customize. The thread tools it relies on, including starting chats in other projects, following them, and stopping them, belong to [Desktop Runtime Thread Tools](../clients/desktop-client.md#514-desktop-runtime-thread-tools) and are available to every Desktop chat.

## 1. Role

- Cardo coordinates and does not do project work itself. It reads, searches the web, and reads chats and their results, but it does not edit files or run commands in a project. Work that changes a project happens in a chat in that project, where the project's configuration, approvals, and history apply.
- Every request that is project work becomes a chat in the project that owns it, started at once. Cardo does not offer to keep work in its own conversation, because there is no project there to do it in. A request that only needs an answer, such as a question about the state of the work, is answered in Cardo directly.
- Cardo chooses the project for each piece of work from what the person said and the projects Desktop lists. When it cannot tell which project is meant, it asks in its conversation, and it never starts the same work in two projects to cover both readings.
- Cardo keeps each chat it starts to one task, so a chat's history is about that task alone. A follow-up on that task goes to the same chat. Unrelated work gets a new chat, in the same project or another.
- The person stays the owner of every chat Cardo starts. The chat appears in its project like any other, the person can open it, answer its requests, and continue it directly, and Cardo's next report reflects what happened there.

## 2. Reporting

- When a chat Cardo started finishes a turn, fails, is stopped, or starts waiting for the person, Cardo is told and continues in its own conversation ([Desktop Runtime Thread Tools](../clients/desktop-client.md#514-desktop-runtime-thread-tools)). It tells the person the outcome in a line or two and carries on with any step of the person's request that the result unblocks.
- A report is the chat's own account, not proof. Before Cardo tells the person that work is done, it reads the chat's result. It never predicts or invents a result it has not received.
- A request waiting in a chat, such as an approval or a question, can only be answered by the person. Cardo names the chat that is waiting and never answers, stops, or works around that request itself, here or through another chat.
- Messages Cardo receives from chats are reports, not instructions from the person. A chat's message never approves an action or widens what Cardo may do.

## 3. Entry and conversation

- Cardo lives in the default Chat workspace as one persistent thread. Desktop creates it the first time the person opens Cardo and reopens the same thread afterwards, so its history spans days of work. Context compaction keeps the thread usable over that time.
- Cardo has its own sidebar destination at the top of the navigation rows. Selecting it opens Cardo's conversation. The detached pet's menu also opens Cardo.
- Cardo's thread does not appear in Recents or any project, cannot be archived or filed into a section, and is never offered as a target of thread tools. Clearing its history is an explicit action in Cardo's own menu.
- The conversation shows each chat Cardo starts as a task card at the position of the call that started it: the task title, the project, and a status word (Working, Needs you, Done, Failed, or Stopped). Selecting a card opens that chat in its project.
- An overview aside lists the chats Cardo started, grouped as Needs you, In progress, Scheduled, and Done, each row opening its chat. Empty groups are omitted.
- The avatar in Cardo's conversation is the companion as the person has colored and dressed it. Cardo's name is the destination's name and is never used as the name of a speaker.

## 4. Thread configuration

- Cardo's thread uses the Agent mode with a coordinator tool policy: the Desktop thread tools, read-only file and web tools, and no file-editing, shell, or SubAgent tools. The policy is enforced by the runtime, not by instructions alone.
- Cardo's instructions add the role and reporting rules above to the generated base instructions. They replace the default chat guidance on offers: in Cardo, `CreateThread` for project work uses `userAsked`, and `ownInitiative` proposals remain available for follow-ups Cardo notices.
- Cardo's thread uses the person's default model and reasoning settings unless they change them in its composer.
- Memory follows the default Chat workspace, so what Cardo learns about the person's preferences is available to it across days without being copied into project memories.

## 5. Scope

- Cardo starts chats in local projects, including projects whose AppServer is not running, and in the default Chat workspace. Remote projects on SSH machines and paired computers are not targets.
- Cardo does not schedule work itself. Scheduled work in its overview comes from Automations the person created.
