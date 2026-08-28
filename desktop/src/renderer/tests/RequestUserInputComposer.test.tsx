import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { DesktopPluginHost } from '@dotcraft/plugin'
import { installDesktopApiMock } from './desktopApiMock'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConversationPanel } from '../components/layout/ConversationPanel'
import { RequestUserInputComposer } from '../components/conversation/RequestUserInputComposer'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore, type PendingUserInputRequest } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import {
  clearDesktopPluginRegistry,
  registerDesktopPluginSurface
} from '../plugins/desktopPluginRegistry'

const sendServerResponse = vi.fn()

function request(overrides: Partial<PendingUserInputRequest> = {}): PendingUserInputRequest {
  return {
    bridgeId: 'bridge-input',
    requestId: 'req-1',
    turnId: 'turn-1',
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
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
      appServer: {
        sendRequest: vi.fn().mockResolvedValue({}),
        sendServerResponse
      },
      file: { readFile: vi.fn().mockResolvedValue('{}') },
      shell: { listEditors: vi.fn().mockResolvedValue([]) },
      workspace: { saveImageToTemp: vi.fn() }
    })

    useConversationStore.getState().reset()
    useConnectionStore.getState().reset()
    useThreadStore.getState().reset()
    useUIStore.setState({
      activeMainView: 'conversation',
      planApprovalDismissed: {}
    })
  })

  afterEach(() => {
    clearDesktopPluginRegistry()
  })

  it('keeps decision pose while inheriting composer mascot effects', () => {
    const { container } = render(
      <LocaleProvider>
        <RequestUserInputComposer
          request={request()}
          mascotEffectState={{ reasoningEffort: 'high', speed: 'fast', contextMax: true }}
        />
      </LocaleProvider>
    )

    const mascot = container.querySelector('[data-mascot-effort]')
    expect(mascot).toHaveAttribute('data-mascot-effort', 'high')
    expect(mascot).toHaveAttribute('data-mascot-speed', 'fast')
    expect(mascot).toHaveAttribute('data-mascot-context', 'max')
    expect(mascot).toHaveClass('composer-mascot-hold-sign')
  })

  it('renders in ConversationPanel instead of the normal composer', async () => {
    const pending = request()
    useConnectionStore.setState({
      status: 'connected',
      capabilities: { modelCatalogManagement: true, workspaceConfigManagement: true }
    })
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

    renderWithLocale(<ConversationPanel workspacePath="X:\\fixtures\\workspace" />)
    await act(async () => {
      await Promise.resolve()
    })

    expect(screen.getByText('Should users handle the provider id directly?')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Send message' })).not.toBeInTheDocument()
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

  it('shows the current question directly without generic asking copy', () => {
    renderWithLocale(<RequestUserInputComposer request={request()} />)

    expect(screen.getByText('Should users handle the provider id directly?')).toBeInTheDocument()
    expect(screen.queryByText('Asking')).not.toBeInTheDocument()
    expect(screen.queryByText('question', { exact: true })).not.toBeInTheDocument()
    expect(screen.queryByText('1 of 1')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Previous question' })).not.toBeInTheDocument()
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

  it('clicking an unselected option selects it, then clicking it again submits', async () => {
    renderWithLocale(<RequestUserInputComposer request={request()} />)

    fireEvent.click(screen.getByRole('button', { name: '2. Required' }))
    expect(sendServerResponse).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: '2. Required' }))

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

  it('clicking the already selected normal option advances through questions and submits on the last question', async () => {
    renderWithLocale(<RequestUserInputComposer request={multiQuestionRequest()} />)

    expect(screen.getByText('1 of 3')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Continue' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: '1. A1 (Recommended)' }))
    expect(screen.getByText('Second question?')).toBeInTheDocument()
    expect(screen.getByText('2 of 3')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Continue' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: '2. B2' }))
    expect(sendServerResponse).not.toHaveBeenCalled()
    expect(screen.getByText('Second question?')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: '2. B2' }))
    expect(screen.getByText('Third question?')).toBeInTheDocument()
    expect(screen.getByText('3 of 3')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Submit' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: '1. A3 (Recommended)' }))

    await waitFor(() => {
      expect(sendServerResponse).toHaveBeenCalledWith('bridge-input', {
        answers: {
          first: { answers: ['A1 (Recommended)'] },
          second: { answers: ['B2'] },
          third: { answers: ['A3 (Recommended)'] }
        }
      })
    })
  })

  it('dismisses with Escape and returns empty answers', async () => {
    renderWithLocale(<RequestUserInputComposer request={request()} />)

    fireEvent.keyDown(window, { key: 'Escape' })

    await waitFor(() => {
      expect(sendServerResponse).toHaveBeenCalledWith('bridge-input', { answers: {} })
    })
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

  it('clicking the selected Other row keeps focus in the input instead of advancing', () => {
    renderWithLocale(<RequestUserInputComposer request={multiQuestionRequest()} />)

    const otherRow = screen.getByRole('button', { name: '3. Other' })
    fireEvent.click(otherRow)
    const textbox = screen.getByPlaceholderText('No, and tell DotCraft what to do differently')
    fireEvent.change(textbox, { target: { value: 'Use my note' } })
    fireEvent.click(otherRow)

    expect(screen.getByText('First question?')).toBeInTheDocument()
    expect(textbox).toHaveFocus()
    expect(sendServerResponse).not.toHaveBeenCalled()
  })

  it('navigates multiple questions with header buttons and clamps at the ends', () => {
    renderWithLocale(<RequestUserInputComposer request={multiQuestionRequest()} />)

    const previous = screen.getByRole('button', { name: 'Previous question' })
    const next = screen.getByRole('button', { name: 'Next question' })
    expect(screen.getByText('1 of 3')).toBeInTheDocument()
    expect(previous).toBeDisabled()
    expect(next).not.toBeDisabled()

    fireEvent.click(next)
    expect(screen.getByText('Second question?')).toBeInTheDocument()
    expect(screen.getByText('2 of 3')).toBeInTheDocument()
    fireEvent.click(next)
    expect(screen.getByText('Third question?')).toBeInTheDocument()
    expect(screen.getByText('3 of 3')).toBeInTheDocument()
    expect(next).toBeDisabled()

    fireEvent.click(previous)
    expect(screen.getByText('Second question?')).toBeInTheDocument()
    expect(screen.getByText('2 of 3')).toBeInTheDocument()
  })

  it('uses ArrowLeft and ArrowRight for question navigation except inside the native input', () => {
    renderWithLocale(<RequestUserInputComposer request={multiQuestionRequest()} />)

    fireEvent.keyDown(window, { key: 'ArrowRight' })
    expect(screen.getByText('Second question?')).toBeInTheDocument()

    const textbox = screen.getByPlaceholderText('No, and tell DotCraft what to do differently')
    fireEvent.focus(textbox)
    fireEvent.keyDown(textbox, { key: 'ArrowRight' })
    expect(screen.getByText('Second question?')).toBeInTheDocument()

    fireEvent.keyDown(window, { key: 'ArrowLeft' })
    expect(screen.getByText('First question?')).toBeInTheDocument()
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

  it('shows option descriptions through an info tooltip', async () => {
    const longDescription =
      'Use ProfilerDriver.GetHierarchyFrameDataView(frame, threadIndex) and keep the full path X:\\fixtures\\workspace\\samples\\profiles\\capture.trace visible when the tooltip wraps.'
    renderWithLocale(<RequestUserInputComposer request={request({
      questions: [
        {
          id: 'provider_id_handling',
          header: 'Provider ID',
          question: 'Should users handle the provider id directly?',
          isOther: true,
          options: [
            {
              label: 'Auto-generate (Recommended)',
              description: longDescription
            },
            {
              label: 'Required',
              description: 'Users must type the id explicitly.'
            }
          ]
        }
      ]
    })} />)

    const icon = screen.getByRole('img', { name: /Why Auto-generate/ })
    fireEvent.focus(icon)

    const tooltip = await screen.findByRole('tooltip')
    expect(tooltip).toHaveAttribute('data-multiline', 'true')
    expect(within(tooltip).getByText(longDescription)).toBeInTheDocument()
  })
})
