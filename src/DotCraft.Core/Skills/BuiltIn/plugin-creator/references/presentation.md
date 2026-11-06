# Plugin presentation

`dotcraft plugin verify` enforces the limits below. It cannot judge the copy rules, so apply those yourself.

## Fields

| Field | Rule |
|---|---|
| `displayName`, `interface.displayName` | A recognizable name of at most 30 characters |
| `description` | One sentence of at most 160 characters |
| `interface.shortDescription` | One line of at most 48 characters |
| `interface.developerName` | The real author or team. Use DotCraft or DotHarness only for plugins they author |
| `interface.category` | Exactly one of `Coding`, `Design`, `Engineering`, `Security`, `Lifestyle`, `Productivity`, or `Research` |
| `capabilities`, `interface.capabilities` | The contributions the plugin actually ships |
| `interface.defaultPrompt` | One line of at most 128 characters |
| `interface.logo`, `interface.composerIcon` | Both are required and may name the same file |
| `interface.longDescription` | Optional, at most 300 characters. Add it only for behavior or audience information the short description cannot hold |

## Copy

State what the plugin does, and nothing else. Write one plain statement of the capability. Hypotheticals, advice, setup instructions, implementation detail, and marketing language belong elsewhere, because this text appears in a catalog people scan. Do not open with "This plugin" or restate the display name.

Make `defaultPrompt` a realistic first request that shows the plugin at work.

Use the user's language for a private plugin and English for the official registry.

| Field | Avoid | Prefer |
|---|---|---|
| `shortDescription` | `A plugin that may help if you need to review changes` | `Review pull requests against team rules` |
| `description` | `This plugin provides tools. Configure a token first.` | `Reviews pull requests against your team's written rules.` |
| `defaultPrompt` | `Use Review Tools.` | `Review this branch against our review rules.` |

## Icons

Reuse branding the user supplies. Otherwise draw one original icon that relates to what the plugin does. Use a third party's mark only with permission. A generic placeholder is not an icon.

- Save it in `assets/` as SVG where possible. PNG, JPEG, and WebP also work, at most 512 KiB per file.
- Make it square. Raster files are at least 256 × 256 for `logo` and 48 × 48 for `composerIcon`.
- Start from one simple silhouette with few colors that stays legible at small size on light and dark backgrounds. Include a background when contrast needs one.
- Keep SVG self-contained: geometry only, with no scripts, event handlers, external references, embedded HTML, or font text.

When you cannot view the rendered icon, report that it was checked by file only.
