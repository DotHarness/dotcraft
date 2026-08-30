import type { DesktopPluginSurfaceProps } from '@dotcraft/plugin'
import type { JSX } from 'react'
import { MascotCharacter } from './MascotCharacter'
import { choiceValue } from './appearance'
import { stringsFor } from './i18n'
import { characterStateFor } from './mascotState'
import { useAppearance, useOverWallpaper } from './useAppearance'

export function ComposerMascot({
  host,
  context
}: DesktopPluginSurfaceProps<'composer.mascot'>): JSX.Element | null {
  const appearance = useAppearance()
  const overWallpaper = useOverWallpaper()
  const strings = stringsFor(host.environment.locale)
  const state = characterStateFor(context)
  const sourceId = context.workspacePath ?? 'dotcraft'

  if (!appearance) return null

  return (
    <div
      className="grok"
      data-state={state}
      data-reduced-motion={context.reducedMotion ? 'true' : 'false'}
      data-over-wallpaper={overWallpaper ? 'true' : 'false'}
      role="img"
      aria-label={strings.mascotLabel}
    >
      <MascotCharacter
        className="grok-character"
        sizePx={context.size}
        state={state}
        color={choiceValue(appearance.color)}
        shape={choiceValue(appearance.shape)}
        sourceId={sourceId}
        followPointer
        reducedMotion={context.reducedMotion}
      />
      {context.submitRevision > 0 && (
        <span key={context.submitRevision} className="grok-burst" aria-hidden="true" />
      )}
    </div>
  )
}
