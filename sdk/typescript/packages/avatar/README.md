# @dotcraft/avatar

Render a consistent DotCraft companion from an Agent name. React components require React 19;
the package root works without React.

## Install

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

Import `deriveAppearance` from `@dotcraft/avatar` when an application needs the same
name-derived appearance without React.

## Follow the pointer

`useAvatarGaze` turns the face of each avatar inside an element toward the pointer. Enable it
only while the avatar is idle. Pass `within` to follow only inside an element, and `reach` to set
how far from the avatar's centre, in CSS pixels, the gaze reaches its limit.

```tsx
const face = useRef<HTMLSpanElement>(null)
useAvatarGaze(face, { enabled: state === 'idle' })

return <span ref={face}><Avatar name="Reviewer" size={60} state={state} motion="system" /></span>
```

## Collection

An appearance fills five independent slots: `head`, `face`, `hand`, `back`, and `skin`. Every
item in the [registry](./src/items.ts) carries a rarity (`common` to `legendary`), a series, and
the zones it occupies; items that share a zone never appear together. Name derivation draws each
slot's rarity with shared weights, so legendary items are rare but reachable. Use
`equip(appearance, slot, id)` to change one slot and clear conflicting ones, and the
[catalog](./src/decorationCatalog.ts) for display names, rarity chrome colors, and series labels.

Effects belong to items: paint skins such as `chrome` and `holographic` replace the body paint,
glow items pulse, and koi circle the body. Effects animate only at 44px and above with motion
enabled; smaller avatars render their static frame, and avatars at 20px and below drop face, hand,
overlay-skin, and effect layers entirely. The model is specified in
[specs/features/avatar-system.md](../../../../specs/features/avatar-system.md).

See the [TypeScript SDK reference](https://www.dotcraft.net/extend/sdks/typescript) for the
available entry points.
