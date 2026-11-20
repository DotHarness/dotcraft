# DotCraft Memory Studio Specification

| Field | Value |
|-------|-------|
| **Version** | 0.13.2 |
| **Status** | Draft |
| **Date** | 2026-11-20 |
| **Related Specs** | [Memory](memory.md), [Dreams](dreams.md), [Desktop Extensions](../architecture/desktop-plugins.md) |

Purpose: Define **Memory Studio**, a bundled Desktop plugin where the user browses and edits memory and tailors the [memory policy](memory.md#10-memory-policy) from presets. Memory Studio is presentation and editing only. It holds no memory or policy of its own: memory stays in the stores, and the policy stays in configuration, so the agent behaves the same in every client whether or not the plugin is enabled.

## 1. Surfaces

| Surface | Contribution |
|---------|--------------|
| **Memory** main view | **Memory** and **Dreams** views, switched at the left of the top bar, and a **Policy** page opened from its settings action. |
| Summary | The memories a thread used, listed under **Sources** (§5). |
| Assistant message action | **Remember**, which starts a memory entry from the message (§6). |

Every read and write goes through the connected AppServer: memory files through the file system methods, the policy through configuration methods, and stores and Dreams through their memory and Dreams methods. Memory Studio therefore works the same against local and remote workspaces, and it shows a notice instead of a view whose server capability is missing.

The main view follows Desktop's catalog pages. Memory and Dreams share one hero title, as Plugins and Skills do. The top bar holds Refresh and Policy as icon-only actions and **New** as the one labelled action, and keeps them while the user switches views. Policy, an open entry, and a Dreams run open as pages that replace the view switch with a breadcrumb back to their view.

## 2. Entries

The Memory view lists the stores from `memory/listStores` and the memory files in each, with search over name, description, and body, and filters by store, type, and age. Each row shows the name, the type as a quiet label, the description, and the last-modified date. A memory not modified for more than 30 days is marked as possibly stale with a warning indicator beside its date.

Opening an entry shows it as a page: `name` is the page title and `description` its subtitle, both edited in place. Under them, the type is a tag that opens a menu of the effective policy's types, plus the entry's current type when the policy does not declare it. The body is Markdown. An existing entry opens with the body rendered directly on the page, without a frame or fill. One icon button, or a double-click on the body, switches to the source in a framed field that grows with its content, and Escape returns to the preview. A new entry opens in the source. The page's actions sit in its top bar, so switching between preview and source never moves them. **Delete** is in a more-actions menu. Save and Cancel appear only once the draft differs from what was loaded, and Cancel restores it. A new entry always shows them, and its Cancel closes the page. Naming rules appear only when the name breaks them.

- **Save** writes the file and keeps its index line in step: it adds `- [Title](name.md) — hook` for a new entry, updates the line of an existing one, and renames both when `name` changes. The hook defaults to the description.
- **Delete** removes the file and its index line after confirmation.
- Before writing, the editor checks that the file and the index are unchanged since it read them. When either changed, it refuses the save, keeps the user's text, and shows the current content. The check and the writes are separate requests, so a write by the agent, extraction, or Dreams in between is not detected. A rename writes the new file and the index before removing the old file, so a failure part-way leaves at most an unindexed file.
- Attached stores are read-only.

These edits are the user editing files, so they reach running threads through the memory index section like any other change.

Memory Studio keeps each writable store's index in step with its files without asking the user, and does not offer the index for editing. Each time it loads a store, it appends a pointer line, with the description as the hook, for every memory file no index line links to, and removes pointer lines whose memory file is missing. It leaves every other line alone, including grouped or free-form lines the agent wrote, since Core already asks the agent to tidy those. The write uses the same unchanged-since-read check as **Save** and is skipped on a conflict until the next load.

A store whose index fills more than 80% of a load limit (200 lines, 25,000 bytes) shows its usage. Past a limit, it warns that the agent sees only part of the index.

## 3. Policy

The Policy page edits the memory policy settings: the types, the save, extraction, and consolidation guidance, and the extraction interval. It is laid out as Settings rows. The first row names the preset the effective policy matches, or shows it as customized, and applies another. The other groups list the types and the three guidance settings, each with a short description. A row is marked only when a personal or workspace value changes it.

Editing a setting opens it on its own page, under Memory › Policy. The extraction interval is edited with the extraction guidance. **Save** writes the draft to the user layer, for all of the user's workspaces. Its menu saves it to the workspace layer instead, for this workspace only. When the draft differs from the default it can show the difference, and when a layer sets the value it can restore the default by removing that value.

### 3.1 Presets

Memory Studio ships presets as complete policies to start from. Applying a preset writes all of its settings to the user layer in one configuration write, after confirmation when that replaces existing values. A value the workspace layer sets still takes precedence.

| Preset | Types | Character |
|--------|-------|-----------|
| **DotCraft** | `user`, `feedback`, `project`, `reference` | The defaults. Applying it removes the policy values from the user layer. |
| **Profile and notes** | `user`, `note` | A small, curated profile of the user and short working notes. Saves sparingly, keeps each entry to a sentence or two, and consolidation compacts the store toward a few dozen entries. |
| **Atomic facts** | `preference`, `fact`, `event` | One self-contained fact per file. Extraction adds new facts and updates only a fact that states the same thing. Consolidation resolves duplicates and contradictions, and an `event` carries an absolute date. |

Every preset uses the default extraction interval. Presets change only with a plugin release, and a policy applied from an older preset stays as written.

## 4. Dreams

The Dreams view lists Dreams runs, with search over the store, summary, and message and filters by store and status, and opens a run for review: its summary and per-file diffs, with **Apply** and **Discard**. It uses the Dreams methods and their conflict rule, so applying a run whose store changed fails with `storeChanged` and leaves the store untouched. Without Dreams on the server the view is absent.

## 5. Summary Sources

In a thread's Summary, Memory Studio adds the memories the thread read or wrote to **Sources** through `summary.sources`, most recent first, each named by its file. It derives them from the memory presentation marks on the thread's file tool calls, so it shows exactly what Core counts as memory operations and never guesses from paths. Selecting a memory opens it in the Memory view. It adds no rows when the thread has no memory operations.

## 6. Remember Action

An assistant message gets a **Remember** action. It opens a new entry in the workspace store, which the user may change to another writable store, with the message text as its body. Nothing is saved until the user saves the entry, so the action never writes memory on its own.
