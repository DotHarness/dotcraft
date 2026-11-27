import { beforeEach, describe, expect, it } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { ReferencePathContextMenu } from '../components/conversation/ReferencePathContextMenu'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useComposerFileReferenceStore } from '../stores/composerFileReferenceStore'
import { useConversationStore } from '../stores/conversationStore'
import { activeDetailScopeId } from '../utils/detailPanelScope'
import { installDesktopApiMock } from './desktopApiMock'

async function addToChat(targetPath: string, directory?: boolean): Promise<string[] | undefined> {
  render(
    <LocaleProvider loadSettings={false}>
      <ReferencePathContextMenu position={{ x: 0, y: 0 }} targetPath={targetPath} directory={directory} onClose={() => {}} />
    </LocaleProvider>
  )
  await act(async () => {})
  fireEvent.click(screen.getByText('Add to chat'))
  return useComposerFileReferenceStore.getState().pendingByScope.get(activeDetailScopeId())
}

describe('ReferencePathContextMenu add to chat', () => {
  beforeEach(() => {
    installDesktopApiMock({})
    useConversationStore.setState({ workspacePath: 'C:\\workspace', remoteWorkspaceActive: true })
    useComposerFileReferenceStore.setState({ pendingByScope: new Map() })
  })

  it('adds a file path unchanged', async () => {
    expect(await addToChat('C:\\workspace\\src\\a.ts')).toEqual(['C:\\workspace\\src\\a.ts'])
  })

  it('adds a directory as a folder reference', async () => {
    expect(await addToChat('C:\\workspace\\src\\', true)).toEqual(['C:\\workspace\\src/'])
  })
})
