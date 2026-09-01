import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { PluginIcon } from '../components/plugins/PluginCatalogItem'
import type { PluginEntry, PluginInterface } from '../stores/pluginStore'

function plugin(interfaceMetadata: PluginInterface): PluginEntry {
  return {
    id: 'example-plugin',
    displayName: 'Example Plugin',
    enabled: false,
    installed: false,
    installable: true,
    removable: false,
    source: 'marketplace',
    rootPath: '',
    interface: interfaceMetadata,
    functions: [],
    skills: [],
    mcpServers: [],
    lspServers: [],
  }
}

function renderPluginIcon(interfaceMetadata: PluginInterface): HTMLElement {
  const { container } = render(<PluginIcon plugin={plugin(interfaceMetadata)} role="list" />)
  const mark = container.firstElementChild
  if (!(mark instanceof HTMLElement)) throw new Error('Expected an identity mark')
  return mark
}

describe('PluginIcon', () => {
  it('paints brandColor behind plugin artwork', () => {
    const mark = renderPluginIcon({
      displayName: 'Example Plugin',
      brandColor: '#123456',
      composerIconDataUrl: 'data:image/svg+xml,<svg/>',
    })

    expect(mark.querySelector('img')).toBeInTheDocument()
    expect(mark).toHaveStyle({ backgroundColor: '#123456' })
  })

  it('keeps the shell transparent when artwork has no brandColor', () => {
    const mark = renderPluginIcon({
      displayName: 'Example Plugin',
      composerIconDataUrl: 'data:image/svg+xml,<svg/>',
    })

    expect(mark.querySelector('img')).toBeInTheDocument()
    expect(mark.style.backgroundColor).toBe('')
  })

  it('uses brandColor behind the generated fallback', () => {
    const mark = renderPluginIcon({
      displayName: 'Example Plugin',
      brandColor: '#654321',
    })

    expect(mark.querySelector('img')).not.toBeInTheDocument()
    expect(mark).toHaveTextContent('E')
    expect(mark).toHaveStyle({ backgroundColor: '#654321' })
  })

  it('keeps the default fallback color when brandColor is absent', () => {
    const mark = renderPluginIcon({ displayName: 'Example Plugin' })

    expect(mark.querySelector('img')).not.toBeInTheDocument()
    expect(mark).toHaveTextContent('E')
    expect(mark.style.getPropertyValue('--identity-mark-fallback-background')).toBe('#0B63CE')
  })
})
