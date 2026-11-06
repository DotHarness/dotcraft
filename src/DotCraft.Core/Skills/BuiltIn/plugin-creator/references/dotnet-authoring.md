# Managed .NET extension authoring

A .NET extension project lives under `.craft/plugin-projects/<plugin-id>`:

```text
.craft/plugin-projects/my-plugin/
├── src/
│   └── Plugin.cs
└── plugin/
    ├── .craft-plugin/plugin.json
    ├── assets/
    └── lib/
```

Copy `assets/template-dotnet-plugin.txt` to `src/Plugin.cs` and `assets/template-plugin.json` to the bundle's manifest. For an id such as `my-plugin`, add this to the manifest and add `dotnet` to `capabilities`:

```json
"dotnet": {
  "minHostVersion": "<running DotCraft version>",
  "entryAssembly": "./lib/MyPlugin.Plugin.dll",
  "entryType": "DotCraft.Plugin.MyPlugin.Plugin"
}
```

Do not add a `.csproj`, package references, NuGet restore, or copied DotCraft assemblies. Use `DotNetPlugin.Inspect` when an exact public type, signature, or XML summary is needed, then call `DotNetPlugin.Build` with the plugin id. The build compiles against the current Host API, updates `plugin/lib`, and activates the resulting development plugin. A newly contributed tool is available from the next turn.

Contribution points are documented at `https://www.dotcraft.net/extend/dotnet-extension-api`.

The candidate for `dotcraft plugin verify` is the project's `plugin/` directory, after the build.

## With Desktop UI

The same plugin may also contribute Desktop UI under one id. Put its `desktop/` directory inside `plugin/`, and read `references/desktop-authoring.md`. Run `npm install` and `npm run build` from `plugin/desktop` before calling `DotNetPlugin.Build`, because managed preflight validates every declared Desktop output.
