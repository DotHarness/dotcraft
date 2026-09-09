# Unity plugin

## Product and ownership

Plugin ID: `DotCraft.Unity`. Display name: **Unity**. The plugin inspects
and automates running Windows x64 Mono Unity Editors through five existing tools:
`unity.list/connect/status/execute/disconnect`.

The bundle implements the public .NET plugin contracts. Core and App have no Unity
composition reference. Activation registers tools without attaching. Public tool
names and schemas remain stable across modes; Plan mode permits list/status only.

## Execution and lifecycle

The plugin owns target discovery, native bootstrap, target-reference compilation
and local session records. Managed bridge sources and the compiler are bundled.
Native libraries load from a hashed plugin data directory rather than the host
generation shadow copy. The bridge owns main-thread execution and a loopback
connection protected by a random credential and protocol version.

Connections bind PID, process start time, project and domain generation. Compilation
cache identity includes source, options, reference fingerprints and domain generation.
Code compiled for an earlier generation is rejected before transmission.

Queued requests may be cancelled before execution. A timeout after execution starts
has an unknown outcome and must not imply termination or trigger replay. Connect is
explicit after reload and must establish fresh metadata before new execution.
An uncertain bootstrap is not repeated by the same service instance.

Deactivation revokes tools and attempts to stop owned bridge callbacks and listeners.
It does not promise target assembly unloading. Cleanup failures remain in plugin data.
Abrupt host termination cannot guarantee cleanup.

## Delivery

The bundle contains the manifest, entry assembly and dependency manifest, private
compiler assemblies, native library, product icon, user skill, and a single Chinese README.
Developer documentation, tests, build caches and session evidence are not packaged.

## Repository layout

`tools/DotCraft.Unity/src` owns managed plugin code and the target payload. `native`
owns the native loader source. `plugin` is the complete
deliverable, including its manifest, icon, skill, README and built binaries. Run `build.bat`
at the tool root to populate the plugin. It forwards arguments and the exit code to
the PowerShell implementation in `scripts/build.ps1`.
