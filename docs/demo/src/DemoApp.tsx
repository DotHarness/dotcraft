/**
 * Demo shell — replicates the Desktop WindowFrame/AppChrome wrapper from
 * App.tsx (which is not exported) and composes the real three-panel layout.
 */
import { ThreePanel } from '@renderer/components/layout/ThreePanel'
import { Sidebar } from '@renderer/components/layout/Sidebar'
import { ConversationPanel } from '@renderer/components/layout/ConversationPanel'
import { DetailPanel } from '@renderer/components/layout/DetailPanel'
import { CustomMenuBar } from '@renderer/components/layout/CustomMenuBar'
import { DEMO_WORKSPACE_NAME, DEMO_WORKSPACE_PATH } from './mockApi'

export function DemoApp(): JSX.Element {
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
              <Sidebar
                workspaceName={DEMO_WORKSPACE_NAME}
                workspacePath={DEMO_WORKSPACE_PATH}
                localWorkspacePath={DEMO_WORKSPACE_PATH}
              />
            }
            conversation={
              <ConversationPanel
                workspacePath={DEMO_WORKSPACE_PATH}
                identityWorkspacePath={DEMO_WORKSPACE_PATH}
                projectKey={DEMO_WORKSPACE_PATH}
              />
            }
            detail={<DetailPanel workspacePath={DEMO_WORKSPACE_PATH} />}
          />
        </div>
      </div>
    </div>
  )
}
