# Unity

Inspect and automate Unity Editor. Read Editor state, execute C#, and perform editing operations.

Supports Windows x64 Mono Unity Editor.

## Enable

Add this local plugin through DotCraft plugin settings, then enable and trust **Unity**. The plugin provides Unity tools and a companion skill.

## Use

Open a Unity project, then ask DotCraft to connect to its Editor. For example:

> Connect to Unity Editor and tell me its version and whether it is in Play Mode.

Pass inline code or a script path to `unity.execute`. Code can wait for Editor updates with `await ctx.WaitFrame()`. For longer work, set `runInBackground` and use the returned execution ID with `unity.wait`.

Reconnect after a script reload. When an execution result is unknown, inspect its state before deciding whether to retry.
