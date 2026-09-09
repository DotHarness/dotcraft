# Unity Console

Use the bundled `scripts/console-read.cs` with `unity.execute(path=...)`. Pass its absolute installed path and optional limits:

```json
{
  "path": "<skill-directory>/scripts/console-read.cs",
  "args": {
    "maxRows": 20,
    "maxMessageLength": 500
  }
}
```

The script reads `UnityEditor.LogEntries` through reflection. It does not open or focus the Console window and does not change its filters.

Treat `VisibleRows` as the current Console view. Search text, enabled log levels, and collapse settings affect it, so check `FilteringText` and `ConsoleFlags` before concluding that a message is absent. Increase `maxRows` carefully because messages can contain long stack traces.

Do not clear logs or change Console settings unless the user requests that change.
