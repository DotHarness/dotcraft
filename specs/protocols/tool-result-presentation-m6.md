# Interactive Tool UI — M‑vi: Capability Negotiation, Security & Acceptance

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Planned |
| **Date** | 2026-06-09 |
| **Parent Spec** | [Interactive Tool UI](tool-result-presentation.md) |
| **Milestone** | M‑vi — Capability negotiation, security & acceptance hardening |
| **Depends on** | M‑iii, M‑iv, M‑v |

## 1. Overview

The hardening and cross‑client acceptance gate. After this milestone, Interactive Tool UI is production‑ready: negotiated, secure, audited, conformant across clients, and documented. Protocol references: parent [§3](tool-result-presentation.md) (capability negotiation), [§10](tool-result-presentation.md) (authorization), [§11](tool-result-presentation.md) (security), [§12](tool-result-presentation.md) (fallback), [§16](tool-result-presentation.md) (acceptance).

## 2. Goal

Only capable clients receive interactive UI; the sandbox/CSP/permission boundaries are reviewed and sound; every UI‑initiated action is audited; the acceptance checklist is green on Desktop and on text‑fallback clients; users and app authors have bilingual docs.

## 3. Scope

- **Capability negotiation**: `interactiveToolUi` is negotiated at `initialize` (parent [§3](tool-result-presentation.md)). The `ui` descriptor and the host methods (`ui/resource/read`, `ui/tool/call`) are only offered to / honored for clients that negotiated it; others receive text fallback only.
- **Security review**: iframe sandbox attributes; per‑resource CSP correctness; `ui/open-link` scheme allow‑list; the `_meta.ui.permissions` model; opaque‑origin isolation (no Node / no host DOM reach); threat review of every UI→host action.
- **Audit completeness**: every UI‑initiated tool call and link open is on the App Binding audit trail with provenance.
- **Acceptance + conformance**: parent [§16](tool-result-presentation.md) checklist green on Desktop and on at least one text‑fallback client; a conformance test suite codifies it.
- **Docs**: bilingual (EN + 中文) user and app‑author documentation (per the dev guide), covering authoring an interactive UI, the bridge, and security expectations.

## 4. Non‑goals

- New feature surface (all behavior is delivered by M‑iii–v).

## 5. Behavioral contract

- A client that does not negotiate `interactiveToolUi` never receives `ui` descriptors, cannot call `ui/*`, and always gets the text fallback.
- The sandbox + per‑resource CSP + scheme allow‑list provably bound what an app UI can reach (network, navigation, host); the app cannot reach Node, the host DOM, or another app's surface.
- Every UI‑initiated action is attributable on the audit trail.

## 6. Required workflow / lifecycle

Negotiation at `initialize` gates all interactive‑UI delivery and host methods. Security boundaries are enforced per parent [§11](tool-result-presentation.md). Audit entries are written for all UI‑initiated actions (extends M‑iii's `ui/tool/call` auditing to link opens and any other host‑mediated effects).

## 7. Constraints & compatibility

- Negotiation must be backward‑compatible: pre‑interactive clients keep working via text fallback with no behavior change.
- Security changes must not regress the M‑iii–v behavior contracts.
- Docs location must fit the existing `docs/` site structure (confirm placement before authoring).

## 8. Acceptance checklist

- [ ] Clients that don't negotiate `interactiveToolUi` never receive `ui` descriptors and can't invoke `ui/*`; they get text.
- [ ] Security review signed off: sandbox, per‑resource CSP, scheme allow‑list, permissions, opaque‑origin isolation.
- [ ] Audit trail covers all UI‑initiated tool calls and link opens, with provenance.
- [ ] Parent §16 acceptance checklist green on Desktop + a text‑fallback client; conformance suite passes.
- [ ] Bilingual user + app‑author docs published in `docs/`.

## 9. Open questions

- Permissions model depth: which `_meta.ui.permissions` are enforced vs declarative in v1.
- Whether an independent (second‑party) security review is required before GA.
- Capability‑negotiation granularity: a single `interactiveToolUi` flag, or sub‑capabilities (e.g. per UI→host action)?
