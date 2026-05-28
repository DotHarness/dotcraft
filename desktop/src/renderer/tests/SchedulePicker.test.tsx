import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { NewTaskDialog } from '../components/automations/NewTaskDialog'
import { SchedulePicker } from '../components/automations/SchedulePicker'
import { useAutomationsStore, type AutomationTemplate } from '../stores/automationsStore'
import { useThreadStore } from '../stores/threadStore'

function renderWithLocale(node: JSX.Element) {
  return render(<LocaleProvider>{node}</LocaleProvider>)
}

describe('SchedulePicker', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useThreadStore.getState().reset()
    useAutomationsStore.setState({
      tasks: [],
      loading: false,
      error: null,
      selectedTaskId: null,
      statusFilter: 'all',
      templates: [],
      templatesLoaded: false
    })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: vi.fn().mockResolvedValue({ locale: 'en' })
        },
        appServer: {
          sendRequest: vi.fn().mockResolvedValue({})
        }
      }
    })
  })

  it('syncs custom minute input when external everyMs changes', async () => {
    const onChange = vi.fn()
    const view = renderWithLocale(
      <SchedulePicker value={{ kind: 'every', everyMs: 45 * 60_000 }} onChange={onChange} />
    )

    expect((screen.getByRole('spinbutton') as HTMLInputElement).value).toBe('45')

    view.rerender(
      <LocaleProvider>
        <SchedulePicker value={{ kind: 'every', everyMs: 90 * 60_000 }} onChange={onChange} />
      </LocaleProvider>
    )

    await waitFor(() => {
      expect((screen.getByRole('spinbutton') as HTMLInputElement).value).toBe('90')
    })

    view.rerender(
      <LocaleProvider>
        <SchedulePicker value={{ kind: 'every', everyMs: 60 * 60_000 }} onChange={onChange} />
      </LocaleProvider>
    )

    expect(screen.queryByRole('spinbutton')).toBeNull()

    view.rerender(
      <LocaleProvider>
        <SchedulePicker
          value={{ kind: 'daily', dailyHour: 9, dailyMinute: 30, tz: 'UTC' }}
          onChange={onChange}
        />
      </LocaleProvider>
    )

    expect(screen.queryByRole('spinbutton')).toBeNull()
  })

  it('updates the custom minute input when templates are applied in NewTaskDialog', async () => {
    const fortyFiveTemplate: AutomationTemplate = {
      id: 'tpl-45',
      title: 'Template 45',
      workflowMarkdown: 'workflow',
      defaultSchedule: { kind: 'every', everyMs: 45 * 60_000 }
    }
    const ninetyTemplate: AutomationTemplate = {
      id: 'tpl-90',
      title: 'Template 90',
      workflowMarkdown: 'workflow',
      defaultSchedule: { kind: 'every', everyMs: 90 * 60_000 }
    }

    useAutomationsStore.setState({
      templates: [fortyFiveTemplate, ninetyTemplate],
      templatesLoaded: true,
      templatesLocale: 'en'
    })

    renderWithLocale(<NewTaskDialog onClose={() => {}} />)

    fireEvent.click(screen.getByRole('button', { name: /use template/i }))
    fireEvent.click(await screen.findByRole('button', { name: /template 45/i }))

    await waitFor(() => {
      expect((screen.getByRole('spinbutton') as HTMLInputElement).value).toBe('45')
    })

    fireEvent.click(screen.getByRole('button', { name: /use template/i }))
    fireEvent.click(await screen.findByRole('button', { name: /template 90/i }))

    await waitFor(() => {
      expect((screen.getByRole('spinbutton') as HTMLInputElement).value).toBe('90')
    })
  })
})
