import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi, beforeEach } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import {
  WorkspaceLaunchTransition,
  centeredLaunchLogoRect,
  type LaunchLogoRect,
  type WorkspaceLaunchTransitionPhase
} from '../components/WorkspaceLaunchTransition'

const settingsGet = vi.fn()

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
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        initialLocale: 'en',
        settings: {
          get: settingsGet
        }
      }
    })
  })

  it('renders a single full-color logo and the light connecting message', async () => {
    const { container } = renderTransition('connecting')

    expect(container.querySelectorAll('.workspace-launch-transition__logo')).toHaveLength(1)
    expect(container.querySelectorAll('.workspace-launch-transition__scrim')).toHaveLength(1)
    expect(container.querySelector('.welcome-brand-opening-logo')).toBeNull()
    expect(await screen.findByText('Connecting to workspace…')).toHaveClass('tool-running-gradient-text')
  })

  it('renders the Chinese connecting message from initialLocale before settings resolve', () => {
    settingsGet.mockReturnValue(new Promise(() => {}))
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        initialLocale: 'zh-Hans',
        settings: {
          get: settingsGet
        }
      }
    })

    renderTransition('connecting')

    expect(screen.getByText('正在连接工作区…')).toHaveClass('tool-running-gradient-text')
  })

  it('computes the centered launch rect from the viewport', () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1200 })
    Object.defineProperty(window, 'innerHeight', { configurable: true, value: 800 })

    expect(centeredLaunchLogoRect()).toEqual({
      left: 552,
      top: 352,
      width: 96,
      height: 96
    })
  })

  it('uses the selected profile logo while preparing a newly initialized workspace', async () => {
    const profileLogo = 'dotcraft-developer.svg'
    const { container } = renderTransition('preparing', profileLogo)

    const logo = container.querySelector('.workspace-launch-transition__logo')
    expect(logo).toBeInstanceOf(HTMLImageElement)
    expect(logo).toHaveAttribute('src', profileLogo)
    expect(await screen.findByText('Preparing your workspace…')).toHaveClass('tool-running-gradient-text')
  })

  it('renders the Chinese preparing message from initialLocale before settings resolve', () => {
    settingsGet.mockReturnValue(new Promise(() => {}))
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        initialLocale: 'zh-Hans',
        settings: {
          get: settingsGet
        }
      }
    })

    renderTransition('preparing')

    expect(screen.getByText('正在准备工作区…')).toHaveClass('tool-running-gradient-text')
  })
})
