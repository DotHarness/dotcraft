import type {
  DesktopPluginSurfaceComponent,
  DesktopPluginSurfaceContext,
  DesktopPluginSurfaceWrapper
} from '@dotcraft/plugin'
import { Fragment, type ReactNode } from 'react'

import {
  useDesktopPluginRegistry,
  type ActiveDesktopPluginSurface
} from '../../plugins/desktopPluginRegistry'
import { DesktopPluginContributionBoundary } from './DesktopPluginContributionBoundary'

export interface DesktopPluginSurfaceProps<S extends string> {
  name: S
  context: DesktopPluginSurfaceContext<S>
  children?: ReactNode
}

export function DesktopPluginSurface<S extends string>({
  name,
  context,
  children
}: DesktopPluginSurfaceProps<S>): JSX.Element {
  const registeredSurfaces = useDesktopPluginRegistry((state) => state.surfaces)
  const surfaces = registeredSurfaces.filter((registration) => registration.surface === name)

  const replacements = surfaces.filter((registration) => registration.kind === 'replace')
  const replacement = replacements.at(-1)
  let content = replacement
    ? renderComponent(replacement, context, children)
    : children

  const additions = surfaces.filter((registration) => registration.kind === 'add')
  if (additions.length > 0) {
    content = (
      <Fragment>
        {content}
        {additions.map((registration) => renderComponent(registration, context, null))}
      </Fragment>
    )
  }

  for (const registration of surfaces) {
    if (registration.kind !== 'wrap') continue
    const innerContent = content
    const Wrapper = registration.component as DesktopPluginSurfaceWrapper<S>
    content = (
      <DesktopPluginContributionBoundary
        key={registration.registrationId}
        identity={surfaceIdentity(registration)}
        fallback={innerContent ?? null}
      >
        <Wrapper host={registration.host} context={context}>
          {innerContent}
        </Wrapper>
      </DesktopPluginContributionBoundary>
    )
  }

  return (
    <div data-dotcraft-plugin-surface={name} style={{ display: 'contents' }}>
      {content}
    </div>
  )
}

function renderComponent<S extends string>(
  registration: ActiveDesktopPluginSurface,
  context: DesktopPluginSurfaceContext<S>,
  fallback: ReactNode
): ReactNode {
  const Component = registration.component as DesktopPluginSurfaceComponent<S>
  return (
    <DesktopPluginContributionBoundary
      key={registration.registrationId}
      identity={surfaceIdentity(registration)}
      fallback={fallback ?? null}
    >
      <Component host={registration.host} context={context} />
    </DesktopPluginContributionBoundary>
  )
}

function surfaceIdentity(registration: ActiveDesktopPluginSurface): string {
  return `${registration.pluginId}/${registration.surface}/${registration.kind}/${registration.registrationId}`
}
