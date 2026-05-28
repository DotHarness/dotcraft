# DotCraft SDK Specifications

This directory contains the SDK specifications for DotCraft.

| File | Purpose |
|------|---------|
| [sdk.md](sdk.md) | Shared SDK contract and cross-language capability matrix. |
| [typescript.md](typescript.md) | TypeScript package, channel runtime, and Node-specific binding details. |
| [dotnet.md](dotnet.md) | .NET package, App Binding native-app helpers, NuGet, and C# binding details. |

`sdk.md` is the common source of truth for SDK behavior. Language binding specs may add idiomatic API shapes, package/runtime constraints, and language-specific profiles, but they must not redefine shared AppServer, Hub, App Binding, or Session semantics.

