# DotCraft Skill 2.0 Design Specification

| Field | Value |
|-------|-------|
| **Version** | 0.2.0 |
| **Status** | Draft |
| **Date** | 2026-05-01 |
| **Parent Specs** | [Session Core](../core/session-core.md), [AppServer Protocol](../protocols/appserver-protocol.md) |

Purpose: Define **Skill 2.0**, an experimental extension of DotCraft's self-learning skill system. Skill 2.0 makes skills safer to import and evolve, easier to adapt to the current workspace and runtime environment, and useful as long-lived procedural memory without exposing internal compiler or VM concepts to ordinary users.

This is the single canonical Skill 2.0 design spec. Temporary implementation planning notes may exist while work is in progress, but they are not part of the durable design.

## 1. Scope

Skill 2.0 extends the current `SKILL.md` model without breaking existing skills.

In scope:

- Keep source skills compatible with the existing `SKILL.md` bundle model.
- Add safe skill variants so DotCraft can learn from use without mutating source skills directly.
- Add an effective skill view so agents and clients read the source-or-variant skill DotCraft actually intends to use.
- Preserve manual skill installation while adding a DotCraft-assisted install path for downloaded or marketplace skills.
- Keep the normal user experience lightweight: most benefits should appear as better behavior, not new required setup.

Deferred:

- GraSP-style multi-skill planning and graph execution.
- Full Skill Run Records and evidence-driven ranking.
- User-facing variant version control, manual accept/reject flows, or source-promotion workflows.
- Automatically applying learned variant edits back into source skills.

Out of scope:

- Replacing the normal ReAct/tool loop.
- Requiring all skills to declare formal schemas before they can run.
- Exposing VM, compiler, variant, fingerprint, or run-record internals to casual users.

## 2. Research Basis

Skill 2.0 uses two complementary research directions.

| Paper | DotCraft Interpretation |
|-------|-------------------------|
| [SkVM: Revisiting Language VM for Skills across Heterogenous LLMs and Harnesses](https://arxiv.org/abs/2604.03088), Le Chen, Erhu Feng, Yubin Xia, Haibo Chen, arXiv:2604.03088, 2026. | Skills should be treated like portable code executed by heterogeneous model/harness pairs. DotCraft adopts SkVM as the basis for source/variant isolation, environment-specific adaptations, effective skill resolution, and safe self-learning. |
| [GraSP: Graph-Structured Skill Compositions for LLM Agents](https://arxiv.org/abs/2604.17870), Tianle Xia, Lingxiang Hu, Yiding Sun, Ming Xu, Lan Xu, Siying Wang, Wei Xu, Jie Jiang, arXiv:2604.17870, 2026. | Skill libraries create an orchestration problem. DotCraft may later adopt GraSP ideas for selecting, ordering, verifying, and locally repairing multi-skill plans. This spec does not define that layer yet. |

SkVM is the primary current design reference. DotCraft uses it as a storage and adaptation model: source skills are stable artifacts, variants are workspace/runtime-specific derivatives, and the resolver decides which effective skill the agent should see.

GraSP remains a future planning reference. It should not be required for ordinary interactive tasks until the Skill 2.0 source/variant foundation is stable.

## 3. Core Concepts

| Concept | Definition |
|---------|------------|
| **Source Skill** | The canonical installed skill bundle under workspace, user, or built-in skill roots. Source skills remain compatible with today's loader. |
| **Source Fingerprint** | A stable hash of the source skill bundle content and selected metadata. It binds a variant to the exact source it was derived from. |
| **Target Signature** | The runtime tuple a variant is optimized for: DotCraft harness version, model id, available tools, OS, shell, sandbox posture, approval policy, workspace identity, and relevant config flags. |
| **Skill Variant** | A generated full skill directory snapshot derived from one source skill and one target signature. It can adapt wording, tool assumptions, verification steps, OS notes, workspace conventions, or known failure fixes. |
| **Effective Skill** | The skill bundle DotCraft actually presents to the agent after resolving source, compatible variant, staleness, restore state, and configuration. |
| **Current Variant** | The variant currently selected by the resolver for a source skill and target signature. There is at most one current variant per source skill per target signature. |
| **Restore Original Skill** | The single normal user-facing recovery action. It removes or disables the current adaptation so DotCraft falls back to the source skill. |
| **Skill Run Record** | A future compact evidence record about skill usage. Run records are deferred until DotCraft has evidence-driven ranking, JIT optimization, or diagnostics consumers. |

## 4. User Experience

Skill 2.0 should feel like DotCraft gradually becomes better at repeated work while keeping source skills stable and recoverable.

Default experience:

- DotCraft uses source or adapted skills naturally.
- Self-learning edits target workspace adaptations instead of source skills.
- Users do not need to understand variants, VMs, fingerprints, compilation, or run records.
- If a learned adaptation causes trouble, the user has one recovery action: restore the original skill.
- Installation says the skill is installed and ready, plus any concrete user action required.

Visible user-facing surfaces:

- Skills UI may show whether a skill has a workspace adaptation.
- Skill detail menus may expose **Restore Original Skill** when an adaptation exists.
- Skill Market may offer a DotCraft-assisted install/check/update action alongside direct install.
- Chat summaries may say DotCraft updated a workspace adaptation only when that is useful.

Not exposed by default:

- Variant ids, target signatures, source fingerprints, resolver policies, and run-record internals.
- Manual accept/reject/promote flows for variants.
- Variant selection knobs in normal personalization settings.

Product highlights:

- "Skills that learn safely from real usage."
- "Workspace-specific adaptations without damaging source skills."
- "Downloaded skills can be installed and checked by DotCraft."
- "A simple restore path when a learned adaptation is not helpful."

## 5. Non-Negotiable Invariants

- Existing `SKILL.md` files remain valid.
- Source skills are not mutated by self-learning.
- Variants are stored outside the source skill directory.
- A variant records the source skill identity and source fingerprint it was derived from.
- A variant becomes stale when its source fingerprint no longer matches the current source.
- A stale variant is not auto-selected unless a future compatibility policy explicitly allows it.
- Restore original skill must be possible without reading, reviewing, or applying a diff.
- Variant support degrades to existing source-skill behavior when disabled.
- AppServer clients are not required to understand variants for ordinary skill usage.
- Agents should use a skill-aware view/read method for skill bodies when variant mode is enabled.
- Raw file reads remain allowed for diagnostics, but they are not the authoritative skill abstraction.

## 6. Storage Model

DotCraft keeps source skills and generated variants in separate roots under the workspace directory.

```text
.craft/
  skills/
    {skill-name}/
      SKILL.md
      ...
  skill-variants/
    {source-scope}.{skill-name}/
      index.json
      {variant-id}/
        manifest.json
        skill/
          SKILL.md
          ...
```

Rules:

- `.craft/skills/` remains the workspace source skill root.
- `.craft/skill-variants/` is never scanned as a normal skill source.
- Each variant stores a full usable skill bundle under `skill/`.
- `manifest.json` is authoritative for a variant.
- `index.json` is an acceleration cache.
- User-global source skills may receive workspace-local variants.
- The initial design prefers workspace-local variants because target signatures include workspace conventions.

The variant manifest should record:

- `variantId`
- source identity and source fingerprint
- target signature or target signature hash
- status such as `current`, `stale`, `restored`, or `superseded`
- created and updated timestamps
- parent variant id, if any
- compact provenance such as originating thread/turn when available
- short summary of the adaptation

## 7. Fingerprints and Target Signatures

The source fingerprint should cover enough of the source bundle to detect meaningful source changes:

- `SKILL.md`
- ordinary supporting files in the skill bundle
- selected skill metadata
- install marker metadata when relevant

The fingerprint should not include generated variant files, temporary staging directories, or volatile access times.

The target signature binds a variant to the environment it was adapted for. It may include:

- DotCraft version or harness profile
- model family or model id
- available tool profile
- operating system and shell
- workspace identity
- relevant approval, sandbox, and skill configuration

The resolver may later support relaxed matching for low-risk fields, but exact matching is the conservative default.

## 8. Resolver Behavior

The variant-aware resolver expands current skill resolution:

1. Resolve the source skill by existing source precedence.
2. Compute or load the source fingerprint.
3. Build the current target signature.
4. Find compatible variants for `(source, sourceFingerprint, targetSignature)`.
5. Exclude variants with `restored`, `stale`, or incompatible status.
6. Pick the current variant if one exists.
7. Fall back to the source skill when no compatible current variant exists.

When a variant is selected:

- Full context loading uses the variant body.
- The agent normally sees only the effective skill instructions.
- Debug metadata may record whether the effective body came from source or variant.

When the source fingerprint changes:

- Existing variants for the old fingerprint become stale.
- Stale variants may be used as evidence for a future adaptation.
- Stale variants are not directly injected.

## 9. Effective Skill View

Variant mode means a raw file path is no longer the complete skill abstraction. `ReadFile` can still read a physical source or variant file, but agents and clients need a skill-aware view method that resolves the effective skill first.

`SkillView` is intentionally small. It is a loader, not a diagnostics API:

- Input: skill name.
- Output: effective `SKILL.md` content.
- It resolves source and current compatible variant internally.
- It may remove frontmatter from the returned content when that makes the agent-facing instructions clearer.
- It does not return variant id, source path, effective path, fingerprint, origin, or supporting-file listings.
- It does not expose variant terminology to the agent.

Supporting files are not automatically expanded through `SkillView`. If a skill body references another file, the agent may read that file explicitly as part of ordinary task execution or diagnostics.

Guidance:

- Skill summaries should instruct agents to use `SkillView` for loading skills when the tool is available.
- Fallback to `ReadFile` is acceptable only when `SkillView` is unavailable or when debugging a specific physical file.
- Diagnostics that need paths, fingerprints, variant ids, or run records should use separate developer-oriented methods.

## 10. SkillManage Semantics

`SkillManage` remains the agent-facing entry point for skill self-learning, but variant mode changes the default write target.

When variant mode is enabled:

- Creating a new skill still creates a source skill.
- Editing an existing source skill for self-learning routes to a variant.
- `edit`, `patch`, `write_file`, and `remove_file` operate on the current variant for the named source skill.
- If no current mutable variant exists, the first mutation forks the current source skill into a new variant and applies the mutation there.
- Skill source directories should be protected from ordinary file-edit tools so source safety does not rely only on prompt compliance.

When variant mode is disabled:

- `SkillManage` uses the existing source-oriented behavior.
- `SkillView` may still return the source body as a compatibility effective view.

The agent-facing result should avoid internal ids and source fingerprints. Developer diagnostics may expose those through separate APIs.

## 11. Restore Original Skill

Restore original skill is the only normal user-facing variant management action.

Restore behavior:

- Mark the current variant as `restored` or remove it from current selection.
- Preserve enough lightweight state so the resolver does not immediately reselect the same adaptation without new evidence.
- Large variant snapshots may be garbage-collected later.
- Refresh skill descriptors after restore.
- Future self-learning may create a new variant only after new evidence appears.

Source promotion is intentionally absent from the core UX. If a future advanced maintenance command copies variant content back to source, it should be treated as a source edit/export feature, not as part of normal self-learning.

## 12. Skill Installation and Import

Manual installation remains valid: users may place a skill folder under `.craft/skills/{name}/` or the user skill root. DotCraft should detect it as a source skill exactly as today.

DotCraft should also provide a built-in skill for DotCraft-assisted installation:

1. Download or stage a candidate skill bundle outside the source skill root.
2. Verify the candidate bundle.
3. Install the verified candidate directory into the workspace source skill root.
4. Read the installed effective skill through `SkillView`.
5. Review the skill against the current workspace and runtime.
6. Write a variant only when DotCraft discovers a concrete environment or workflow conflict.
7. Summarize installation status, environment issues, adaptations, and user actions.

The installer is an agent workflow skill, not a dedicated high-level install agent tool. It should use ordinary file and shell tools plus `dotcraft skill verify` and `dotcraft skill install`.

Installer constraints:

- Install from the candidate directory rather than rewriting the candidate skill.
- Do not use `WriteFile` to regenerate `SKILL.md` merely to fit DotCraft formatting.
- Use CLI parameters such as `--name <local-name>` for local naming mismatches.
- Preserve ordinary relative files such as root markdown/json files, `docs/*`, `references/*`, `scripts/*`, `assets/*`, and `agents/openai.yaml`.
- Keep safety checks: no path traversal, no escaped files, no hidden control files or directories except DotCraft install markers, and file count/size limits.

The CLI `--name <local-name>` value is the canonical local install name when present. The frontmatter `name` must exist, but it does not have to exactly match the local install name. This allows marketplace slugs such as `git` to install skills whose display/frontmatter name is `Git`.

Desktop-assisted installs should start a concise DotCraft conversation with only:

- skill display name
- candidate directory
- local install name
- source
- workspace
- expected final summary fields

The prompt should rely on the built-in installer workflow, explicitly ask DotCraft to install from the candidate directory, and avoid exposing internal variant terminology to the user.

## 13. Run Records

Full Skill Run Records are deferred.

Run records are useful when DotCraft needs evidence-driven behavior such as:

- JIT skill optimization after repeated failures or repairs.
- Ranking source skills and variants by historical success.
- Developer diagnostics for recurring environment conflicts.
- Automatic maintenance policies for stale or low-confidence adaptations.
- Cross-session skill memory beyond compact variant provenance.

They are not required for the current install and variant safety loop. Current implementations may rely on thread records, trace storage, and variant manifest provenance for debugging.

When implemented later, run records should be compact, bounded, and privacy-conscious. Large trace payloads should stay in trace storage and be referenced by id. Records should store summaries, hashes, counters, and trace ids rather than raw full prompts or secrets.

## 14. Configuration

Skill 2.0 should remain conservative by default.

Relevant settings:

```json
{
  "Skills": {
    "SelfLearning": {
      "Enabled": true,
      "VariantMode": "enabled"
    }
  }
}
```

Allowed `VariantMode` values:

- `disabled`: self-learning writes use existing source-oriented behavior.
- `enabled`: self-learning writes route through variants and effective skill resolution.
- `passive`: reserved for a future run-record-only evidence layer.

Initial implementation supports `enabled` and `disabled`. `passive` is reserved until run records have consumers.

Desktop should not expose `VariantMode` as a normal personalization setting while variants remain an internal safety layer behind skill self-learning. User-facing controls should describe product behavior, not VM mechanics.

## 15. AppServer and Client Surface

No immediate protocol break is required. Public surfaces should be guarded by capability flags.

Candidate methods:

| Method | Purpose |
|--------|---------|
| `skills/view` | Read the effective skill content after source/variant resolution. |
| `skills/restoreOriginal` | Restore the source skill by removing or disabling the current adaptation. |

Deferred methods:

| Method | Purpose |
|--------|---------|
| `skills/runs/list` | List compact run records for diagnostics after the evidence layer exists. |

`capabilities.skillVariants` means the current runtime has variant mode enabled, not merely that the server binary knows about `skills/view` or `skills/restoreOriginal`. Clients should use it to decide whether to show variant-dependent user actions such as restoring the original skill.

`skills/read` remains source-oriented for compatibility. Clients and agents should migrate to `skills/view` / `SkillView` when they need the instructions DotCraft will actually use.

## 16. Security and Trust

Variant isolation improves safety but does not remove normal tool risk.

- Downloaded or marketplace skills must be verified before they become source skills.
- Variants inherit source skill file path constraints.
- Variant-generated scripts are just skill supporting files until invoked through normal tools.
- Source skill directories should be protected from accidental self-learning edits.
- Imported skills and their variants should preserve provenance when practical.
- Variant manifests should avoid storing secrets, full raw prompts, or unnecessary file contents.

## 17. Future Work

- GraSP-style advisory skill planning.
- Full Skill Run Records and aggregated activity metadata.
- Evidence-driven variant optimization and ranking.
- Advanced developer diagnostics for effective paths, source paths, fingerprints, recent runs, and staleness.
- Explicit source export/promotion workflows for advanced maintainers.

## 18. Assumptions

- Skill 2.0 is experimental and should ship behind conservative defaults.
- Source/variant safety is more important than immediate automation.
- SkVM improves individual skill reliability; GraSP may later improve multi-skill orchestration.
- DotCraft should prioritize user trust: source skills are stable, learned changes are recoverable, and fallback behavior is always available.
