import type { JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import type { McpTransport } from '../../../../stores/mcpStore'
import { Input } from '../../../ui/Input'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { EditableKeyValueList, EditableValueList } from '../../ui/EditableList'
import { SegmentedControl } from '../../ui/SegmentedControl'
import { McpOAuthFields } from './McpOAuthFields'
import { hasExplicitAuthorization, type useMcpDraft } from './useMcpDraft'

export function McpServerEditor({ editor }: { editor: ReturnType<typeof useMcpDraft> }): JSX.Element {
  const t = useT()
  const {
    mcpDraft,
    setMcpDraft,
    argRows,
    setArgRows,
    envRows,
    setEnvRows,
    envVarRows,
    setEnvVarRows,
    httpHeaderRows,
    setHttpHeaderRows,
    envHttpHeaderRows,
    setEnvHttpHeaderRows
  } = editor
  return (
    <>
      <SettingsGroup title={t('settings.group.identity')}>
        <SettingsRow label={t('settings.mcp.field.name')} htmlFor="mcp-server-name" orientation="block">
          <Input
            id="mcp-server-name"
            value={mcpDraft.name}
            onChange={(e) => setMcpDraft((prev) => ({ ...prev, name: e.target.value }))}
            placeholder={t('settings.mcp.field.namePlaceholder')}
          />
        </SettingsRow>
        <SettingsRow
          label={t('settings.mcp.field.transport')}
          control={
            <SegmentedControl<McpTransport>
              value={mcpDraft.transport}
              options={[
                { value: 'stdio', label: t('settings.mcp.transport.stdio') },
                { value: 'streamableHttp', label: t('settings.mcp.transport.http') }
              ]}
              onChange={(transport) => setMcpDraft((prev) => ({ ...prev, transport }))}
              ariaLabel={t('settings.mcp.field.transport')}
            />
          }
        />
      </SettingsGroup>

      {mcpDraft.transport === 'stdio' && (
        <>
          <SettingsGroup title={t('settings.group.command')}>
            <SettingsRow label={t('settings.mcp.field.command')} htmlFor="mcp-server-command" orientation="block">
              <Input
                id="mcp-server-command"
                value={mcpDraft.command ?? ''}
                onChange={(e) => setMcpDraft((prev) => ({ ...prev, command: e.target.value }))}
                placeholder="npx"
                mono
              />
            </SettingsRow>
            <SettingsRow label={t('settings.mcp.field.args')} orientation="block">
              <EditableValueList
                rows={argRows}
                setRows={setArgRows}
                placeholder={t('settings.mcp.field.argsPlaceholder')}
                addLabel={t('settings.mcp.addArgument')}
              />
            </SettingsRow>
            <SettingsRow label={t('settings.mcp.field.cwd')} htmlFor="mcp-server-cwd" orientation="block">
              <Input
                id="mcp-server-cwd"
                value={mcpDraft.cwd ?? ''}
                onChange={(e) => setMcpDraft((prev) => ({ ...prev, cwd: e.target.value }))}
                placeholder="~/code"
                mono
              />
            </SettingsRow>
          </SettingsGroup>

          <SettingsGroup title={t('settings.group.environment')}>
            <SettingsRow label={t('settings.mcp.field.env')} orientation="block">
              <EditableKeyValueList
                rows={envRows}
                setRows={setEnvRows}
                keyPlaceholder={t('settings.mcp.keyPlaceholder')}
                valuePlaceholder={t('settings.mcp.valuePlaceholder')}
                addLabel={t('settings.mcp.addVariable')}
              />
            </SettingsRow>
            <SettingsRow
              label={t('settings.mcp.field.envForwarding')}
              description={t('settings.mcp.field.envForwardingHint')}
              orientation="block"
            >
              <EditableValueList
                rows={envVarRows}
                setRows={setEnvVarRows}
                placeholder={t('settings.mcp.field.envForwardingPlaceholder')}
                addLabel={t('settings.mcp.addVariable')}
              />
            </SettingsRow>
          </SettingsGroup>
        </>
      )}

      {mcpDraft.transport === 'streamableHttp' && (
        <>
          <SettingsGroup title={t('settings.group.http')}>
            <SettingsRow label={t('settings.mcp.field.url')} htmlFor="mcp-server-url" orientation="block">
              <Input
                id="mcp-server-url"
                value={mcpDraft.url ?? ''}
                onChange={(e) => setMcpDraft((prev) => ({ ...prev, url: e.target.value }))}
                placeholder="https://example.com/mcp"
                mono
              />
            </SettingsRow>
            <SettingsRow label={t('settings.mcp.field.bearerEnv')} htmlFor="mcp-server-bearer" orientation="block">
              <Input
                id="mcp-server-bearer"
                value={mcpDraft.bearerTokenEnvVar ?? ''}
                onChange={(e) => setMcpDraft((prev) => ({ ...prev, bearerTokenEnvVar: e.target.value }))}
                placeholder={t('settings.mcp.field.bearerEnvPlaceholder')}
                mono
              />
            </SettingsRow>
            <SettingsRow label={t('settings.mcp.field.httpHeaders')} orientation="block">
              <EditableKeyValueList
                rows={httpHeaderRows}
                setRows={setHttpHeaderRows}
                keyPlaceholder={t('settings.mcp.headerPlaceholder')}
                valuePlaceholder={t('settings.mcp.valuePlaceholder')}
                addLabel={t('settings.mcp.addHeader')}
              />
            </SettingsRow>
            <SettingsRow label={t('settings.mcp.field.envHeaders')} orientation="block">
              <EditableKeyValueList
                rows={envHttpHeaderRows}
                setRows={setEnvHttpHeaderRows}
                keyPlaceholder={t('settings.mcp.headerPlaceholder')}
                valuePlaceholder={t('settings.mcp.field.envForwardingPlaceholder')}
                addLabel={t('settings.mcp.addHeader')}
              />
            </SettingsRow>
          </SettingsGroup>

          <McpOAuthFields
            value={mcpDraft.oauth}
            onChange={(oauth) => setMcpDraft((previous) => ({ ...previous, oauth }))}
            authorizationConflict={hasExplicitAuthorization(mcpDraft, httpHeaderRows, envHttpHeaderRows)}
          />
        </>
      )}
    </>
  )
}
