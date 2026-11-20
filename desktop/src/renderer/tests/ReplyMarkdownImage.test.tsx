import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { AssistantMessageContent } from '../components/conversation/AssistantMessageContent'
import { ImageReadScopeContext, type ImageReadScope } from '../components/conversation/imagePreview/galleryImages'
import { LocaleProvider } from '../contexts/LocaleContext'

const workspacePath = '/workspace/project'

function renderReply(text: string, scope: ImageReadScope = { workspacePath }) {
  return render(
    <LocaleProvider loadSettings={false}>
      <ImageReadScopeContext.Provider value={scope}>
        <AssistantMessageContent text={text} streaming={false} />
      </ImageReadScopeContext.Provider>
    </LocaleProvider>
  )
}

describe('local images in assistant replies', () => {
  it('loads workspace-relative images through the viewer and falls back to alt text on failure', () => {
    renderReply('![Composer](.craft/attachments/images/shot%201.jpg)')
    const image = screen.getByRole('img', { name: 'Composer' })
    expect(image).toHaveAttribute(
      'src',
      `dotcraft-viewer://workspace${workspacePath}/.craft/attachments/images/shot%201.jpg?workspaceImage=${encodeURIComponent(workspacePath)}`,
    )

    fireEvent.error(image)
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
    expect(screen.getByText('Composer')).toBeInTheDocument()
  })

  it('does not interpret a remote reply path as a local workspace file', () => {
    renderReply('![Remote screenshot](shots/result.jpg)', {
      workspacePath,
      source: { connectionKey: 'remote:fixture', connectionId: 'host' },
    })
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
    expect(screen.getByText('Remote screenshot')).toBeInTheDocument()
  })

  it('does not request images outside the reply workspace', () => {
    renderReply('![Outside](%2e%2e/other/shot.jpg)')
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
    expect(screen.getByText('Outside')).toBeInTheDocument()
  })
})
