import type { CSSProperties, ReactElement, ReactNode } from 'react'

/**
 * The layer stack the active-idle styles animate: travel (x), lift (y), body (squash), trail (jets).
 * ComposerMascot tags its own wrappers with these classes instead of rendering this stage.
 */
export function MascotIdleStage({ className, style, children }: {
  className?: string
  style?: CSSProperties
  children: ReactNode
}): ReactElement {
  return (
    <div className={className ? `composer-mascot-stage ${className}` : 'composer-mascot-stage'} style={style}>
      <div className="composer-mascot-travel">
        <div className="composer-mascot-lift">
          <div className="composer-mascot-body">
            <div className="composer-mascot-trail">{children}</div>
          </div>
        </div>
      </div>
    </div>
  )
}
