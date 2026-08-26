import { act, render, screen } from '@testing-library/react'
import { describe, expect, it, vi, beforeEach } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import {
  WorkspaceLaunchTransition,
  centeredLaunchLogoRect,
  type LaunchLogoRect,
  type WorkspaceLaunchTransitionPhase
} from '../components/WorkspaceLaunchTransition'
import { installDesktopApiMock } from './desktopApiMock'

const settingsGet = vi.fn()

function setViewportSize(width: number, height: number): void {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: width })
  Object.defineProperty(window, 'innerHeight', { configurable: true, value: height })
}

function installApi(locale: 'en' | 'zh-Hans'): void {
  installDesktopApiMock({ initialLocale: locale, settings: { get: settingsGet } })
}

function renderTransition(phase: WorkspaceLaunchTransitionPhase, logoSrc?: string) {
  const from: LaunchLogoRect = { left: 10, top: 20, width: 96, height: 96 }
  const to: LaunchLogoRect = { left: 100, top: 120, width: 96, height: 96 }
  return render(
    <LocaleProvider>
      <WorkspaceLaunchTransition phase={phase} from={from} to={to} logoSrc={logoSrc} />
    </LocaleProvider>
  )
}

describe('WorkspaceLaunchTransition', () => {
  beforeEach(() => {
    settingsGet.mockResolvedValue({ locale: 'en' })
    setViewportSize(1024, 768)
    installApi('en')
  })

  it('renders a single full-color logo and the light connecting message', async () => {
    const { container } = renderTransition('connecting')

    expect(container.querySelectorAll('.workspace-launch-transition__logo')).toHaveLength(1)
    expect(container.querySelectorAll('.workspace-launch-transition__scrim')).toHaveLength(1)
    expect(container.querySelector('.welcome-brand-opening-logo')).toBeNull()
    expect(await screen.findByText('Connecting to workspace…')).toBeInTheDocument()
  })

  it('renders the Chinese connecting message from initialLocale before settings resolve', () => {
    settingsGet.mockReturnValue(new Promise(() => {}))
    installApi('zh-Hans')

    renderTransition('connecting')

    expect(screen.getByText('正在连接工作区…')).toBeInTheDocument()
  })

  it('computes the centered launch rect from the viewport', () => {
    setViewportSize(1200, 800)

    expect(centeredLaunchLogoRect()).toEqual({
      left: 552,
      top: 352,
      width: 96,
      height: 96
    })
  })

  it.each([
    'connecting',
    'preparing',
    'main-reveal',
    'error-reveal'
  ] as const)('recenters the %s logo when the viewport is resized', (phase) => {
    setViewportSize(800, 600)
    const { container } = renderTransition(phase)
    const overlay = container.querySelector<HTMLElement>('.workspace-launch-transition')

    expect(overlay?.style.getPropertyValue('--launch-logo-from-x')).toBe('352px')
    expect(overlay?.style.getPropertyValue('--launch-logo-to-y')).toBe('252px')

    act(() => {
      setViewportSize(1200, 800)
      window.dispatchEvent(new Event('resize'))
    })

    expect(overlay?.style.getPropertyValue('--launch-logo-from-x')).toBe('552px')
    expect(overlay?.style.getPropertyValue('--launch-logo-from-y')).toBe('352px')
    expect(overlay?.style.getPropertyValue('--launch-logo-to-x')).toBe('552px')
    expect(overlay?.style.getPropertyValue('--launch-logo-to-y')).toBe('352px')
  })

  it.each([
    'welcome-to-center',
    'setup-complete-to-center'
  ] as const)('updates only the center target during %s', (phase) => {
    setViewportSize(800, 600)
    const { container } = renderTransition(phase)
    const overlay = container.querySelector<HTMLElement>('.workspace-launch-transition')

    expect(overlay?.style.getPropertyValue('--launch-logo-from-x')).toBe('10px')
    expect(overlay?.style.getPropertyValue('--launch-logo-from-y')).toBe('20px')
    expect(overlay?.style.getPropertyValue('--launch-logo-to-x')).toBe('352px')
    expect(overlay?.style.getPropertyValue('--launch-logo-to-y')).toBe('252px')

    act(() => {
      setViewportSize(1200, 800)
      window.dispatchEvent(new Event('resize'))
    })

    expect(overlay?.style.getPropertyValue('--launch-logo-from-x')).toBe('10px')
    expect(overlay?.style.getPropertyValue('--launch-logo-from-y')).toBe('20px')
    expect(overlay?.style.getPropertyValue('--launch-logo-to-x')).toBe('552px')
    expect(overlay?.style.getPropertyValue('--launch-logo-to-y')).toBe('352px')
  })

  it('preserves element-to-element handoff coordinates when the viewport is resized', () => {
    const { container } = renderTransition('setup-handoff')
    const overlay = container.querySelector<HTMLElement>('.workspace-launch-transition')

    act(() => {
      setViewportSize(1200, 800)
      window.dispatchEvent(new Event('resize'))
    })

    expect(overlay?.style.getPropertyValue('--launch-logo-from-x')).toBe('10px')
    expect(overlay?.style.getPropertyValue('--launch-logo-from-y')).toBe('20px')
    expect(overlay?.style.getPropertyValue('--launch-logo-to-x')).toBe('100px')
    expect(overlay?.style.getPropertyValue('--launch-logo-to-y')).toBe('120px')
  })

  it('uses the setup logo while preparing a newly initialized workspace', async () => {
    const setupLogo = 'setup-logo.svg'
    const { container } = renderTransition('preparing', setupLogo)

    const logo = container.querySelector('.workspace-launch-transition__logo')
    expect(logo).toBeInstanceOf(HTMLImageElement)
    expect(logo).toHaveAttribute('src', setupLogo)
    expect(await screen.findByText('Preparing your workspace…')).toBeInTheDocument()
  })

  it('renders the Chinese preparing message from initialLocale before settings resolve', () => {
    settingsGet.mockReturnValue(new Promise(() => {}))
    installApi('zh-Hans')

    renderTransition('preparing')

    expect(screen.getByText('正在准备工作区…')).toBeInTheDocument()
  })
})
