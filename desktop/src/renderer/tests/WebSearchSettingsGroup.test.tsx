import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { WebSearchSettingsGroup } from '../components/settings/panels/webSearch/WebSearchSettingsGroup'

const config = vi.hoisted(() => ({
  values: {} as Record<string, unknown>,
  write: vi.fn()
}))
vi.mock('../stores/configStore', () => ({
  useConfigSetting: (path: string) => ({
    value: config.values[path],
    pending: false,
    set: (value: unknown) => config.write(path, value)
  })
}))
vi.mock('../contexts/LocaleContext', () => ({
  useT: () => (key: string) => key
}))
vi.mock('../components/settings/ui/SettingsSelect', () => ({
  SettingsSelect: ({
    id,
    value,
    options,
    onValueChange,
    ariaLabel,
    disabled
  }: {
    id: string
    value: string
    options: Array<{ value: string; label: string; disabled?: boolean }>
    onValueChange: (value: string) => void
    ariaLabel: string
    disabled: boolean
  }) => (
    <select
      id={id}
      aria-label={ariaLabel}
      value={value}
      disabled={disabled}
      onChange={(event) => onValueChange(event.target.value)}
    >
      {options.map((option) => (
        <option
          key={option.value}
          value={option.value}
          disabled={option.disabled}
        >
          {option.label}
        </option>
      ))}
    </select>
  )
}))

beforeEach(() => {
  config.values = { 'Tools.Web.SearchProvider': 'Auto' }
  config.write.mockReset()
})
afterEach(cleanup)

describe('web search configuration', () => {
  it('overrides an inherited provider with an explicit automatic selection', () => {
    config.values['Tools.Web.DeepSeekProvider'] = 'inherited-account'
    render(<WebSearchSettingsGroup providers={[]} providersLoading={false} />)

    fireEvent.change(
      screen.getByLabelText('settings.llm.webSearch.deepSeekProvider'),
      { target: { value: '' } }
    )

    expect(config.write).toHaveBeenCalledWith('Tools.Web.DeepSeekProvider', '')
  })

  it('saves provider reuse without asking for a duplicate key', () => {
    render(
      <WebSearchSettingsGroup
        providers={[
          {
            id: 'account',
            displayName: 'Account',
            protocol: 'anthropic',
            endPoint: '',
            hasApiKey: false,
            managedBy: 'modelService',
            isAuthenticated: true,
            webSearchBackend: 'deepseek'
          }
        ]}
        providersLoading={false}
      />
    )
    fireEvent.change(screen.getByLabelText('settings.llm.webSearch.service'), {
      target: { value: 'DeepSeek' }
    })
    fireEvent.change(
      screen.getByLabelText('settings.llm.webSearch.deepSeekProvider'),
      { target: { value: 'account' } }
    )
    expect(config.write).toHaveBeenCalledWith(
      'Tools.Web.SearchProvider',
      'DeepSeek'
    )
    expect(config.write).toHaveBeenCalledWith(
      'Tools.Web.DeepSeekProvider',
      'account'
    )
    expect(
      screen.queryByLabelText('settings.llm.webSearch.deepSeekKey')
    ).toBeNull()
  })

  it('keeps Exa and DeepSeek key drafts separate and supports clearing', () => {
    config.values['Tools.Web.SearchProvider'] = 'Exa'
    const { rerender } = render(
      <WebSearchSettingsGroup providers={[]} providersLoading={false} />
    )
    fireEvent.change(screen.getByLabelText('settings.llm.webSearch.exaKey'), {
      target: { value: 'unsaved-exa' }
    })
    config.values['Tools.Web.SearchProvider'] = 'DeepSeek'
    config.values['Tools.Web.DeepSeekCredentialSource'] = 'ApiKey'
    config.values['Tools.Web.DeepSeekApiKey'] = '***'
    rerender(<WebSearchSettingsGroup providers={[]} providersLoading={false} />)
    const input = screen.getByLabelText('settings.llm.webSearch.deepSeekKey')
    fireEvent.blur(input)
    expect(config.write).not.toHaveBeenCalled()
    fireEvent.change(input, { target: { value: ' new-key ' } })
    fireEvent.blur(input)
    expect(config.write).toHaveBeenLastCalledWith(
      'Tools.Web.DeepSeekApiKey',
      'new-key'
    )
    fireEvent.change(input, { target: { value: '' } })
    fireEvent.blur(input)
    expect(config.write).toHaveBeenLastCalledWith(
      'Tools.Web.DeepSeekApiKey',
      null
    )
  })
})
