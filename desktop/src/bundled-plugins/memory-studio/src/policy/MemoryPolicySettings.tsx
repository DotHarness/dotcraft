import { SettingsBreadcrumb, SettingsPanelShell, type DesktopPluginSurfaceWrapperProps } from '@dotcraft/plugin'
import { useState, type JSX } from 'react'
import { usePolicyState } from '../hooks/useMemoryData'
import { useT } from '../i18n'
import { Notice } from '../shared/ui'
import { PolicySettingEditor } from './PolicySettingEditor'
import { PolicyTab, type PolicyEditorSetting } from './PolicyTab'
import styles from './policy.module.css'

export function MemoryPolicySettings({
  host,
  context,
  children
}: DesktopPluginSurfaceWrapperProps<'settings.personalization'>): JSX.Element {
  const t = useT()
  const policy = usePolicyState(host)
  const [editing, setEditing] = useState<PolicyEditorSetting | null>(null)

  if (policy.status === 'unsupported') return <>{children}</>

  if (editing != null && policy.value != null) {
    const title = t(`policy.setting.${editing}` as const)
    return (
      <SettingsPanelShell
        title={title}
        description={t(`policy.setting.${editing}.description` as const)}
        breadcrumb={(
          <SettingsBreadcrumb
            parentLabel={context.title}
            currentLabel={title}
            onBack={() => setEditing(null)}
          />
        )}
      >
        <PolicySettingEditor
          key={editing}
          state={policy.value}
          setting={editing}
          onClose={() => setEditing(null)}
          onChanged={policy.reload}
        />
      </SettingsPanelShell>
    )
  }

  return (
    <div className={styles.stack}>
      {children}
      {policy.value ? (
        <PolicyTab
          state={policy.value}
          onEdit={setEditing}
          onChanged={policy.reload}
        />
      ) : policy.status === 'error' ? (
        <Notice tone="error">{policy.error}</Notice>
      ) : null}
    </div>
  )
}
