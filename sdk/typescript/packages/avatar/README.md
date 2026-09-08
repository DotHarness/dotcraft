# @dotcraft/avatar

Render a DotCraft companion from an Agent name. The same name produces the same colors,
expression and decorations across applications. React components require React 19; the core
entry works without React.

## Install

After the package's first npm release:

```shell
npm install @dotcraft/avatar
```

## Render an avatar

Import the stylesheet once in your application.

```tsx
import { Avatar } from '@dotcraft/avatar/react'
import '@dotcraft/avatar/styles.css'

export function AgentIdentity() {
  return <Avatar name="Reviewer" size={36} label="Reviewer" />
}
```

Avatars are static by default. Set `state="working"` and `motion="system"` to animate while
respecting reduced-motion preferences. Use `paused` to freeze playback and increment
`eventSequence` to replay an event. Provide a localized `label`, or omit it for decorative artwork.
See the [component props](./src/Avatar.tsx) for all options.

`ComposerMascot` accepts `theme="light"` or `theme="dark"` for host-aware effects. It defaults
to `dark`; pass the host's applied theme when the surrounding application supports both variants.

## Derive an appearance

```ts
import { deriveAppearance } from '@dotcraft/avatar'

const appearance = deriveAppearance('Reviewer')
```

Names are trimmed and normalized to Unicode NFC; case and internal spaces remain significant.
An empty name shows the original undecorated mascot. `AppearanceAvatar` from `/react` renders
an explicit appearance for design tools. See the [appearance types and compatibility rules](./src/appearanceModel.ts).
