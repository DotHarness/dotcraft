# Compilation And Domain Reload

Compilation and Domain Reload unload the current bridge. Any active execution becomes `lost`, even when the requested Unity operation succeeds.

When source files change, finish the host-side file edits first. If Unity does not begin compiling on its own, request compilation once:

```csharp
UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
return "compilation requested";
```

For a reload without compilation, request it once:

```csharp
UnityEditor.EditorUtility.RequestScriptReload();
return "reload requested";
```

The initiating response may be lost. Do not replay either request. Wait until the Editor is responsive, then call `unity.connect` without a PID. A successful connection creates a new generation. Confirm readiness with `unity.status`, then read the Console for compiler errors before continuing.

Code that must survive a reload belongs in project source or another persistent Unity integration. An attached snippet and its background execution cannot survive the managed domain that contains them.
