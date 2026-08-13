# Runtime module boundaries

DotCraft runtime assemblies follow a one-way dependency model:

```text
Feature assembly -> DotCraft.Core -> DotCraft.Agents / DotCraft.Protocol
DotCraft.App -> DotCraft.Core + shipped feature assemblies
```

`DotCraft.Core` owns the session and agent kernel plus contracts that are shared by multiple
features. A feature assembly may consume Core services and contracts, but Core must not reference a
feature implementation. `DotCraft.App` is the built-in composition root and is responsible for
wiring shipped features together.

Feature-to-feature references are not used to contribute optional runtime state. A contributor
implemented by one feature and consumed by another must implement a narrow, feature-neutral Core
contract. Such contracts must describe the contributed runtime capability rather than expose the
implementing feature's concrete services.

## Dashboard

`DotCraft.Dashboard` owns the hosted Dashboard surface:

- Dashboard HTTP routes and response projection;
- Dashboard authentication middleware;
- interactive and read-only host capability selection;
- Dashboard-specific trace and thread-operation readers; and
- embedded Dashboard and login HTML resources.

Dashboard consumes tracing, persistence, configuration, Dreams, session, and tool contracts from
Core. Core does not reference `DotCraft.Dashboard`.

Runtime modules that expose Dashboard-visible orchestrator state implement the Core-owned
`IOrchestratorSnapshotProvider` contract. `DotCraft.App` discovers those providers and supplies them
when mounting Dashboard routes. This keeps provider modules and Dashboard independently dependent on
Core.

The standalone `dotcraft dashboard` command and in-process channel/AppServer mounting remain in
`DotCraft.App`, because they select process mode, addresses, and enabled shipped modules. Dashboard
configuration keys and workspace persistence formats remain unchanged.

Dashboard behavior tests belong to `DotCraft.Dashboard.Tests`. Composition-root tests remain in
`DotCraft.App.Tests`; Core tests must not depend on the Dashboard implementation.
