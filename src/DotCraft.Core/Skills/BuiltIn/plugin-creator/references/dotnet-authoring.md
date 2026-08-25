# Managed .NET plugin authoring

Use the deployed creator to scaffold a project under `.craft/plugin-projects/<plugin-id>`:

```powershell
python .craft/skills/plugin-creator/scripts/create_basic_plugin.py "My Plugin" --dotnet
```

The scaffold contains only editable C# source and its development bundle:

```text
.craft/plugin-projects/my-plugin/
├── src/
│   └── Plugin.cs
└── plugin/
    ├── .craft-plugin/
    │   └── plugin.json
    └── lib/
```

Edit `src/**/*.cs` with the ordinary workspace file tools. Do not add a `.csproj`, package references,
NuGet restore, or copied DotCraft assemblies. Use `DotNetPlugin.Inspect` when an exact public type,
signature, or XML summary is needed, then call `DotNetPlugin.Build` with the plugin id. The build
compiles against the current Host API, updates `plugin/lib`, and activates the resulting development
plugin. A newly contributed Tool is available from the next Turn.

Keep `.craft-plugin/plugin.json` aligned with the generated entry assembly and entry type. Keep the
authoring project focused on its managed contributions.
