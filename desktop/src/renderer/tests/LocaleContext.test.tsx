import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider, useT } from '../contexts/LocaleContext'

const settingsGet = vi.fn()

function Probe(): JSX.Element {
  const t = useT()
  return <div>{t('workspaceLaunch.connecting')}</div>
}

beforeEach(() => {
  settingsGet.mockReset()
  document.documentElement.lang = 'en'
})

describe('LocaleProvider', () => {
  it('uses initialLocale for the first render before settings resolve', () => {
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

    render(
      <LocaleProvider>
        <Probe />
      </LocaleProvider>
    )

    expect(screen.getByText('正在连接工作区…')).toBeInTheDocument()
    expect(document.documentElement.lang).toBe('zh-Hans')
  })
})
