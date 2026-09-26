---
name: dotcraft-simplify
description: Find, evaluate, propose, or implement evidence-backed simplifications in DotCraft. Use for audits, reviews, or refactors involving dead or duplicated code, speculative abstractions, redundant state or lifecycle machinery, unnecessary compatibility paths, or hand-rolled infrastructure.
---

# Simplifying DotCraft

Turn broad simplification requests into a few well-proven changes that reduce real ownership, API, state, dependency, or maintenance surface. Preserve required behavior and intentional boundaries; fewer lines alone do not make a system simpler.

Read the owning specs and inspect the current diff before judging whether a surface is unnecessarily complex.

## Recognize Strong Candidates

Prefer candidates with concrete evidence that their cost exceeds their value:

- A public method, DTO field, event, config option, service registration, module facet, helper, package, or UI state has no production consumer.
- Tests, docs, fixtures, or snapshots are the only consumers, and no current contract requires the behavior.
- Multiple mutable owners represent the same lifecycle fact and can drift or require separate cleanup.
- A general Core or Protocol contract exists for one feature-specific caller and can become a private capability owned by that feature.
- A compatibility adapter, fallback, migration, or defensive branch protects no supported persisted data, wire client, provider, plugin, or entry point.
- Hand-rolled infrastructure duplicates a suitable platform or established dependency and replacing it yields net deletion.
- A change introduces flexibility, representations, or abstractions that no present requirement or second independent consumer needs.

Reject thin claims such as “this looks complex,” raw unused-code-tool output, or cleanup with no ownership benefit. Prefer a few high-confidence candidates over a long inventory of guesses.

## Survey Broadly

Start with the largest or most lifecycle-heavy production surfaces, then cover adjacent owners so the first easy finding does not end the audit. Useful domains include:

- Agent and Session Core: turn boundaries, tool calls, cancellation, persistence, recovery, replay, and live thread state.
- Runtime, AppServer, and Protocol: host lifecycle, modules, providers, projections, capability negotiation, subscriptions, and process ownership.
- Features and channels: optional-feature isolation, adapter contracts, background workers, external processes, and host wiring.
- Desktop: protocol mapping, duplicated client state, feature ownership, shared primitives, localization, accessibility, and styles.
- SDKs, scripts, examples, tests, and docs: duplicated models, obsolete compatibility helpers, publish boundaries, and misplaced support code.

## Prove Or Reject Each Candidate

Use `rg` first. Search exact symbols, config keys, protocol strings, registrations, implementations, and call forms, then read the callers.

Classify consumers as production, non-production, or ambiguous. Inspect ambiguous and dynamically discovered paths, and account for external consumers before treating a surface as unused.

For each candidate, identify its current owner, observable contract, production consumers, companion artifacts, and the capability or compatibility that removal gives up. Reject or downgrade it when a production consumer exists, a governing spec requires the separation, compatibility survives, or the change only moves complexity behind a wrapper.

## Protect Intentional Boundaries

- Do not cross established assembly, feature, and host ownership merely to reduce wiring, and do not create a second session kernel.
- Domain history, exact model or provider history, protocol projections, and UI state may be intentionally distinct. Merge them only when they encode the same fact and can share one owner.
- Stable protocol, persistence, plugin, and SDK surfaces may have external consumers even when the repository has no caller.
- Change the generator, schema, catalog source, or discovery input that owns generated files rather than editing generated output.

When a simplification changes an owned protocol design or process flow, update that design first. Compatibility indirection or a broader public API is not a simplification merely because it makes a move compile.

## Audit Trust And Lifecycle Machinery

For each defensive copy, validator, lock, queue, cancellation source, readiness signal, state flag, callback capture, and disposer, identify the trust boundary and owner transition. Keep validation at untrusted or durable boundaries; challenge repeated validation or copying between same-process typed services when ownership and immutability already establish the contract.

For asynchronous flows, map each mechanism to a distinct transition or owner. Collapse mechanisms only when they encode the same fact. Preserve machinery that protects atomic publication, terminal-outcome arbitration, rollback, callback containment, replay or idempotency, process ownership, or disposal to quiescence.

## Evaluate Dependency Replacements

Prefer capabilities already available at DotCraft's supported platform floors. Before adding a dependency:

- Name the exact hand-rolled surface it replaces and any residual semantics.
- Check its health, licensing, runtime support, footprint, and packaging impact.
- Require net deletion after accounting for glue, tests, docs, and operational burden.

A wrapper that relocates the same complexity or weakens required semantics is not a simplification.

## Produce And Apply The Result

For each retained candidate, report:

- **Target and evidence:** the owner, consumers, governing contract, and relevant call sites.
- **Simplification:** exactly what to remove, fold, demote, or rehome, including companion artifacts.
- **Benefit and tradeoff:** the ownership or maintenance surface removed and the capability or compatibility lost.
- **Verification and confidence:** observable acceptance criteria, focused checks, and missing evidence.

Rank candidates by confidence and ownership reduction rather than raw deletion size. If none clears the bar, show representative rejections instead of inventing work.

Reserve inline `TODO`, `FIXME`, or `XXX` notes for small local cleanups. Durable architectural decisions belong in the owning design.

When implementation is authorized, make the smallest coherent ownership change and remove the old implementation and its companion registrations, mappings, configuration, tests, docs, localization, and generator inputs together.

Validate in proportion to risk. Focus on affected observable contracts, compatibility, lifecycle behavior, and the final diff. In the handoff, state what was simplified, why the evidence supported it, what was intentionally preserved, and which checks ran.
