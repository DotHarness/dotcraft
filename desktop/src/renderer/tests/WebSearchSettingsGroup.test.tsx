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
  const deepSeek = (id: string) => ({
    id,
    displayName: id,
    protocol: 'anthropic',
    endPoint: '',
    hasApiKey: false,
    managedBy: 'modelService',
    isAuthenticated: true,
    webSearchBackend: 'deepseek'
  })

  it('shows no DeepSeek settings in automatic mode', () => {
    render(<WebSearchSettingsGroup providers={[deepSeek('account')]} providersLoading={false} />)

    expect(screen.queryByLabelText('settings.llm.webSearch.credentials')).toBeNull()
    expect(screen.getByLabelText('settings.llm.webSearch.exaKey')).toBeTruthy()
  })

  it('overrides a stale provider with an explicit automatic selection', () => {
    config.values['Tools.Web.SearchProvider'] = 'DeepSeek'
    config.values['Tools.Web.DeepSeekProvider'] = 'inherited-account'
    render(<WebSearchSettingsGroup providers={[]} providersLoading={false} />)

    fireEvent.change(
      screen.getByLabelText('settings.llm.webSearch.deepSeekProvider'),
      { target: { value: '' } }
    )

    expect(config.write).toHaveBeenCalledWith('Tools.Web.DeepSeekProvider', '')
  })

  it('reuses the only provider without asking for a choice or a key', () => {
    render(<WebSearchSettingsGroup providers={[deepSeek('account')]} providersLoading={false} />)
    fireEvent.change(screen.getByLabelText('settings.llm.webSearch.service'), {
      target: { value: 'DeepSeek' }
    })
    expect(config.write).toHaveBeenCalledWith('Tools.Web.SearchProvider', 'DeepSeek')

    config.values['Tools.Web.SearchProvider'] = 'DeepSeek'
    cleanup()
    render(<WebSearchSettingsGroup providers={[deepSeek('account')]} providersLoading={false} />)
    expect(screen.queryByLabelText('settings.llm.webSearch.deepSeekProvider')).toBeNull()
    expect(screen.queryByLabelText('settings.llm.webSearch.deepSeekKey')).toBeNull()
  })

  it('asks for a provider only when there are several', () => {
    config.values['Tools.Web.SearchProvider'] = 'DeepSeek'
    render(
      <WebSearchSettingsGroup
        providers={[deepSeek('personal'), deepSeek('team')]}
        providersLoading={false}
      />
    )
    fireEvent.change(
      screen.getByLabelText('settings.llm.webSearch.deepSeekProvider'),
      { target: { value: 'team' } }
    )
    expect(config.write).toHaveBeenCalledWith('Tools.Web.DeepSeekProvider', 'team')
  })

  it('offers ChatGPT only while a subscription provider is signed in', () => {
    const subscription = (isAuthenticated: boolean) => ({
      id: 'openai',
      displayName: 'OpenAI',
      protocol: 'openai-responses',
      endPoint: '',
      hasApiKey: false,
      authMethod: 'chatgptOAuth',
      isAuthenticated
    })
    const chatGptOption = () => screen.getByRole('option', { name: 'ChatGPT' }) as HTMLOptionElement

    render(<WebSearchSettingsGroup providers={[subscription(false)]} providersLoading={false} />)
    expect(chatGptOption().disabled).toBe(true)

    cleanup()
    render(<WebSearchSettingsGroup providers={[subscription(true)]} providersLoading={false} />)
    expect(chatGptOption().disabled).toBe(false)
    fireEvent.change(screen.getByLabelText('settings.llm.webSearch.service'), {
      target: { value: 'ChatGPT' }
    })
    expect(config.write).toHaveBeenCalledWith('Tools.Web.SearchProvider', 'ChatGPT')
  })

  it('offers a key when no DeepSeek provider exists', () => {
    config.values['Tools.Web.SearchProvider'] = 'DeepSeek'
    render(<WebSearchSettingsGroup providers={[]} providersLoading={false} />)

    fireEvent.click(screen.getByRole('button', { name: 'settings.llm.webSearch.useApiKey' }))

    expect(config.write).toHaveBeenCalledWith('Tools.Web.DeepSeekCredentialSource', 'ApiKey')
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
