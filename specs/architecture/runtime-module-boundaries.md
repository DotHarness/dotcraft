# Runtime module boundaries

This specification defines stable rules for separating runtime responsibilities. It does not list
the repository's current modules.

## Dependency model

Runtime dependencies point toward shared foundations:

```text
Composition root -> Features -> Core -> Foundations
                 \-----------> Core
```

- Core owns the runtime kernel and feature-neutral shared contracts.
- A feature owns its behavior and depends on Core. Core never depends on a feature implementation.
- The composition root selects, configures, and connects features. It owns process policy, not
  feature behavior.
- Features do not depend on one another for optional collaboration. They contribute capabilities
  through narrow Core contracts wired by the composition root.
- Dependency cycles are not permitted. A project reference must not be added merely to make a move
  compile.

## Ownership and contracts

A responsibility has one owner. Its implementation, resources, configuration binding, and behavior
tests move together. Host wiring tests remain with the composition root.

Add a Core contract only when it belongs to the runtime kernel or supports multiple independent
components. Keep it capability-oriented, feature-neutral, and no broader than required. Do not
expose concrete services, orchestration details, or persistence internals to complete an extraction.

Do not use compatibility shims, type forwarding, or friend-assembly access as substitutes for a
clear dependency boundary.

## Test boundaries

Tests follow the production responsibility they verify. Kernel tests remain with Core, feature
behavior tests remain with the feature, and host wiring tests remain with the composition root.

Test projects must not reference other test projects. Substantial support shared by multiple owners
may live in a narrowly scoped test-support assembly, but it must not become a general utility layer.

An extraction must not broaden production internals for tests. Prefer observable behavior and
existing contracts. White-box tests that require an existing internal boundary remain there.

## Boundary change process

Apply boundary changes in this order:

1. Map the implementation, resources, consumers, tests, and references.
2. Define the intended owner and dependency direction before implementation.
3. Move the responsibility, resources, and tests as one coherent change.
4. Wire it through the composition root with the smallest required contract.
5. Remove old files and references without compatibility or tombstone artifacts.
6. Validate the owner, affected consumers, dependency graph, and full solution.

A boundary change is complete when ownership is unambiguous, dependencies remain one-way, existing
observable behavior is preserved, and the former owner contains no residual implementation.
