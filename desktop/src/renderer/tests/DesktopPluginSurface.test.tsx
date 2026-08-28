import type {
  DesktopPluginHost,
  DesktopPluginSurfaceComponent,
  DesktopPluginSurfaceWrapper
} from '@dotcraft/plugin'
import { act, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { DesktopPluginSurface } from '../components/desktopPlugins/DesktopPluginSurface'
import {
  clearDesktopPluginRegistry,
  registerDesktopPluginSurface
} from '../plugins/desktopPluginRegistry'

declare module '@dotcraft/plugin' {
  interface DesktopPluginSurfaceContextMap {
    'test.surface': { readonly label: string }
  }
}

function host(pluginId: string): DesktopPluginHost {
  return {
    plugin: { id: pluginId, version: '1.0.0', displayName: `${pluginId} owner` }
  } as DesktopPluginHost
}

function component(text: string): DesktopPluginSurfaceComponent<'test.surface'> {
  return ({ host: pluginHost, context }) => (
    <span data-testid={text}>{text}:{pluginHost.plugin.id}:{context.label}</span>
  )
}

beforeEach(() => clearDesktopPluginRegistry())
afterEach(() => act(() => clearDesktopPluginRegistry()))

describe('DesktopPluginSurface', () => {
  it('keeps additions in registration order and restores previous replacements on dispose', () => {
    const disposeFirstAdd = registerDesktopPluginSurface(
      'add.first', host('add.first'), 'test.surface', 'add', component('first-add')
    )
    const disposeFirstReplace = registerDesktopPluginSurface(
      'replace.first', host('replace.first'), 'test.surface', 'replace', component('first-replace')
    )
    registerDesktopPluginSurface(
      'add.second', host('add.second'), 'test.surface', 'add', component('second-add')
    )
    const disposeLastReplace = registerDesktopPluginSurface(
      'replace.last', host('replace.last'), 'test.surface', 'replace', component('last-replace')
    )

    render(
      <DesktopPluginSurface name="test.surface" context={{ label: 'context' }}>
        <span data-testid="core">core</span>
      </DesktopPluginSurface>
    )

    expect(screen.getByTestId('last-replace')).toHaveTextContent('last-replace:replace.last:context')
    expect(screen.queryByTestId('first-replace')).toBeNull()
    expect(screen.queryByTestId('core')).toBeNull()
    expect(screen.getAllByTestId(/-add$/).map((entry) => entry.dataset.testid))
      .toEqual(['first-add', 'second-add'])
    act(disposeLastReplace)
    expect(screen.getByTestId('first-replace')).toBeInTheDocument()
    expect(screen.queryByTestId('last-replace')).toBeNull()

    act(disposeFirstReplace)
    expect(screen.getByTestId('core')).toBeInTheDocument()

    act(disposeFirstAdd)
    expect(screen.queryByTestId('first-add')).toBeNull()
    expect(screen.getByTestId('second-add')).toBeInTheDocument()
  })

  it('nests later wrappers outside earlier wrappers and restores the inner tree on dispose', () => {
    const inner: DesktopPluginSurfaceWrapper<'test.surface'> = ({ children, context }) => (
      <section data-testid="inner-wrapper" data-label={context.label}>{children}</section>
    )
    const outer: DesktopPluginSurfaceWrapper<'test.surface'> = ({ children, host: pluginHost }) => (
      <aside data-testid="outer-wrapper" data-plugin={pluginHost.plugin.id}>{children}</aside>
    )
    const disposeInner = registerDesktopPluginSurface(
      'wrap.inner', host('wrap.inner'), 'test.surface', 'wrap', inner
    )
    const disposeOuter = registerDesktopPluginSurface(
      'wrap.outer', host('wrap.outer'), 'test.surface', 'wrap', outer
    )

    render(
      <DesktopPluginSurface name="test.surface" context={{ label: 'context' }}>
        <span data-testid="core">core</span>
      </DesktopPluginSurface>
    )

    expect(screen.getByTestId('outer-wrapper')).toContainElement(screen.getByTestId('inner-wrapper'))
    expect(screen.getByTestId('inner-wrapper')).toContainElement(screen.getByTestId('core'))
    expect(screen.getByTestId('inner-wrapper')).toHaveAttribute('data-label', 'context')
    expect(screen.getByTestId('outer-wrapper')).toHaveAttribute('data-plugin', 'wrap.outer')

    act(disposeOuter)
    expect(screen.queryByTestId('outer-wrapper')).toBeNull()
    expect(screen.getByTestId('inner-wrapper')).toContainElement(screen.getByTestId('core'))

    act(disposeInner)
    expect(screen.queryByTestId('inner-wrapper')).toBeNull()
    expect(screen.getByTestId('core')).toBeInTheDocument()
  })

  it('isolates a failed addition without hiding the remaining surface', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    registerDesktopPluginSurface(
      'add.failed',
      host('add.failed'),
      'test.surface',
      'add',
      () => {
        throw new Error('surface failed')
      }
    )
    registerDesktopPluginSurface(
      'add.healthy', host('add.healthy'), 'test.surface', 'add', component('healthy-add')
    )

    render(
      <DesktopPluginSurface name="test.surface" context={{ label: 'context' }}>
        <span data-testid="core">core</span>
      </DesktopPluginSurface>
    )

    expect(screen.getByTestId('core')).toBeInTheDocument()
    expect(screen.getByTestId('healthy-add')).toBeInTheDocument()
    expect(consoleError).toHaveBeenCalled()
    consoleError.mockRestore()
  })

  it('falls back to Core content when a replacement fails', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    registerDesktopPluginSurface(
      'replace.failed',
      host('replace.failed'),
      'test.surface',
      'replace',
      () => {
        throw new Error('replacement failed')
      }
    )

    render(
      <DesktopPluginSurface name="test.surface" context={{ label: 'context' }}>
        <span data-testid="core">core</span>
      </DesktopPluginSurface>
    )

    expect(screen.getByTestId('core')).toBeInTheDocument()
    expect(consoleError).toHaveBeenCalled()
    consoleError.mockRestore()
  })
})
