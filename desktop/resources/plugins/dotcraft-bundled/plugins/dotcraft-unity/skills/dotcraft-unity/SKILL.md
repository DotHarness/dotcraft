---
name: dotcraft-unity
description: Use when a DotCraft thread is connected to Unity Editor through dotcraft-unity, when unity_execute_csharp is available, or when the user asks to inspect, automate, capture, or debug a Unity project from the Editor. Provides safe background-first rules for Unity Editor automation and GameView capture.
---

# DotCraft Unity

## Overview

Use `unity_execute_csharp` for short Unity Editor method-body snippets that run inside the connected Editor process. Treat it as live editor automation: it can inspect and mutate scene, asset, and project state.

This skill governs Unity tool calls, especially `unity_execute_csharp`. It does not replace normal repository file editing workflows.

## References

Load only the reference needed for the current task:

- **GameView screenshots**: Read `references/gameview-screenshot.md` when the user asks to capture, compare, inspect, or save the Unity GameView image.
- **Console reading**: Read `references/console-reading.md` when the user asks to inspect, summarize, search, or diagnose Unity Console logs.
- **API helpers**: Read `references/api.md` when a `unity_execute_csharp` snippet needs loaded-type lookup or third-party component reflection.

## Default Behavior

Keep Unity in the background by default. Do not change the user's current Editor focus, selected window, dock layout, active view, or Play Mode state unless the user explicitly asked for that visible action.

Prefer read-only inspection first, then make the smallest useful change when the user's request clearly asks to fix, create, modify, or automate something in Unity.

Return concise summaries from snippets. Include paths, object names, counts, and error messages that help the next step. Avoid dumping huge JSON, broad hierarchy listings, or full asset databases unless the user asks.

## Good Uses

- Read Console logs, Editor state, Play Mode state, build settings, scene names, selected objects, GameObjects, Components, serialized fields, asset metadata, and project settings.
- Run small, bounded Editor automations the user requested, such as creating objects, adjusting components, updating selected assets, saving a scene, or validating a setup.
- Use `AssetDatabase`, `EditorSceneManager`, `PrefabUtility`, and `Undo` deliberately for bounded asset, scene, and prefab work.

## Avoid By Default

Do not call these APIs unless the user explicitly asked for the visible effect or there is no background-safe alternative and you explain it first:

- `EditorWindow.Show()`, `ShowUtility()`, `Focus()`, `GetWindow(..., focus: true)`, or any equivalent foreground activation.
- `EditorUtility.DisplayDialog`, modal prompts, object pickers, menu execution, or other UI that requires user interaction.
- `EditorGUIUtility.PingObject`, `AssetDatabase.OpenAsset`, `Selection.activeObject`, or selection changes whose purpose is only visual navigation.
- `EditorApplication.ExecuteMenuItem`, layout changes, opening Unity windows, docking changes, or changing the active view.
- Entering or exiting Play Mode, pausing, stepping frames, or changing time scale unless the user requested runtime inspection.
- Broad `AssetDatabase.Refresh`, `ImportAsset`, `ForceReserializeAssets`, scene saves, package changes, or project-wide rewrites.

Do not use long polling loops. If Unity needs time to compile, import, enter Play Mode, or finish an async action, return the current state and ask for or perform one bounded follow-up check.

## Confirmation Policy

If the user's request clearly asks for a Unity change, perform ordinary bounded changes without extra confirmation. Ask before high-risk actions:

- Deleting scenes, assets, GameObjects, Components, prefabs, or project settings.
- Entering or exiting Play Mode, changing scenes, saving dirty scenes, or modifying build settings.
- Package Manager changes, project-wide asset import/reimport, `AssetDatabase.Refresh`, or large batch operations.
- Running external processes, opening network resources, or writing outside the workspace/project tree.
- Any action that may interrupt the user's visible Editor session.

When confirmation is needed, describe the exact Unity action and the state it may change.

## Snippet Style

Keep snippets short and deterministic. Prefer one clear task per call.

Use `DotCraft.Editor` API helpers for high-boilerplate inspection work. `unity_execute_csharp` snippets already import this namespace, so use `Dcu.Type`, `Dcu.Components`, `Dcu.Get`, and `Dcu.Call` directly when they replace substantial reflection or type-search code.

Validate inputs and return early with a readable message when required objects, assets, scenes, or Unity internal APIs are missing.

For mutations, use `Undo.RecordObject` or `Undo.RegisterCreatedObjectUndo` where practical, mark dirty objects deliberately, and return what changed. Avoid silent changes.

For inspection, return a compact string or small object with only the fields needed for the next decision.
