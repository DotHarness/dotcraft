// @vitest-environment jsdom
import { describe, it, expect, beforeAll, beforeEach, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import type { ComponentProps } from 'react'
import { MarkdownRenderer } from '../components/conversation/MarkdownRenderer'
import { sanitizeMermaidSvg } from '../components/conversation/mermaidSanitize'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConversationStore } from '../stores/conversationStore'
import { useOpenTargetsStore } from '../stores/openTargetsStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { installDesktopApiMock } from './desktopApiMock'

const mermaidMock = vi.hoisted(() => ({
  initialize: vi.fn(),
  render: vi.fn()
}))

vi.mock('mermaid', () => ({
  default: mermaidMock
}))

const openExternal = vi.fn()
const authorizeFile = vi.fn()
const classify = vi.fn()
const settingsSet = vi.fn()
const shellListOpenTargets = vi.fn()
const shellOpenInTarget = vi.fn()
const shellRevealLocalPath = vi.fn()
const clipboardWriteText = vi.fn()

beforeAll(() => {
  installDesktopApiMock({
      settings: {
        get: () => Promise.resolve({ locale: 'en' }),
        set: settingsSet
      },
      workspace: {
        viewer: {
          authorizeFile,
          classify
        }
      },
      shell: {
        openExternal,
        listOpenTargets: shellListOpenTargets,
        openInTarget: shellOpenInTarget,
        revealLocalPath: shellRevealLocalPath
      }
    })
  Object.defineProperty(navigator, 'clipboard', {
    configurable: true,
    value: { writeText: clipboardWriteText }
  })
})

describe('MarkdownRenderer', () => {
  beforeEach(() => {
    openExternal.mockReset()
    authorizeFile.mockReset()
    classify.mockReset()
    window.getSelection()?.removeAllRanges()
    settingsSet.mockReset()
    useOpenTargetsStore.setState({ status: 'idle', targets: [], preferences: undefined })
    shellListOpenTargets.mockResolvedValue([
      { id: 'fileManager', label: 'File Explorer', labelKey: 'openTargets.fileManager', iconKey: 'fileManager' },
      { id: 'cursor', label: 'Cursor', iconKey: 'editor' }
    ])
    shellOpenInTarget.mockReset()
    shellRevealLocalPath.mockReset()
    clipboardWriteText.mockReset()
    clipboardWriteText.mockResolvedValue(undefined)
    authorizeFile.mockImplementation(async ({ absolutePath }: { absolutePath: string }) => ({ absolutePath }))
    classify.mockResolvedValue({ contentClass: 'pdf', mime: 'application/pdf', sizeBytes: 100 })
    useConversationStore.getState().reset()
    useConversationStore.setState({ workspacePath: 'F:/workspace' })
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    useViewerTabStore.setState({
      byThread: new Map(),
      currentThreadId: 'thread-1',
      currentWorkspacePath: 'F:/workspace'
    })
    useUIStore.setState({
      activeDetailTab: { kind: 'system', id: 'changes' },
      detailPanelPreferredVisible: false,
      detailPanelVisible: false
    })
    document.documentElement.removeAttribute('data-theme')
    document.documentElement.removeAttribute('style')
    mermaidMock.initialize.mockReset()
    mermaidMock.render.mockReset()
    mermaidMock.render.mockResolvedValue({
      svg: '<svg xmlns="http://www.w3.org/2000/svg"><text>Rendered Mermaid</text></svg>',
      diagramType: 'flowchart-v2'
    })
  })

  function renderWithLocale(
    content: string,
    props?: Partial<ComponentProps<typeof MarkdownRenderer>>
  ): ReturnType<typeof render> {
    return render(
      <LocaleProvider>
        <MarkdownRenderer content={content} {...props} />
      </LocaleProvider>
    )
  }

  it('turns <br> into a line break and shows other raw HTML as text', () => {
    const { container } = renderWithLocale('| A |\n|---|\n| one<br>two |\n\n<details><summary>Answer</summary>3</details>')
    expect(container.querySelector('td br')).not.toBeNull()
    expect(container.querySelector('details')).toBeNull()
    expect(container.textContent).toContain('<details><summary>Answer</summary>3</details>')
  })

  it('sanitizes mermaid SVG output before insertion', async () => {
    mermaidMock.render.mockResolvedValueOnce({
      svg: [
        '<svg xmlns="http://www.w3.org/2000/svg" onload="alert(1)">',
        '<script>alert(1)</script>',
        '<a href="javascript:alert(1)" target="_blank"><text>Unsafe label</text></a>',
        '<use xlink:href="https://example.com/icon.svg"></use>',
        '</svg>'
      ].join(''),
      diagramType: 'flowchart-v2'
    })
    const { container } = renderWithLocale('```mermaid\nflowchart TD\n  A-->B\n```')

    expect(await screen.findByTestId('mermaid-diagram')).toHaveTextContent('Unsafe label')
    expect(container.querySelector('script')).toBeNull()
    expect(container.querySelector('a')).toBeNull()
    expect(container.querySelector('[onload]')).toBeNull()
    expect(container.innerHTML).not.toContain('javascript:')
    expect(container.innerHTML).not.toContain('https://example.com/icon.svg')
  })

  it('removes executable and external-link SVG content in the sanitizer helper', () => {
    const clean = sanitizeMermaidSvg([
      '<svg xmlns="http://www.w3.org/2000/svg" onclick="alert(1)">',
      '<script>alert(1)</script>',
      '<a href="https://example.com" target="_blank"><text>Label</text></a>',
      '<use href="https://example.com/icon.svg"></use>',
      '</svg>'
    ].join(''))
    const wrapper = document.createElement('div')
    wrapper.innerHTML = clean

    expect(wrapper.textContent).toContain('Label')
    expect(wrapper.querySelector('script')).toBeNull()
    expect(wrapper.querySelector('a')).toBeNull()
    expect(wrapper.querySelector('[onclick]')).toBeNull()
    expect(wrapper.innerHTML).not.toContain('https://example.com')
  })

  it('preserves sanitized Mermaid HTML labels inside foreignObject nodes', () => {
    const clean = sanitizeMermaidSvg([
      '<svg xmlns="http://www.w3.org/2000/svg">',
      '<foreignObject width="160" height="48">',
      '<div xmlns="http://www.w3.org/1999/xhtml" onclick="alert(1)">',
      '<span class="nodeLabel">SampleTracer.Inject<br/>sampleName</span>',
      '<script>alert(1)</script>',
      '<a href="javascript:alert(1)" target="_blank">unsafe link</a>',
      '</div>',
      '</foreignObject>',
      '</svg>'
    ].join(''))
    const wrapper = document.createElement('div')
    wrapper.innerHTML = clean

    expect(wrapper.textContent).toContain('SampleTracer.Inject')
    expect(wrapper.textContent).toContain('sampleName')
    expect(wrapper.querySelector('foreignObject')).not.toBeNull()
    expect(wrapper.querySelector('script')).toBeNull()
    expect(wrapper.querySelector('a')).toBeNull()
    expect(wrapper.querySelector('[onclick]')).toBeNull()
    expect(wrapper.innerHTML).not.toContain('javascript:')
  })

  it('does not open unsupported schemes in external link mode', () => {
    renderWithLocale('[Unsafe](javascript:alert(1))', { linkMode: 'external' })
    const anchor = screen.getByText('Unsafe').closest('a')
    expect(anchor).not.toBeNull()
    fireEvent.click(anchor!)
    expect(openExternal).not.toHaveBeenCalled()
  })

  it.each(['javascript:123', 'https:123', 'data:123', 'vbscript:123'])(
    'keeps numeric non-file schemes blocked in Markdown: %s', (target) => {
      renderWithLocale(`[unsafe](${target})`)
      const anchor = screen.getByText('unsafe').closest('a')!
      if (target !== 'https:123') expect(anchor.getAttribute('href')).toBe('')
      fireEvent.click(anchor, { detail: 1 })
      expect(authorizeFile).not.toHaveBeenCalled()
      expect(openExternal).not.toHaveBeenCalled()
      expect(useViewerTabStore.getState().getThreadState('thread-1').tabs).toHaveLength(0)
    }
  )

  it('does not relax the image URL sanitizer for bare file locations', () => {
    renderWithLocale('![image](Test.cpp:123)')
    expect(screen.getByRole('img', { name: 'image' }).getAttribute('src')).toBeNull()
  })
})
