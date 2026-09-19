import { render, screen } from '@testing-library/react'
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

  it('renders the Chinese preparing message from initialLocale before settings resolve', () => {
    settingsGet.mockReturnValue(new Promise(() => {}))
    installApi('zh-Hans')

    renderTransition('preparing')

    expect(screen.getByText('正在准备工作区…')).toBeInTheDocument()
  })
})
