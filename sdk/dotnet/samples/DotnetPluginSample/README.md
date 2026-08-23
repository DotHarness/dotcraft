# .NET plugin sample

Two prepared bundles that together cover every public .NET contribution point and export a typed
service across a plugin boundary.

| Bundle | What it is |
|---|---|
| `acme.review-core` | The provider. Contributes to every contribution point in the catalog, reads its activation settings snapshot, and exports `IReviewService`. |
| `acme.review-consumer` | The consumer. Declares a minimum compatible `acme.review-core` version, resolves the exported service during activation, and contributes on top of it. |

## Build

```powershell
dotnet build .\ReviewProvider\ReviewProvider.csproj
dotnet build .\ReviewConsumer\ReviewConsumer.csproj
```

```bash
dotnet build ReviewProvider/ReviewProvider.csproj
dotnet build ReviewConsumer/ReviewConsumer.csproj
```

Each project writes its output into `bundles/<pluginId>/lib/`, next to its prepared manifest. Both
projects reference `ReviewApi` directly, so either may be built first. DotCraft loads these built
artifacts as-is; it does not restore packages, run MSBuild, or compile source during activation.

## Install, trust, activate

1. **Install.** Copy both bundle directories from `bundles/` into the workspace's `.craft/plugins/`,
   or install each from disk in Desktop. Order does not matter: a consumer can install while its
   provider is absent and remains `blocked` until the dependency is available.
2. **Trust.** Confirm each bundle's current fingerprint before activation. The Host stores the exact
   plugin-id/fingerprint pair in the machine-local `dotnet-plugin-trust.json` authority file next to
   global config. The authority is not workspace config, and one plugin id may retain grants for
   several fingerprints. Revocation removes only the current pair. Treat the bundle as
   full-authority code with filesystem, network, credential, and native access.
3. **Activate.** Enable both plugins. Providers activate before consumers; disabling a provider or
   revoking its trust stops consumers first and leaves them blocked on the missing dependency.

Verify a build end to end with the Host's own admission, preflight, trust, and runtime:

```powershell
.\verify.ps1
```

The script builds both bundles and runs `DotnetPluginSampleBundleTests` through real admission,
preflight, trust, and activation. Tests assert observable contribution effects, run a deterministic
turn through the official Generic Host and `ISessionService`, verify teardown restores built-ins, and
fail the catalog census when a public contribution contract lacks a sample disposition.

## What the sample contributes

### Prompt and context

| Contribution point | Contribution | Bundle |
|---|---|---|
| `ISystemPromptSection` | A `review-checklist` section added between two built-ins, sized from the plugin's settings | provider |
| `ISystemPromptSection` | A **Tier-B replacement** of the built-in `response-style` section | provider |
| `ISystemPromptSection` | A section that reads the provider's exported checklist | consumer |
| `ISystemPromptAssembler` | A **Tier-C takeover** that receives the whole assembled prompt and appends a trailer | provider |
| `IChatContextProvider` | A stable line surfaced through the `chat-context` section | provider |
| `IThreadSystemPromptContextProvider` | A base-instruction page in the `thread-context` section | provider |
| `IAgentContextSource` | An `AIContextProvider` ordered behind the built-in `memory` entry | provider |
| `ICompactionSummarizer` | A **Tier-B replacement** of the built-in `local-summary` summarizer | provider |
| `ICompactableToolPolicy` | An opinion that makes this plugin's stale Tool results prunable | provider |

### Chat pipeline

| Contribution point | Contribution | Bundle |
|---|---|---|
| `IChatMiddleware` | An observing wrapper around the model call, outside the built-ins; declines the SubAgent pipeline | provider |

### Tools

| Contribution point | Contribution | Bundle |
|---|---|---|
| `IToolSource` | `review.summary` and `review.publish` | provider |
| `IToolSource` | `review.normalize`, backed by the provider's exported service | consumer |
| `IToolPolicyEvaluator` | Denies review input longer than the activation snapshot's configured limit | provider |
| `IToolApprovalEvaluator` | Refuses `review.publish` without an explicit `approved` argument | provider |
| `IToolInvocationRecorder` | A dispatch-stage recorder joining the Host's own rather than replacing it | provider |
| `IToolResultNormalizer` | Stamps the content the Host's own normalizer produced | provider |
| `IToolRestriction` | Masks `review.publish` from the model and rewrites `review.summary`'s description | provider |

### Session lifecycle and product surfaces

| Contribution point | Contribution | Bundle |
|---|---|---|
| `IThreadLifecycleContributor` | Thread start and delete observation | provider |
| `ITurnLifecycleContributor` | Turn start and end observation | provider |
| `IThreadRuntimeSignalContributor` | Observation of the runtime signals no turn callback expresses | provider |
| `ICommitMessageSuggester` | A **Tier-B replacement** of the source-control summary generator | provider |
| `IWelcomeSuggester` | A **Tier-B replacement** of the welcome suggestion generator | provider |
| `ISubAgentRuntimeSource` | A `acme-review-pass` runtime and the `review-pass` profile it ships | provider |
| `ICodeCommand` | A `/review` slash command (alias `/rv`) that expands into model input | provider |
| `ITraceSink` | A read-only fan-out over what `TraceStore` has just recorded | provider |

The sample writes coarse event names and outcomes to `activity.log` under
`IPluginActivationContext.DataRoot`. It intentionally omits prompt content, errors, paths, and
thread, turn, call, session, and generation identifiers.

## Settings

The provider reads a snapshot of its own effective settings from Host config at activation. Its code
defines a fallback for every key because plugin settings are open plugin-owned data and the Host does
not validate their shape.

| Key | Type | Default | What it changes |
|---|---|---|---|
| `checklistLimit` | integer, 1–10 | `3` | How many checklist items the `review-checklist` prompt section renders |
| `tone` | `direct` \| `coaching` | `direct` | The tone the replaced response-style section asks for, and the wording the tool restriction writes |
| `maxInputLength` | positive integer | `2000` | Maximum `text` length accepted by the plugin's dispatch policy stage |

Values live in Host config under `Plugins.Settings["acme.review-core"]` and merge global→workspace per
field, alongside `EnabledPlugins`:

```json
{
  "Plugins": {
    "Settings": {
      "acme.review-core": { "checklistLimit": 5, "tone": "coaching", "maxInputLength": 4000 }
    }
  }
}
```

A plugin reads only its own bag through `IPluginActivationContext.Settings`. The value is fixed for
that activation generation; a config edit becomes visible only after runtime reconciliation restarts
the generation. The Host does not validate the bag, so the plugin supplies runtime fallbacks.

## What the suite does not prove

The harness can check only registration for `IAgentContextSource`, `IThreadLifecycleContributor`, and
`ITurnLifecycleContributor`, whose readers require a real session path. The coverage census records that
limit explicitly; the privacy-minimized `activity.log` can confirm their coarse event names in a live session.

## Replacement and takeover

`ReviewResponseStyleSection` uses `ReplaceTarget` to shadow a named built-in while its handle lives;
disposing the handle restores the built-in. Returning `null` suppresses a replaceable section.
`ReviewPromptAssembler` demonstrates Tier C: the last resolved assembler receives the assembled result
and returns the final one.

## Host version binding

Plugins compile directly against `DotCraft.Core`, so `dotnet.minHostVersion` declares the oldest Host
they support. An older Host blocks the plugin before code runs; a newer Host loads it best-effort.
Rebuild against each Host minor compatibility target. Do not ship DotCraft assemblies in the bundle:
the Host supplies them by simple name, and `Private="false"` plus `HostAssemblies.targets` keeps them out.

## Owning resources

Teardown revokes handles before in-flight Tool calls drain. Own call-scoped resources through
`context.Lifetime.Own` or `OwnAsync`, which release them after the drain:

```csharp
var journal = new ReviewJournal(context.DataRoot);
context.Lifetime.Own(journal);
context.Contributions.Add<IToolSource>(new SummaryTool(service, journal));
```

Every `Contributions.Add` call belongs in `ActivateAsync`. The registrar seals when activation
commits; background work cannot add a contribution to an already active generation.

Run background work through `context.Lifetime.Run`; raw threads, static subscriptions, and global
caches can pin the collectible load context after routing has stopped.

An ordinary plugin mutation waits only up to the runtime cleanup timeout, but a generation remains
functionally pending until its work actually settles; it cannot overlap a replacement or provider
teardown. Host shutdown waits for that functional teardown before disposing providers and the host
root. The outer process owns any hard shutdown deadline. Collectible-context GC happens later and
does not block runtime progress.

## Typed services across plugins

Public service interfaces live in their own assembly, listed in `exportedApiAssemblies`. The provider
exports an implementation during activation:

```csharp
context.Exports.Add<IReviewService>(new ReviewService());
```

The consumer declares the minimum compatible provider version it needs and resolves it during activation:

```json
{ "dependencies": { "acme.review-core": "1.0.0" } }
```

```csharp
var review = context.Dependencies.GetRequired<IReviewService>("acme.review-core");
```

`Exports` and `Dependencies` are activation-only. The declared value is a minimum within one
compatibility line: `1.0.0` accepts `1.x` versions at or above `1.0.0`, but not `2.0.0`; a `0.x`
requirement accepts only the same major and minor (for example, `0.2.1` through later `0.2.x`
versions). Within that line, a provider keeps each exported API assembly's simple name,
`AssemblyVersion`, culture, and public-key token unchanged. A breaking API needs a new compatibility
line and should normally use a new plugin and API identity. Exported signatures may use only types
the consumer can resolve: exported plugin types and Host assemblies shared by both plugins.

## Related docs

- [Build a .NET plugin](../../../../docs/developing/integrations/dotnet-plugins.md)
