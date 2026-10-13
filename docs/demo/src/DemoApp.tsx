/**
 * Demo shell — replicates the Desktop WindowFrame/AppChrome wrapper from
 * App.tsx (which is not exported) and composes the real three-panel layout.
 */
import { useEffect } from 'react'
import { ThreePanel } from '@renderer/components/layout/ThreePanel'
import { Sidebar } from '@renderer/components/layout/Sidebar'
import { SettingsSidebar } from '@renderer/components/layout/SettingsSidebar'
import { ConversationPanel } from '@renderer/components/layout/ConversationPanel'
import { DetailPanel } from '@renderer/components/layout/DetailPanel'
import { CustomMenuBar } from '@renderer/components/layout/CustomMenuBar'
import { CoreMainViewBoundary, coreMainViews } from '@renderer/core/coreMainViewRoutes'
import { useUIStore } from '@renderer/stores/uiStore'
import { DEMO_WORKSPACE_NAME, DEMO_WORKSPACE_PATH } from './mockApi'

const SettingsView = coreMainViews.settings

function ReportReady(): null {
  useEffect(() => {
    if (window.parent === window) return
    const frame = requestAnimationFrame(() => {
      window.parent.postMessage({ type: 'dotcraft-showcase:ready' }, '*')
    })
    return () => cancelAnimationFrame(frame)
  }, [])
  return null
}

export function DemoApp(): JSX.Element {
  const settings = useUIStore((s) => s.activeMainView === 'settings')

  return (
    <div
      className="dotcraft-window-frame"
      style={{
        position: 'relative',
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        width: '100%',
        overflow: 'hidden',
        isolation: 'isolate',
        background: 'var(--chrome-glass)',
        boxShadow: 'inset 0 0 0 1px var(--shell-chrome-border)'
      }}
    >
      <div
        style={{
          display: 'flex',
          flexDirection: 'column',
          height: '100%',
          width: '100%',
          overflow: 'hidden'
        }}
      >
        <CustomMenuBar />
        <div
          style={{
            flex: 1,
            minHeight: 0,
            overflow: 'hidden',
            display: 'flex',
            flexDirection: 'column'
          }}
        >
          <ThreePanel
            sidebar={
              settings ? (
                <SettingsSidebar />
              ) : (
                <Sidebar
                  workspaceName={DEMO_WORKSPACE_NAME}
                  workspacePath={DEMO_WORKSPACE_PATH}
                  localWorkspacePath={DEMO_WORKSPACE_PATH}
                />
              )
            }
            conversation={
              settings ? (
                <CoreMainViewBoundary>
                  <SettingsView
                    workspacePath={DEMO_WORKSPACE_PATH}
                    identityWorkspacePath={DEMO_WORKSPACE_PATH}
                    onThreadListRefreshRequested={() => {}}
                  />
                  <ReportReady />
                </CoreMainViewBoundary>
              ) : (
                <>
                  <ConversationPanel
                    workspacePath={DEMO_WORKSPACE_PATH}
                    identityWorkspacePath={DEMO_WORKSPACE_PATH}
                    projectKey={DEMO_WORKSPACE_PATH}
                  />
                  <ReportReady />
                </>
              )
            }
            detail={<DetailPanel workspacePath={DEMO_WORKSPACE_PATH} />}
          />
        </div>
      </div>
    </div>
  )
}
