import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const rendererRoot = resolve(__dirname, '..')

function readRendererFile(path: string): string {
  return readFileSync(resolve(rendererRoot, path), 'utf8').replace(/\r\n/g, '\n')
}

describe('workspace launch flow source', () => {
  it('keeps workspace switching in App and gates ready workspaces on connected status', () => {
    const appSource = readRendererFile('App.tsx')
    const welcomeSource = readRendererFile('components/WelcomeScreen.tsx')
    const tokensCss = readRendererFile('styles/tokens.css')

    expect(welcomeSource).toContain('onOpenWorkspace')
    expect(welcomeSource).not.toContain('window.api.workspace.switch(path)')
    expect(appSource).toContain('handleOpenWorkspaceFromWelcome')
    expect(appSource).toContain('window.api?.initialWorkspaceStatus')
    expect(appSource).toContain('createInitialWorkspaceLaunchTransition(initialWorkspaceStatus)')
    expect(appSource).toContain('useRef(initialWorkspacePath)')
    expect(appSource).toContain('useRef(hasPreloadedWorkspaceStatusRef.current)')
    expect(appSource).toContain('window.api.workspace.switch(request.path)')
    expect(appSource).toContain("phase: 'welcome-hold'")
    expect(appSource).toContain("workspaceLaunchTransition?.phase === 'welcome-hold'")
    expect(appSource).toContain("workspaceLaunchTransition.phase === 'welcome-hold' && workspaceStatus.status === 'ready'")
    expect(appSource).toContain("isInitialWorkspaceStatus && path && payload.status === 'ready'")
    expect(appSource).toContain("phase: 'connecting'")
    expect(appSource).toContain("workspaceStatus.status === 'ready' && status === 'connected'")
    expect(appSource).toContain("phase: 'main-reveal'")
    expect(appSource).toContain("phase: 'error-reveal'")
    expect(appSource).toContain("phase: 'setup-complete-to-center'")
    expect(appSource).toContain("phase: 'preparing'")
    expect(appSource).toContain('keepSetupFlowDuringCompletionCover')
    expect(appSource).toContain("workspaceLaunchTransition?.phase === 'setup-complete-to-center'")
    expect(appSource).toContain('setupWorkspaceStatusSnapshotRef')
    expect(appSource).toContain('onRunSetup={handleRunWorkspaceSetup}')
    expect(appSource).toContain('logoSrc: context.logoSrc')
    expect(appSource.match(/<WorkspaceLaunchTransition\s/g) ?? []).toHaveLength(1)
    expect(appSource.match(/\{launchOverlay\}/g) ?? []).toHaveLength(1)
    expect(appSource).toContain('overlays={(')
    expect(appSource).toContain('let content: ReactNode')
    expect(tokensCss).toContain('.workspace-launch-transition {\n  position: absolute;')
    expect(tokensCss).toContain('.workspace-launch-transition__scrim')
    expect(tokensCss).toContain('--workspace-launch-surface:')
    expect(tokensCss).toContain('#202020,\n      #181818')
    expect(tokensCss).toContain('#f3f3ee,\n      #ededed')
    expect(tokensCss).toContain('background: var(--workspace-launch-surface);')
    expect(tokensCss).toContain('border-radius: inherit;')
    expect(tokensCss).not.toContain('--workspace-launch-surface: var(--chrome-glass)')
    expect(tokensCss).not.toContain('background: var(--bg-primary);')
    expect(tokensCss).toContain('.workspace-launch-transition__logo {\n  position: absolute;')
    expect(tokensCss).toContain('.workspace-setup-logo-handoff {\n  position: absolute;')
    expect(tokensCss).toContain('.workspace-setup-logo-handoff__logo {\n  position: absolute;')
    expect(tokensCss).not.toContain('.workspace-launch-transition--welcome-to-center {\n  animation:')
  })

  it('routes needs-setup through launch handoff and setup wizard entry through local handoff', () => {
    const appSource = readRendererFile('App.tsx')
    const transitionSource = readRendererFile('components/WorkspaceLaunchTransition.tsx')

    expect(appSource).toContain("phase: 'setup-handoff'")
    expect(appSource).toContain("workspaceLaunchTransition.phase === 'welcome-hold'")
    expect(appSource).toContain('workspaceLaunchTransition.from')
    expect(appSource).toContain('workspaceLaunchTransition.to')
    expect(appSource).toContain('setSetupLogoHandoff')
    expect(appSource).toContain('<WorkspaceSetupLogoHandoff')
    expect(transitionSource).toContain("export type WorkspaceSetupLogoHandoffPhase = 'hold' | 'move'")
    expect(transitionSource).not.toContain("'setup-to-wizard-hold'")
    expect(transitionSource).not.toContain("'setup-to-wizard'")
    expect(appSource).toContain('hideLogo={hideSetupInterstitialLogo}')
    expect(appSource).toContain('hideLogo={hideSetupWizardLogo}')
    expect(appSource).toContain('deferContent={deferSetupWizardContent}')
  })

  it('keeps setup content under the setup-complete cover until the scrim is opaque', () => {
    const appSource = readRendererFile('App.tsx')

    expect(appSource).toContain("if (workspaceLaunchTransition?.phase === 'setup-complete-to-center')")
    expect(appSource).toContain('return\n    }\n\n    setShowSetupWizard(false)')
    expect(appSource).toContain('workspaceStatus.status === \'needs-setup\' || keepSetupFlowDuringCompletionCover')
    expect(appSource).toContain('setupWorkspaceStatusSnapshotRef.current ?? workspaceStatus')
  })
})
