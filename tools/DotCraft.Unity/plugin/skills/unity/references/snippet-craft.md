# Snippet Troubleshooting

Use this reference when `unity.execute` fails to compile, times out, or returns an unexpected result.

## Compilation Errors

Read the Roslyn diagnostics before changing the snippet. `System.Object`, `Random`, and `Debug` can conflict with Unity types; qualify the intended type when needed. Internal Unity APIs vary by Editor version, so inspect a type with `Dcu.Members` instead of guessing members repeatedly:

```csharp
var type = Dcu.Type("UnityEditor.SceneManagement.EditorSceneManager", false);
return type == null ? null : Dcu.Members(type, "Save", true, 40);
```

## Work Across Frames

Use `await ctx.WaitFrame()`, `WaitFrames`, `WaitSeconds`, or `WaitUntil` when Unity must advance before the result can be read. These helpers resume the snippet on the Editor main thread. Do not block that thread with `Thread.Sleep` or a busy loop.

Use background execution when the work can outlive the initial tool call. Keep the returned execution ID and query it with `unity.wait`. Cancellation is cooperative; a synchronous loop must call `ctx.ThrowIfCancellationRequested()`.

## Unknown And Lost Results

`unknown` means the host cannot prove whether a dispatched request ran. Inspect Editor state before deciding whether to retry. `lost` means the process, bridge, or domain generation changed; reconnect and verify the intended effect with a new read-only call.

## Compact Results

Return only fields needed for the next decision. Common Unity value types serialize directly. For complex Unity objects, construct an anonymous object with the required fields. Avoid logging inside large loops because Console output can obscure the useful result.
