import './setupPluginRuntime'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { DesktopPluginHost } from '@dotcraft/plugin'
import { installDesktopApiMock } from './desktopApiMock'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConversationPanel } from '../components/layout/ConversationPanel'
import { RequestUserInputComposer } from '../components/conversation/RequestUserInputComposer'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore, type PendingUserInputRequest } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useUserInputAutoResolutionStore } from '../stores/userInputAutoResolutionStore'
import {
  clearDesktopPluginRegistry,
  registerDesktopPluginSurface
} from '../plugins/desktopPluginRegistry'

const sendServerResponse = vi.fn()
const cancelServerRequest = vi.fn()
const snoozeUserInputAutoResolution = vi.fn()
const sendRequest = vi.fn()

function request(overrides: Partial<PendingUserInputRequest> = {}): PendingUserInputRequest {
  return {
    bridgeId: 'bridge-input',
    threadId: 'thread-1',
    requestId: 'req-1',
    turnId: 'turn-1',
    isBlocking: false,
    questions: [
      {
        id: 'provider_id_handling',
        header: 'Provider ID',
        question: 'Should users handle the provider id directly?',
        isOther: true,
        options: [
          {
            label: 'Auto-generate (Recommended)',
            description: 'DotCraft creates a stable id.'
          },
          {
            label: 'Required',
            description: 'Users must type the id explicitly.'
          }
        ]
      }
    ],
    ...overrides
  }
}

function multiQuestionRequest(): PendingUserInputRequest {
  return request({
    questions: [
      {
        id: 'first',
        header: 'First',
        question: 'First question?',
        isOther: true,
        options: [
          { label: 'A1 (Recommended)', description: 'Pick A1.' },
          { label: 'B1', description: 'Pick B1.' }
        ]
      },
      {
        id: 'second',
        header: 'Second',
        question: 'Second question?',
        isOther: true,
        options: [
          { label: 'A2 (Recommended)', description: 'Pick A2.' },
          { label: 'B2', description: 'Pick B2.' }
        ]
      },
      {
        id: 'third',
        header: 'Third',
        question: 'Third question?',
        isOther: true,
        options: [
          { label: 'A3 (Recommended)', description: 'Pick A3.' },
          { label: 'B3', description: 'Pick B3.' }
        ]
      }
    ]
  })
}

function renderWithLocale(node: JSX.Element): void {
  render(<LocaleProvider>{node}</LocaleProvider>)
}

describe('RequestUserInputComposer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    clearDesktopPluginRegistry()
    sendServerResponse.mockResolvedValue({})
    cancelServerRequest.mockResolvedValue({})
    snoozeUserInputAutoResolution.mockResolvedValue({})
    sendRequest.mockResolvedValue({})
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
      appServer: {
        sendRequest,
        sendServerResponse,
        cancelServerRequest,
        snoozeUserInputAutoResolution
      },
      file: { readFile: vi.fn().mockResolvedValue('{}') },
      shell: { listOpenTargets: vi.fn().mockResolvedValue([]) },
      workspace: { saveImageToTemp: vi.fn() }
    })

    useConversationStore.getState().reset()
    useConnectionStore.getState().reset()
    useThreadStore.getState().reset()
    useUIStore.setState({
      activeMainView: 'conversation',
      planApprovalDismissed: {}
    })
    useUserInputAutoResolutionStore.getState().replace([])
  })

  afterEach(() => {
    clearDesktopPluginRegistry()
  })

  it('reports user input as busy without misclassifying it as an approval', async () => {
    const pending = request()
    useConnectionStore.setState({ status: 'connected' })
    useThreadStore.setState({
      activeThreadId: 'thread-1',
      activeThread: {
        id: 'thread-1',
        userId: 'local',
        workspacePath: 'X:\\fixtures\\workspace',
        displayName: 'Question thread',
        status: 'active',
        originChannel: 'dotcraft-desktop',
        metadata: {},
        createdAt: new Date().toISOString(),
        lastActiveAt: new Date().toISOString(),
        turns: []
      },
      loading: false
    })
    useConversationStore.setState({
      turnStatus: 'waitingInput',
      pendingUserInput: pending
    })
    registerDesktopPluginSurface(
      'fixture.user-input-context',
      {
        plugin: {
          id: 'fixture.user-input-context',
          version: '1.0.0',
          displayName: 'Fixture user input context'
        }
      } as DesktopPluginHost,
      'composer',
      'replace',
      ({ context }) => (
        <div
          data-testid="user-input-composer-context"
          data-busy={String(context.busy)}
          data-awaiting-approval={String(context.awaitingApproval)}
        />
      )
    )

    renderWithLocale(<ConversationPanel workspacePath="X:\\fixtures\\workspace" />)

    expect(await screen.findByTestId('user-input-composer-context')).toHaveAttribute('data-busy', 'true')
    expect(screen.getByTestId('user-input-composer-context')).toHaveAttribute('data-awaiting-approval', 'false')
  })

  it('submits the selected option after ArrowDown then Enter', async () => {
    useConversationStore.setState({ turnStatus: 'waitingInput', pendingUserInput: request() })
    renderWithLocale(<RequestUserInputComposer request={request()} />)

    fireEvent.keyDown(window, { key: 'ArrowDown' })
    fireEvent.keyDown(window, { key: 'Enter' })

    await waitFor(() => {
      expect(sendServerResponse).toHaveBeenCalledWith('bridge-input', {
        answers: {
          provider_id_handling: {
            answers: ['Required']
          }
        }
      })
    })
  })

  it('notifies after the server accepts the user input response', async () => {
    const onResponseAccepted = vi.fn()
    renderWithLocale(
      <RequestUserInputComposer request={request()} onResponseAccepted={onResponseAccepted} />
    )

    fireEvent.keyDown(window, { key: 'ArrowDown' })
    fireEvent.keyDown(window, { key: 'Enter' })

    await waitFor(() => {
      expect(sendServerResponse).toHaveBeenCalledWith('bridge-input', {
        answers: {
          provider_id_handling: {
            answers: ['Required']
          }
        }
      })
    })
    expect(onResponseAccepted).toHaveBeenCalledTimes(1)
  })

  it('dismisses with Escape and returns empty answers', async () => {
    renderWithLocale(<RequestUserInputComposer request={request()} />)

    fireEvent.keyDown(window, { key: 'Escape' })

    await waitFor(() => {
      expect(sendServerResponse).toHaveBeenCalledWith('bridge-input', { answers: {} })
    })
  })

  it('interrupts the turn when a blocking request is dismissed', async () => {
    const blocking = request({ isBlocking: true })
    useConversationStore.setState({
      turnStatus: 'waitingInput',
      activeTurnId: 'turn-1',
      pendingUserInput: blocking
    })
    renderWithLocale(<RequestUserInputComposer request={blocking} />)

    fireEvent.keyDown(window, { key: 'Escape' })

    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('turn/interrupt', {
        threadId: 'thread-1',
        turnId: 'turn-1'
      })
      expect(cancelServerRequest).toHaveBeenCalledWith('bridge-input')
    })
    expect(sendServerResponse).not.toHaveBeenCalled()
  })

  it('submits Other as a user note', async () => {
    renderWithLocale(<RequestUserInputComposer request={request()} />)

    fireEvent.click(screen.getByRole('button', { name: '3. Other' }))
    const textbox = await screen.findByPlaceholderText('No, and tell DotCraft what to do differently')
    expect(textbox.tagName).toBe('INPUT')
    fireEvent.change(textbox, { target: { value: 'Use a fixed template' } })
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(sendServerResponse).toHaveBeenCalledWith('bridge-input', {
        answers: {
          provider_id_handling: {
            answers: ['user_note: Use a fixed template']
          }
        }
      })
    })
  })

  it('preserves selected options and Other text when moving between questions', async () => {
    renderWithLocale(<RequestUserInputComposer request={multiQuestionRequest()} />)

    fireEvent.click(screen.getByRole('button', { name: '3. Other' }))
    const firstInput = screen.getByPlaceholderText('No, and tell DotCraft what to do differently')
    fireEvent.change(firstInput, { target: { value: '写代码' } })

    fireEvent.keyDown(window, { key: 'ArrowRight' })
    fireEvent.click(screen.getByRole('button', { name: '2. B2' }))
    fireEvent.keyDown(window, { key: 'ArrowLeft' })

    const restoredInput = screen.getByPlaceholderText('No, and tell DotCraft what to do differently')
    expect(restoredInput).toHaveValue('写代码')

    fireEvent.keyDown(window, { key: 'ArrowRight' })
    fireEvent.keyDown(window, { key: 'Enter' })
    fireEvent.keyDown(window, { key: 'Enter' })

    await waitFor(() => {
      expect(sendServerResponse).toHaveBeenCalledWith('bridge-input', {
        answers: {
          first: { answers: ['user_note: 写代码'] },
          second: { answers: ['B2'] },
          third: { answers: ['A3 (Recommended)'] }
        }
      })
    })
  })

})
