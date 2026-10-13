import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { McpOAuthFields } from '../components/settings/panels/mcp/McpOAuthFields'
import { McpServerEditor } from '../components/settings/panels/mcp/McpServerEditor'
import { hasExplicitAuthorization, useMcpDraft } from '../components/settings/panels/mcp/useMcpDraft'

function Editor() {
  const editor = useMcpDraft()
  return (
    <>
      <button
        onClick={() =>
          editor.startMcpDraft({
            name: 'registered',
            enabled: true,
            transport: 'streamableHttp',
            url: 'https://example.test/mcp',
            oauth: {
              clientId: 'client',
              clientSecret: '********',
              hasClientSecret: true
            }
          })
        }
      >
        Load
      </button>
      <McpOAuthFields
        value={editor.mcpDraft.oauth}
        onChange={(oauth) => editor.setMcpDraft((previous) => ({ ...previous, oauth }))}
      />
      <button
        onClick={() =>
          editor.setMcpDraft((previous) => ({
            ...previous,
            transport: 'stdio'
          }))
        }
      >
        Stdio
      </button>
      <output data-testid="payload">{JSON.stringify(editor.buildDraftPayload())}</output>
    </>
  )
}

describe('MCP OAuth editor', () => {
  it('preserves an unchanged masked secret and sends replacements and explicit deletion', () => {
    render(
      <LocaleProvider loadSettings={false}>
        <Editor />
      </LocaleProvider>
    )
    fireEvent.click(screen.getByText('Load'))
    expect(JSON.parse(screen.getByTestId('payload').textContent!).oauth).toEqual({
      clientId: 'client'
    })
    fireEvent.change(screen.getByLabelText('Client secret'), {
      target: { value: 'replacement' }
    })
    expect(JSON.parse(screen.getByTestId('payload').textContent!).oauth.clientSecret).toBe(
      'replacement'
    )
    fireEvent.change(screen.getByLabelText('Client secret'), {
      target: { value: '' }
    })
    expect(JSON.parse(screen.getByTestId('payload').textContent!).oauth.clientSecret).toBe('')
  })

  it('removes OAuth explicitly when disabled or switched to stdio', () => {
    render(
      <LocaleProvider loadSettings={false}>
        <Editor />
      </LocaleProvider>
    )
    fireEvent.click(screen.getByText('Load'))
    fireEvent.click(screen.getByRole('button', { name: 'Automatic' }))
    expect(JSON.parse(screen.getByTestId('payload').textContent!).oauth).toBeNull()
    fireEvent.click(screen.getByText('Load'))
    fireEvent.click(screen.getByText('Stdio'))
    expect(JSON.parse(screen.getByTestId('payload').textContent!).oauth).toBeNull()
  })

  it('switches to an environment reference and serializes the configured callback', () => {
    render(
      <LocaleProvider loadSettings={false}>
        <Editor />
      </LocaleProvider>
    )
    fireEvent.click(screen.getByText('Load'))
    fireEvent.click(screen.getByRole('button', { name: 'Environment variable' }))
    fireEvent.change(screen.getByLabelText('Secret environment variable'), {
      target: { value: 'MCP_SECRET' }
    })
    fireEvent.change(screen.getByLabelText('Callback URL'), {
      target: { value: 'http://127.0.0.1:18555/callback/' }
    })
    expect(JSON.parse(screen.getByTestId('payload').textContent!).oauth).toEqual({
      clientId: 'client',
      clientSecret: null,
      clientSecretEnvVar: 'MCP_SECRET',
      callbackUrl: 'http://127.0.0.1:18555/callback/'
    })
  })

  it('flags explicit Authorization only for headers that will be saved', () => {
    const draft = { name: 'remote', enabled: true, transport: 'streamableHttp' as const }
    const row = (key: string, value: string) => ({ id: key, key, value })
    expect(hasExplicitAuthorization(draft, [row('Authorization', '')], [])).toBe(false)
    expect(hasExplicitAuthorization(draft, [], [row('authorization', 'MCP_TOKEN')])).toBe(true)
    expect(hasExplicitAuthorization({ ...draft, bearerTokenEnvVar: 'MCP_TOKEN' }, [], [])).toBe(true)
  })

  it('locks the name of an existing server so a saved secret cannot be orphaned by a rename', () => {
    function NameEditor() {
      const editor = useMcpDraft()
      return (
        <>
          <button onClick={() => editor.startMcpDraft()}>New</button>
          <button
            onClick={() =>
              editor.startMcpDraft({
                name: 'registered',
                enabled: true,
                transport: 'streamableHttp',
                url: 'https://example.test/mcp',
                oauth: { clientId: 'client', clientSecret: '********', hasClientSecret: true }
              })
            }
          >
            Load
          </button>
          <McpServerEditor editor={editor} />
        </>
      )
    }
    render(
      <LocaleProvider loadSettings={false}>
        <NameEditor />
      </LocaleProvider>
    )
    fireEvent.click(screen.getByText('Load'))
    expect(screen.getByLabelText('Name')).toBeDisabled()
    fireEvent.click(screen.getByText('New'))
    expect(screen.getByLabelText('Name')).not.toBeDisabled()
  })
})
