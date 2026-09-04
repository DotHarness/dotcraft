import './setupPluginRuntime'
import type { DesktopPluginHost, DesktopPluginSurfaceComponent } from '@dotcraft/plugin'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  clearDesktopPluginRegistry,
  registerDesktopPluginSurface,
  useDesktopPluginRegistry
} from '../plugins/desktopPluginRegistry'

const host = { plugin: { id: 'hud', version: '1.0.0', displayName: 'HUD' } } as DesktopPluginHost
const component: DesktopPluginSurfaceComponent<string> = () => null

let warn: ReturnType<typeof vi.spyOn>

beforeEach(() => {
  clearDesktopPluginRegistry()
  warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
})

afterEach(() => {
  warn.mockRestore()
  clearDesktopPluginRegistry()
})

describe('Core surface names', () => {
  it('warns about a misspelled Core surface and still keeps the registration', () => {
    registerDesktopPluginSurface('hud', host, 'composer.toolbar.trailng', 'add', component)

    expect(warn).toHaveBeenCalledTimes(1)
    expect(warn.mock.calls[0][0]).toContain('composer.toolbar.trailng')

    const surfaces = useDesktopPluginRegistry.getState().surfaces
    expect(surfaces).toHaveLength(1)
    expect(surfaces[0].surface).toBe('composer.toolbar.trailng')
  })

  it('warns for a bare Core root Core does not define', () => {
    registerDesktopPluginSurface('hud', host, 'app.sidebar', 'replace', component)
    expect(warn).toHaveBeenCalledTimes(1)
  })

  it('stays quiet for a plugin-declared surface, which may legally be unmounted', () => {
    registerDesktopPluginSurface('hud', host, 'hud.readout', 'add', component)
    registerDesktopPluginSurface('hud', host, 'oratorio.board.card', 'add', component)
    registerDesktopPluginSurface('hud', host, 'appearance.preview', 'add', component)

    expect(warn).not.toHaveBeenCalled()
    expect(useDesktopPluginRegistry.getState().surfaces).toHaveLength(3)
  })
})
