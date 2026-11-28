# DotCraft Memory Studio Specification

| Field | Value |
|-------|-------|
| **Version** | 0.15.0 |
| **Status** | Draft |
| **Date** | 2026-11-26 |
| **Related Specs** | [Memory](memory.md), [Dreams](dreams.md), [Desktop Extensions](../architecture/desktop-plugins.md) |

Purpose: Define **Memory Studio**, a bundled Desktop plugin where the user browses and edits memory and tailors the [memory policy](memory.md#10-memory-policy). Memory Studio is presentation and editing only. It holds no memory or policy of its own: memory stays in the stores, and the policy stays in configuration, so the agent behaves the same in every client whether or not the plugin is enabled.

## 1. Surfaces

| Surface | Contribution |
|---------|--------------|
| **Memory** main view | **Memory** and **Dreams** views, switched at the left of the top bar, with a settings action that opens Settings › Personalization. |
| App overlay (`app.overlay`) | Compact memory modal dialog for creating, viewing, editing, and deleting a memory from Memory Studio, **Remember**, or thread **Sources** (§2, §5, §6). |
| Settings (`settings.personalization`) | **Memory policy** settings under Settings › Personalization (§3). |
| Summary | The memories a thread used, listed under **Sources** (§5). |
| Assistant message action | **Remember**, which opens the **New memory** dialog over the current conversation (§6). |

Every read and write goes through the connected AppServer: memory files through the file system methods, the policy through configuration methods, and stores and Dreams through their memory and Dreams methods. Memory Studio therefore works the same against local and remote workspaces, and it shows a notice instead of a view whose server capability is missing.

The main view follows Desktop's catalog pages. Memory and Dreams share one hero title, as Plugins and Skills do. The top bar holds Refresh and Settings as icon-only actions and **New** as the one labelled action, and keeps them while the user switches views. An open entry and a Dreams run open as pages that replace the view switch with a breadcrumb back to their view.

## 2. Entries

The Memory view lists the stores from `memory/listStores` and the memory files in each, with search over name, description, and body, and filters by store, type, and age. The workspace store is titled by the foreground workspace's name, as in "dotcraft memory", and that name switches the workspace in place. Memories show as cards: the type and the last-modified date on the first line, then the name and up to two lines of description. A memory not modified for more than 30 days is marked as possibly stale with a warning indicator beside its date.

Creating or opening a memory opens a compact modal dialog over the current view: lightweight pills show and choose the `type` (including an entry's undeclared type so opening never changes it) and `store` (selectable across writable stores when creating), `name` and `description` are edited in place as frameless card headings, and the Markdown `body` sits in a recessed frameless surface. A new memory opens in source mode. An existing memory opens in Markdown preview, where one icon button or a double-click switches to source and Escape returns to preview. **Delete** sits at the left of the footer for writable existing memories, with **Cancel** and **Save** at the right. Naming rules appear only when the name breaks them.

- **Save** writes the file and keeps its index line in step: it adds `- [Title](name.md) — hook` for a new entry, updates the line of an existing one, and renames both when `name` changes. The hook defaults to the description.
- **Delete** removes the file and its index line after confirmation.
- Before writing, the editor checks that the file and the index are unchanged since it read them. When either changed, it refuses the save, keeps the user's text, and shows the current content. The check and the writes are separate requests, so a write by the agent, extraction, or Dreams in between is not detected. A rename writes the new file and the index before removing the old file, so a failure part-way leaves at most an unindexed file.
- The personal store is listed first and edited like the workspace store. Attached stores are read-only.
- The personal store and the workspace store each have a more-actions menu on their title, shown while the pointer is over the title, that clears the store's memory files and index after confirmation. The personal store's confirmation says every workspace on this computer uses it.

These edits are the user editing files, so they reach running threads through the memory index section like any other change.

Memory Studio keeps each writable workspace or scope store's index in step with its files without asking the user, and does not offer the index for editing. Each time it loads such a store, it appends a pointer line, with the description as the hook, for every memory file no index line links to, and removes pointer lines whose memory file is missing. It leaves every other line alone, including grouped or free-form lines the agent wrote, since Core already asks the agent to tidy those. The write uses the same unchanged-since-read check as **Save** and is skipped on a conflict until the next load. Browsing the shared personal store does not automatically rewrite its index, so concurrent writes from other workspaces are never pruned while loading.

A store whose index fills more than 80% of a load limit (200 lines, 25,000 bytes) shows its usage. Past a limit, it warns that the agent sees only part of the index.

## 3. Policy

Memory Studio adds a **Memory policy** group under **Settings › Personalization** (through `settings.personalization`) to edit the memory policy settings: the types, the save, extraction, and consolidation guidance, and the extraction interval. Each row shows a short description and is marked only when a personal or workspace value changes it. When any setting is customized, the group header offers a restore-defaults icon button that removes all personal and workspace policy overrides after confirmation.

Editing a setting opens a Settings subpage with a breadcrumb back to Personalization. Editing a type includes its `Personal` switch, which sends that type's memories to the [personal store](memory.md#42-personal-store). The extraction interval is edited with the extraction guidance. **Save** writes the draft to the user layer, for all of the user's workspaces. Its menu saves it to the workspace layer instead, for this workspace only. When the draft differs from the default it can show the difference, and when a layer sets the value it can restore the default by removing that value.

## 4. Dreams

The Dreams view lists Dreams runs, with search over the store, summary, and message and filters by store and status, and opens a run for review: its summary and per-file diffs, with **Apply** and **Discard**. It uses the Dreams methods and their conflict rule, so applying a run whose store changed fails with `storeChanged` and leaves the store untouched. Without Dreams on the server the view is absent.

## 5. Summary Sources

In a thread's Summary, Memory Studio adds the memories the thread read or wrote to **Sources** through `summary.sources`, most recent first, each named by its file. It derives them from the memory presentation marks on the thread's file tool calls, so it shows exactly what Core counts as memory operations and never guesses from paths. Selecting a memory opens its compact modal dialog directly over the conversation. It adds no rows when the thread has no memory operations.

## 6. Remember Action

An assistant message gets a **Remember** action. It opens the **New memory** modal dialog over the current conversation—without navigating away to the Memory view—defaulted to the workspace store (which the user may change to another writable store) and prefilled with the message text as its body. Nothing is saved until the user saves the dialog, so the action never writes memory on its own.
