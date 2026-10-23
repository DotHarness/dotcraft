import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, within } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ModuleConfigForm } from '../components/channels/ModuleConfigForm'
import type { DiscoveredModule } from '../../preload/api'
import { installDesktopApiMock } from './desktopApiMock'

const settingsGet = vi.fn()

function createModule(): DiscoveredModule {
  return {
    moduleId: 'example-standard',
    channelName: 'example',
    displayName: 'Example',
    packageName: '@example/channel',
    configFileName: 'example.json',
    supportedTransports: ['websocket'],
    requiresInteractiveSetup: false,
    variant: 'standard',
    source: 'bundled',
    absolutePath: 'X:\\fixtures\\modules\\channel-example',
    configGroups: [
      { id: 'configuration', displayLabel: 'Configuration' },
      { id: 'advanced', displayLabel: 'Advanced' },
      { id: 'empty', displayLabel: 'Empty' }
    ],
    configDescriptors: [
      {
        key: 'example.platform',
        displayLabel: 'Platform',
        description: 'Select a service environment.',
        required: false,
        dataKind: 'enum',
        masked: false,
        interactiveSetupOnly: false,
        group: 'configuration',
        defaultValue: 'primary',
        options: [
          { value: 'primary', displayLabel: 'Primary' },
          { value: 'secondary', displayLabel: 'Secondary' }
        ]
      },
      {
        key: 'example.reaction',
        displayLabel: 'Reaction',
        description: 'Reaction shown while processing.',
        required: false,
        dataKind: 'enum',
        masked: false,
        interactiveSetupOnly: false,
        group: 'advanced',
        defaultValue: 'GLANCE',
        allowCustomValue: true,
        options: [{ value: 'GLANCE', displayLabel: 'Glance', preview: '👀' }]
      }
    ]
  }
}

function renderForm(
  module = createModule(),
  config: Record<string, unknown> = {},
  onChange = vi.fn()
) {
  settingsGet.mockResolvedValue({ locale: 'en' })
  render(
    <LocaleProvider>
      <ModuleConfigForm
        module={module}
        config={config}
        onChange={onChange}
        onSave={vi.fn()}
        saving={false}
        persistedEnabled={false}
        onStart={vi.fn()}
        qrDataUrl={null}
        qrPhase="idle"
        moduleLogLines={[]}
        logsLoading={false}
        onLoadLogs={vi.fn()}
      />
    </LocaleProvider>
  )
  return onChange
}

describe('ModuleConfigForm descriptors', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    installDesktopApiMock({
      settings: { get: settingsGet },
      modules: { pickDirectory: vi.fn().mockResolvedValue(null) }
    })
  })

  it('renders non-empty groups and effective defaults without mutating config', async () => {
    const onChange = renderForm()

    const headings = await screen.findAllByRole('heading', { level: 2 })
    expect(headings.map((heading) => heading.textContent)).toEqual(['Configuration', 'Advanced'])
    expect(screen.getByRole('combobox', { name: 'Platform' })).toHaveTextContent('Primary')
    expect(screen.queryByText('Empty')).not.toBeInTheDocument()
    expect(onChange).not.toHaveBeenCalled()
  })

  it('writes a custom enum value only after the user edits it', async () => {
    const onChange = renderForm()

    fireEvent.click(await screen.findByRole('combobox', { name: 'Reaction' }))
    fireEvent.click(screen.getByRole('option', { name: 'Custom…' }))
    fireEvent.change(screen.getByPlaceholderText('Enter a value'), { target: { value: 'PARTY' } })

    expect(onChange).toHaveBeenLastCalledWith({ example: { reaction: 'PARTY' } })
  })

  it('opens the descriptor documentation route for the active locale', async () => {
    const openExternal = vi.fn()
    installDesktopApiMock({
      settings: { get: settingsGet },
      modules: { pickDirectory: vi.fn().mockResolvedValue(null) },
      shell: { openExternal }
    })
    const module = createModule()
    module.configDescriptors = [{
      key: 'example.scopes',
      displayLabel: 'Scopes',
      description: 'One scope per line.',
      required: false,
      dataKind: 'list',
      masked: false,
      interactiveSetupOnly: false,
      group: 'configuration',
      docsPath: { en: '/guide/channels/example#scopes' }
    }]

    renderForm(module)

    fireEvent.click(await screen.findByRole('button', { name: 'Learn more about Scopes' }))
    expect(openExternal).toHaveBeenCalledWith('https://www.dotcraft.net/guide/channels/example#scopes')
  })

  it('keeps legacy advanced fields visible', async () => {
    const module = createModule()
    module.configGroups = undefined
    module.configDescriptors = [{
      key: 'example.legacy',
      displayLabel: 'Legacy value',
      description: '',
      required: false,
      dataKind: 'string',
      masked: false,
      interactiveSetupOnly: false,
      advanced: true
    }]

    renderForm(module)

    expect(await screen.findByRole('heading', { name: 'Advanced' })).toBeInTheDocument()
    expect(screen.getByText('Legacy value')).toBeInTheDocument()
  })

  it('chooses list options by group, asks the channel what is available, and drops unknown saved values', async () => {
    const sendRequest = vi.fn().mockResolvedValue({ name: 'example', key: 'example.scopes', available: ['calendar:read'] })
    installDesktopApiMock({
      settings: { get: settingsGet },
      modules: { pickDirectory: vi.fn().mockResolvedValue(null) },
      appServer: { sendRequest }
    })
    const module = createModule()
    module.configDescriptors = [{
      key: 'example.scopes',
      displayLabel: 'Scopes',
      description: 'Scopes requested on authorization.',
      required: false,
      dataKind: 'list',
      masked: false,
      interactiveSetupOnly: false,
      group: 'configuration',
      options: [
        { value: 'calendar:read', displayLabel: 'calendar:read' },
        { value: 'docs:read', displayLabel: 'docs:read' }
      ],
      optionGroups: [
        { id: 'calendar', displayLabel: 'Calendar', values: ['calendar:read'] },
        { id: 'docs', displayLabel: 'Docs', values: ['docs:read'] }
      ]
    }]

    const onChange = renderForm(module, { example: { scopes: ['calendar:read', 'retired:scope'] } })

    fireEvent.click(await screen.findByRole('button', { name: 'Edit' }))
    expect(sendRequest).toHaveBeenCalledWith('externalChannel/configOptionAvailability', { name: 'example', key: 'example.scopes' })
    const dialog = await screen.findByRole('dialog', { name: 'Scopes' })
    expect(within(dialog).getByRole('checkbox', { name: 'Calendar' })).toBeChecked()
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'Docs' }))
    expect(await within(dialog).findByRole('status')).toBeInTheDocument()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Done' }))

    expect(onChange).toHaveBeenLastCalledWith({ example: { scopes: ['calendar:read', 'docs:read'] } })
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
})
