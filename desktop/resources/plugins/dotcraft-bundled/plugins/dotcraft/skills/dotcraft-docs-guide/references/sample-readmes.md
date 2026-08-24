# Sample READMEs

Use this reference for a README owned by a sample or example outside the rendered documentation
site. First decide whether it describes one runnable leaf sample or indexes several examples.

## Runnable leaf samples

Include only what the reader needs at the sample boundary:

- one short statement of the result or capability the sample demonstrates;
- the role of each project or component when that distinction is necessary to understand the sample;
- prerequisites that block the sample from running;
- the shortest supported build, run, or verification command;
- an optional live or provider-backed command when it proves something the deterministic path cannot;
- one link to the authoritative guide or reference for deeper behavior when one exists.

Prefer the repository's verification script when it already builds and exercises the sample. Do not
repeat lower-level commands unless a reader needs them for a distinct workflow.

## Hand details to their owner

Move complete API or contribution catalogs, configuration references, architecture, trust and
security models, lifecycle mechanics, compatibility policy, maintenance rationale, and exhaustive
troubleshooting to their owning documentation or specification. Preserve prerequisites and safety
warnings needed to run the sample without unsafe copy-and-paste, and link to the complete model.

Do not impose a fixed section count or line limit. Keep the README as small as the sample permits
while preserving an executable path and the distinctions a reader must understand.

## Aggregate example indexes

An examples-directory README may inventory several runnable examples. Keep each entry to a name and
one-line purpose, then provide package-level setup, validation, and any shared safety requirement
once. Link to leaf-specific guidance only when an example needs a materially different workflow.

## Repository conventions

- Use one H1, sentence-case headings, tagged code fences, and relative repository links.
- Match the sample's supported shell and platform instead of adding unused command variants.
- Do not create a localized mirror or a `Related docs` section unless nearby sample READMEs establish
  that convention.
- Do not run the VitePress build unless a rendered documentation-site page also changes.

Validate links and commands against the owning files. Run the smallest relevant sample build or
verification when practical, and do not invoke a credentialed or billable live path solely to check
README wording.
