# Workflow Pattern Selection

Choose a Workflow only when moving the orchestration into code adds value. Direct or tightly coupled work usually needs no Workflow. Bound every collection and loop before it can spawn Agents.

## Fan out, then synthesize

Use for independent inspections that require one whole-set judgment. Preserve every intended ID, including missing results, and give the synthesis Agent the complete ledger.

```js
phase("review", "Run independent reviews");
const work = [
  { id: "correctness", prompt: "Review correctness." },
  { id: "tests", prompt: "Review test coverage." }
];
const results = await parallel(work.map(item => async () => ({
  id: item.id,
  result: await agent(item.prompt, { label: item.id })
})));

phase("synthesize", "Reconcile all findings");
return agent({
  prompt: "Produce one ranked report. Identify any missing review coverage.",
  context: results
}, { label: "synthesis" });
```

## Pipeline per item

Use when each item follows the same ordered stages while different items can progress concurrently. Keep the stable ID in every stage value.

```js
return pipeline(
  files,
  (file, _original, index) => agent(`Audit ${file}`, { label: `audit-${index}` })
    .then(result => ({ id: file, result })),
  value => value.result === null
    ? value
    : agent({ prompt: `Verify the audit for ${value.id}.`, context: value.result }, {
        label: `verify-${value.id}`
      }).then(verification => ({ ...value, verification }))
);
```

## Adversarial verification

Finish production before verification. Use separate Agents and prompts for producer and skeptic roles. A verifier that cannot evaluate a claim leaves it unverified; it does not refute the claim. Report producer and verifier failures separately.

## Bounded iterative discovery

Use a loop only when cardinality is unknown. Deduplicate discoveries by stable key, cap rounds and total items, and stop on an explicit condition such as two successful dry rounds. A failed or `null` round is missing coverage and must not count as a dry round.

## Adaptation rules

- Let JavaScript own work IDs, ordering, filtering, deduplication, stopping, and the failure ledger.
- Let Agents own semantic reading, editing, command execution, research, judgment, and synthesis.
- Do not filter `null` before pairing it with the intended work ID.
- Combine patterns only when the task has both dependency shapes.
- Prefer several small independent calls over one unbounded fan-out or a long opaque call, while staying within the user's requested scale.
