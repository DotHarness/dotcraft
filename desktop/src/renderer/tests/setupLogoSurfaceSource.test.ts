import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const rendererRoot = resolve(__dirname, '..')

function readRendererFile(path: string): string {
  return readFileSync(resolve(rendererRoot, path), 'utf8')
}

describe('setup logo surfaces', () => {
  it('keeps welcome and setup logos free of circular base rings', () => {
    const tokensCss = readRendererFile('styles/tokens.css')
    const welcomeSource = readRendererFile('components/WelcomeScreen.tsx')
    const interstitialSource = readRendererFile('components/WorkspaceSetupInterstitial.tsx')
    const wizardSource = readRendererFile('components/WorkspaceSetupWizard.tsx')

    expect(tokensCss).not.toContain('.welcome-logo-focus::before')
    expect(tokensCss).not.toContain('.welcome-logo-focus::after')
    expect(tokensCss).not.toContain('.setup-logo-focus::before')
    expect(tokensCss).not.toContain('.setup-logo-focus::after')
    expect(tokensCss).not.toContain('.welcome-brand-opening-ring')
    expect(tokensCss).not.toContain('.setup-logo-ring')
    expect(tokensCss).not.toContain('welcome-logo-breathe')
    expect(tokensCss).not.toContain('setup-logo-breathe')
    expect(tokensCss).not.toContain('setup-logo-open')
    expect(tokensCss).not.toContain('setup-wizard-logo-beat')
    expect(tokensCss).toContain('--dotcraft-logo-shadow')
    expect(tokensCss).toContain('filter: var(--dotcraft-logo-shadow)')
    expect(tokensCss).toContain('@keyframes setup-profile-logo-fade-in')
    expect(tokensCss).toContain('@keyframes setup-profile-logo-fade-out')
    expect(tokensCss).toContain('.setup-profile-logo-image--entering')
    expect(tokensCss).toContain('.setup-profile-logo-image--leaving')
    expect(welcomeSource).not.toContain('welcome-logo-ring')
    expect(welcomeSource).not.toContain('welcome-brand-opening-logo')
    expect(interstitialSource).not.toContain('setup-logo-ring')
    expect(wizardSource).toContain('setup-wizard-kicker')
    expect(wizardSource).toContain('ProfileLogoTransition')
    expect(wizardSource).not.toContain('key={`${step}:${profile}`}')
    const profileLogoAnimationCss = tokensCss.match(
      /@keyframes setup-profile-logo-fade-in[\s\S]*?\.setup-wizard-title-block/
    )?.[0] ?? ''
    expect(profileLogoAnimationCss).not.toContain('scale(')
    expect(profileLogoAnimationCss).not.toContain('translate')
    expect(tokensCss).not.toMatch(/\.setup-wizard-title-block\s*{[^}]*animation:/s)
    expect(tokensCss).not.toContain('.setup-wizard-shell--handoff .setup-wizard-title-block')
    expect(tokensCss).toContain('.setup-wizard-kicker.tool-running-gradient-text')
    expect(tokensCss).toContain('tool-running-gradient 4.8s linear infinite')
    expect(tokensCss).not.toMatch(/\s+\.setup-wizard-kicker,\r?\n\s+\.setup-stepper-row,/)
    expect(tokensCss).toContain('@keyframes setup-nav-back-enter')
    expect(tokensCss).toContain('@keyframes setup-nav-next-enter')
    expect(tokensCss).toContain('.setup-wizard-shell--handoff .workspace-setup-nav-button')
    expect(tokensCss).toMatch(
      /\.workspace-launch-transition--connecting \.workspace-launch-transition__logo,\s*\.workspace-launch-transition--preparing \.workspace-launch-transition__logo\s*{[^}]*var\(--launch-logo-to-x/s
    )
    expect(tokensCss).not.toMatch(
      /\.workspace-launch-transition--welcome-hold \.workspace-launch-transition__logo,\s*\.workspace-launch-transition--setup-complete-to-center \.workspace-launch-transition__logo/
    )
    expect(tokensCss).toMatch(
      /\.workspace-launch-transition--setup-complete-to-center \.workspace-launch-transition__logo,\s*\.workspace-launch-transition--connecting \.workspace-launch-transition__logo,\s*\.workspace-launch-transition--preparing \.workspace-launch-transition__logo/s
    )
    expect(tokensCss).toMatch(
      /@media \(prefers-reduced-motion: reduce\)[\s\S]*\.setup-profile-logo-image--entering/
    )
  })
})
