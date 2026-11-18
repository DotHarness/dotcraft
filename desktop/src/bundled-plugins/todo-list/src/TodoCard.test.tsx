import '../../../renderer/tests/setupPluginRuntime'
import { act, render, screen, waitFor } from '@testing-library/react'
import type { DesktopPluginHost, DesktopPluginTurnStatusContext } from '@dotcraft/plugin'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { startPlanFeed } from './planFeed'
import { TodoCard } from './TodoCard'

type Todo = { content: string; status: string }
type Listener = (params: unknown) => void

const working: Todo[] = [{ content: 'Read the spec', status: 'completed' }, { content: 'Ship it', status: 'in_progress' }]
const finished: Todo[] = [{ content: 'Read the spec', status: 'completed' }, { content: 'Ship it', status: 'completed' }]

let stopFeed: (() => void) | null = null

function fixtureHost(plan: { title?: string; todos: Todo[] } | null) {
  const listeners = new Set<Listener>()
  const host = {
    environment: { locale: 'en' },
    appServer: {
      request: vi.fn(async () => ({ thread: { plan } })),
      onNotification: (_method: string, listener: Listener) => {
        listeners.add(listener)
        return () => listeners.delete(listener)
      }
    }
  } as unknown as DesktopPluginHost
  stopFeed = startPlanFeed(host)
  return { host, notify: (params: unknown) => listeners.forEach((listener) => listener(params)) }
}

function context(running: boolean): DesktopPluginTurnStatusContext {
  return { workspacePath: null, threadId: 'thread', turnId: 'turn', running }
}

afterEach(() => {
  stopFeed?.()
  stopFeed = null
})

describe('to-do list card', () => {
  it('restores the thread list and follows plan updates while the turn runs', async () => {
    const { host, notify } = fixtureHost({ todos: working })
    render(<TodoCard host={host} context={context(true)} />)
    expect(await screen.findByRole('region', { name: 'To-dos, 1 of 2 done' })).toBeInTheDocument()

    act(() => notify({ threadId: 'thread', todos: finished }))
    expect(screen.getByRole('region', { name: 'To-dos, 2 of 2 done' })).toBeInTheDocument()
  })

  it('stays after the turn ends only while steps remain', async () => {
    const { host, notify } = fixtureHost({ todos: working })
    render(<TodoCard host={host} context={context(false)} />)
    expect(await screen.findByRole('region')).toBeInTheDocument()

    act(() => notify({ threadId: 'thread', todos: finished }))
    expect(screen.queryByRole('region')).not.toBeInTheDocument()
  })

  it('leaves an unstarted plan to its plan card', async () => {
    const { host } = fixtureHost({ title: 'Plan', todos: [{ content: 'Read the spec', status: 'pending' }] })
    render(<TodoCard host={host} context={context(true)} />)
    await waitFor(() => expect(host.appServer.request).toHaveBeenCalled())
    expect(screen.queryByRole('region')).not.toBeInTheDocument()
  })
})
