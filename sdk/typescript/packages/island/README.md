# @dotcraft/island

Show that a DotCraft Agent is working on a machine: a floating island that names who is there and
what it is doing, and a glow along the screen's edges. Requires React 19 and `@dotcraft/avatar`.

## Install

```shell
npm install @dotcraft/island @dotcraft/avatar
```

Import both stylesheets once in your application.

```tsx
import '@dotcraft/avatar/styles.css'
import '@dotcraft/island/styles.css'
```

## Show the island

```tsx
import { Island, IslandHead, IslandRobots } from '@dotcraft/island/react'

export function Working() {
  return (
    <Island theme="dark" aria-label="Reviewer is running npm test">
      <IslandHead open={false} onToggle={() => {}} disabled expandLabel="Show details" collapseLabel="Hide details">
        <IslandRobots robots={[{ id: 'reviewer', name: 'Reviewer', pose: 'working', lead: true }]} />
        <strong>Reviewer</strong>
      </IslandHead>
    </Island>
  )
}
```

Set `card` and render `IslandBody` to open the island into a card. `IslandChoices` asks the user a
question in that card.

## Show the edge glow

```tsx
import { EdgeGlow } from '@dotcraft/island/react'

<EdgeGlow origin={{ x: 960, y: 28 }} />
```

Render it over the whole screen and pass the island's centre in that surface's pixels.
