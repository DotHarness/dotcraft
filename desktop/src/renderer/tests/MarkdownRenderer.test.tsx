// @vitest-environment jsdom
import { describe, it, expect, beforeAll, beforeEach, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { ComponentProps } from 'react'
import { THEME_CHANGED_EVENT } from '../../shared/theme'
import { MarkdownRenderer } from '../components/conversation/MarkdownRenderer'
import { sanitizeMermaidSvg } from '../components/conversation/mermaidSanitize'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConversationStore } from '../stores/conversationStore'
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
const shellListEditors = vi.fn()
const shellLaunchLocalPathInEditor = vi.fn()
const shellOpenLocalPath = vi.fn()
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
        listEditors: shellListEditors,
        launchLocalPathInEditor: shellLaunchLocalPathInEditor,
        openLocalPath: shellOpenLocalPath,
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
    settingsSet.mockReset()
    shellListEditors.mockResolvedValue([
      { id: 'explorer', labelKey: 'editors.explorer', iconKey: 'explorer' },
      { id: 'cursor', labelKey: 'editors.cursor', iconKey: 'editor-generic' }
    ])
    shellLaunchLocalPathInEditor.mockReset()
    shellOpenLocalPath.mockReset()
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

  function lastMermaidThemeVariables(): Record<string, unknown> {
    const calls = mermaidMock.initialize.mock.calls
    const config = calls[calls.length - 1]?.[0] as { themeVariables?: Record<string, unknown> } | undefined
    return config?.themeVariables ?? {}
  }

  function expectMermaidSafeColor(value: unknown): void {
    expect(value).toEqual(expect.any(String))
    expect(value).not.toMatch(/\b(?:color-mix|var)\(/i)
    expect(value).toMatch(/^(?:#[0-9a-f]{3,8}|rgba?\(|hsla?\(|[a-z]+$)/i)
  }

  it('renders plain text content', () => {
    const { container } = renderWithLocale('Hello world')
    expect(container.textContent).toContain('Hello world')
  })

  it('renders a heading', () => {
    renderWithLocale('# Main Title')
    const heading = document.querySelector('h1')
    expect(heading).not.toBeNull()
    expect(heading?.textContent).toContain('Main Title')
  })

  it('renders a subheading', () => {
    renderWithLocale('## Section')
    const heading = document.querySelector('h2')
    expect(heading).not.toBeNull()
    expect(heading?.textContent).toContain('Section')
  })

  it('renders an unordered list', () => {
    const { container } = renderWithLocale('- Item 1\n- Item 2\n- Item 3')
    const items = container.querySelectorAll('li')
    expect(items.length).toBe(3)
    expect(items[0].textContent).toContain('Item 1')
  })

  it('renders a fenced code block', () => {
    const content = '```typescript\nconst x = 1\n```'
    const { container } = renderWithLocale(content)
    const codeBlock = container.querySelector('pre')
    expect(codeBlock).not.toBeNull()
    expect(codeBlock?.textContent).toContain('const x = 1')
  })

  it('wraps fenced code blocks by default', () => {
    const longToken = 'example-token-with-no-natural-breaks-abcdefghijklmnopqrstuvwxyz0123456789'
    const { container } = renderWithLocale(`\`\`\`text\n${longToken}\n\`\`\``)
    const codeBlock = container.querySelector('pre')
    const wrapButton = screen.getByRole('button', { name: 'Disable word wrap' })

    expect(codeBlock).toHaveStyle({
      overflowX: 'hidden',
      whiteSpace: 'pre-wrap',
      overflowWrap: 'anywhere'
    })
    expect(wrapButton).toHaveAttribute('aria-pressed', 'true')
  })

  it('shows code block actions on hover or keyboard focus', () => {
    renderWithLocale('```text\ncontent\n```')
    const codeBlock = screen.getByTestId('markdown-code-block')
    const actions = screen.getByTestId('markdown-code-actions')
    const copyButton = screen.getByRole('button', { name: 'Copy code' })

    expect(actions).toHaveStyle({ opacity: 0, pointerEvents: 'none' })

    fireEvent.mouseEnter(codeBlock)
    expect(actions).toHaveStyle({ opacity: 1, pointerEvents: 'auto' })

    fireEvent.mouseLeave(codeBlock)
    expect(actions).toHaveStyle({ opacity: 0, pointerEvents: 'none' })

    fireEvent.focus(copyButton)
    expect(actions).toHaveStyle({ opacity: 1, pointerEvents: 'auto' })

    fireEvent.blur(copyButton, { relatedTarget: document.body })
    expect(actions).toHaveStyle({ opacity: 0, pointerEvents: 'none' })
  })

  it('toggles word wrap independently for each code block', () => {
    const content = [
      '```text',
      'first-long-line',
      '```',
      '',
      '```text',
      'second-long-line',
      '```'
    ].join('\n')
    const { container } = renderWithLocale(content)
    const codeBlocks = container.querySelectorAll('pre')
    const wrapButtons = screen.getAllByRole('button', { name: 'Disable word wrap' })

    fireEvent.click(wrapButtons[0])

    expect(codeBlocks[0]).toHaveStyle({
      overflowX: 'auto',
      whiteSpace: 'pre',
      overflowWrap: 'normal'
    })
    expect(codeBlocks[1]).toHaveStyle({
      overflowX: 'hidden',
      whiteSpace: 'pre-wrap',
      overflowWrap: 'anywhere'
    })
    const enableButton = screen.getByRole('button', { name: 'Enable word wrap' })
    expect(enableButton).toHaveAttribute('aria-pressed', 'false')

    fireEvent.click(enableButton)

    expect(codeBlocks[0]).toHaveStyle({
      overflowX: 'hidden',
      whiteSpace: 'pre-wrap',
      overflowWrap: 'anywhere'
    })
    expect(screen.getAllByRole('button', { name: 'Disable word wrap' })).toHaveLength(2)
  })

  it('copies the complete code block and exposes localized icon-button feedback', async () => {
    renderWithLocale('```text\nfirst line\nsecond line\n```')
    const copyButton = screen.getByRole('button', { name: 'Copy code' })

    act(() => copyButton.focus())
    expect(copyButton).toHaveFocus()
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Copy code')

    fireEvent.click(copyButton)

    await waitFor(() => {
      expect(clipboardWriteText).toHaveBeenCalledWith('first line\nsecond line\n')
    })
    expect(await screen.findByRole('button', { name: 'Code copied' })).toBeInTheDocument()
  })

  it('renders a fenced mermaid block as a diagram', async () => {
    renderWithLocale('```mermaid\nflowchart TD\n  A-->B\n```')

    expect(await screen.findByTestId('mermaid-diagram')).toHaveTextContent('Rendered Mermaid')
    expect(document.querySelector('pre')).toBeNull()
    expect(mermaidMock.initialize).toHaveBeenCalledWith(expect.objectContaining({
      startOnLoad: false,
      securityLevel: 'antiscript',
      htmlLabels: true,
      theme: 'base',
      flowchart: expect.objectContaining({ useMaxWidth: false })
    }))
    expect(mermaidMock.render).toHaveBeenCalledWith(
      expect.stringMatching(/^dc-mermaid-/),
      'flowchart TD\n  A-->B',
      expect.any(Element)
    )
  })

  it('keeps ordinary code blocks on the existing code renderer', () => {
    renderWithLocale('```typescript\nconst x = 1\n```')

    expect(screen.getByRole('button', { name: 'Copy code' })).toBeInTheDocument()
    expect(mermaidMock.render).not.toHaveBeenCalled()
  })

  it('copies mermaid source from rendered diagrams', async () => {
    renderWithLocale('```mmd\nsequenceDiagram\n  A->>B: Hello\n```')

    fireEvent.click(await screen.findByRole('button', { name: 'Copy Mermaid source' }))

    await waitFor(() => {
      expect(clipboardWriteText).toHaveBeenCalledWith(expect.stringContaining('sequenceDiagram'))
      expect(clipboardWriteText).toHaveBeenCalledWith(expect.stringContaining('A->>B: Hello'))
    })
  })

  it('falls back to the original code block when mermaid render fails', async () => {
    mermaidMock.render.mockRejectedValueOnce(new Error('Invalid diagram'))
    renderWithLocale('```mermaid\nflowchart TD\n  A-->B\n```')

    expect(await screen.findByText('Unable to render Mermaid diagram. Showing source instead.')).toBeInTheDocument()
    const codeBlock = document.querySelector('pre')
    expect(codeBlock).not.toBeNull()
    expect(codeBlock?.textContent).toContain('flowchart TD')
    expect(screen.getByRole('button', { name: 'Disable word wrap' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Copy code' })).toBeInTheDocument()
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

  it('normalizes color-mix theme tokens before rendering mermaid', async () => {
    document.documentElement.style.setProperty('--text-secondary', 'color-mix(in srgb, #eeeeec 66%, #141515)')
    document.documentElement.style.setProperty('--border-active', 'color-mix(in srgb, var(--text-primary) 18%, transparent)')
    document.documentElement.style.setProperty('--border-default', 'var(--border-active)')
    mermaidMock.render.mockImplementationOnce(async () => {
      const variables = lastMermaidThemeVariables()
      for (const key of ['lineColor', 'nodeBorder', 'clusterBorder']) {
        const value = variables[key]
        if (typeof value === 'string' && /\b(?:color-mix|var)\(/i.test(value)) {
          throw new Error(`Unsupported color format: "${value}"`)
        }
      }

      return {
        svg: '<svg xmlns="http://www.w3.org/2000/svg"><text>Rendered Mermaid</text></svg>',
        diagramType: 'flowchart-v2'
      }
    })

    renderWithLocale('```mermaid\nflowchart TD\n  A-->B\n```')

    expect(await screen.findByTestId('mermaid-diagram')).toHaveTextContent('Rendered Mermaid')
    const variables = lastMermaidThemeVariables()
    expectMermaidSafeColor(variables.lineColor)
    expectMermaidSafeColor(variables.nodeBorder)
    expectMermaidSafeColor(variables.clusterBorder)
  })

  it('quotes punctuation-heavy flowchart labels before rendering mermaid', async () => {
    mermaidMock.render.mockImplementationOnce(async (_id, source: string) => {
      if (source.includes('V[SampleTracer.Inject')) {
        throw new Error('Parse error on line 32: got PS')
      }
      if (source.includes("markerToMethod['sample:Type.Method']=callee]")) {
        throw new Error('Parse error on line 36: inner bracket closed the node')
      }

      return {
        svg: '<svg xmlns="http://www.w3.org/2000/svg"><text>Rendered Mermaid</text></svg>',
        diagramType: 'flowchart-v2'
      }
    })

    renderWithLocale([
      '```mermaid',
      'flowchart TD',
      "  U --> V[SampleTracer.Inject(callee,<br/>sampleName='sample:Type.Method')]",
      "  V --> W[记录 SampleTraceInfo<br/>SampleCache.SetLevel<br/>markerToMethod['sample:Type.Method']=callee]",
      '  W --> R{level + 1 <= MaxLevel?}',
      '```'
    ].join('\n'))

    expect(await screen.findByTestId('mermaid-diagram')).toHaveTextContent('Rendered Mermaid')
    const renderedSource = mermaidMock.render.mock.calls.at(-1)?.[1] as string
    expect(renderedSource).toContain('V["SampleTracer.Inject(callee,<br/>sampleName=\'sample:Type.Method\')"]')
    expect(renderedSource).toContain('W["记录 SampleTraceInfo<br/>SampleCache.SetLevel<br/>markerToMethod[\'sample:Type.Method\']=callee"]')
    expect(renderedSource).toContain('R{"level + 1 <= MaxLevel?"}')
  })

  it('keeps mermaid temporary DOM mutations outside the React-rendered frame', async () => {
    mermaidMock.render.mockImplementationOnce(async (_id, _source, container: Element | undefined) => {
      container?.replaceChildren(document.createElement('svg'))
      return {
        svg: '<svg xmlns="http://www.w3.org/2000/svg"><text>Rendered Mermaid</text></svg>',
        diagramType: 'flowchart-v2'
      }
    })

    expect(() => {
      renderWithLocale('```mermaid\nflowchart TD\n  A-->B\n```')
    }).not.toThrow()

    expect(await screen.findByTestId('mermaid-diagram')).toHaveTextContent('Rendered Mermaid')
    expect(screen.getByRole('button', { name: 'Copy Mermaid source' })).toBeInTheDocument()
  })

  it('uses current theme tokens when rendering mermaid and rerenders after theme changes', async () => {
    document.documentElement.style.setProperty('--bg-secondary', 'rgb(1, 2, 3)')
    document.documentElement.style.setProperty('--text-secondary', 'color-mix(in srgb, #1a1c1f 70%, #f9f9f9)')
    renderWithLocale('```mermaid\nflowchart TD\n  A-->B\n```')

    await waitFor(() => {
      expect(mermaidMock.render).toHaveBeenCalledTimes(1)
    })
    expect(mermaidMock.initialize).toHaveBeenLastCalledWith(expect.objectContaining({
      themeVariables: expect.objectContaining({
        primaryColor: 'rgb(1, 2, 3)',
        darkMode: false
      })
    }))
    expectMermaidSafeColor(lastMermaidThemeVariables().lineColor)

    act(() => {
      document.documentElement.setAttribute('data-theme', 'dark')
      window.dispatchEvent(new CustomEvent(THEME_CHANGED_EVENT, { detail: { mode: 'dark' } }))
    })

    await waitFor(() => {
      expect(mermaidMock.render).toHaveBeenCalledTimes(2)
    })
    expect(mermaidMock.initialize).toHaveBeenLastCalledWith(expect.objectContaining({
      themeVariables: expect.objectContaining({ darkMode: true })
    }))
    expectMermaidSafeColor(lastMermaidThemeVariables().lineColor)
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

  it('renders inline code', () => {
    const { container } = renderWithLocale('Use `npm install` to install.')
    const code = container.querySelector('code')
    expect(code).not.toBeNull()
    expect(code?.textContent).toContain('npm install')
  })

  it('marks markdown body for trailing block margin trim', () => {
    const { container } = renderWithLocale('Only paragraph')
    const markdownBody = container.querySelector('.markdown-body')
    const lastBlock = container.querySelector('.markdown-body > :last-child')

    expect(markdownBody).not.toBeNull()
    expect(lastBlock).not.toBeNull()
  })

  it('keeps long inline code and paths inline when overflow containment is enabled', () => {
    const longPath = 'Library/PackageCache/com.example.mock-long-package@0.0.0/FakeDependency.dll'
    const longToken = 'example-token-with-no-natural-breaks-abcdefghijklmnopqrstuvwxyz0123456789'
    const { container } = renderWithLocale(
      `Keep \`${longPath}\` and \`${longToken}\` in assumptions.`,
      { containOverflow: true }
    )

    const inlineCode = container.querySelectorAll('p code')
    expect(inlineCode).toHaveLength(2)
    expect(container.querySelector('pre')).toBeNull()
    expect(inlineCode[0].textContent).toBe(longPath)
    expect(inlineCode[1].textContent).toBe(longToken)
  })

  it('renders a GFM table', () => {
    const tableMarkdown = [
      '| Name | Value |',
      '|------|-------|',
      '| foo  | bar   |'
    ].join('\n')
    const { container } = renderWithLocale(tableMarkdown)
    const table = container.querySelector('table')
    expect(table).not.toBeNull()
    expect(container.textContent).toContain('foo')
    expect(container.textContent).toContain('bar')
  })

  it('renders a link with onClick (no href navigation)', () => {
    renderWithLocale('[DotCraft](https://example.com)')
    const link = screen.getByRole('link', { name: /dotcraft/i })
    expect(link).toBeDefined()
    expect(link.getAttribute('href')).toBe('https://example.com')
  })

  it('opens http links externally in external link mode', () => {
    renderWithLocale('[DotCraft](https://example.com/docs)', { linkMode: 'external' })
    fireEvent.click(screen.getByRole('link', { name: /dotcraft/i }))
    expect(openExternal).toHaveBeenCalledWith('https://example.com/docs')
  })

  it('does not open unsupported schemes in external link mode', () => {
    renderWithLocale('[Unsafe](javascript:alert(1))', { linkMode: 'external' })
    const anchor = screen.getByText('Unsafe').closest('a')
    expect(anchor).not.toBeNull()
    fireEvent.click(anchor!)
    expect(openExternal).not.toHaveBeenCalled()
  })

  it('renders file links as inline reference pills', async () => {
    renderWithLocale('[./docs/guide.md](./docs/guide.md)')
    const link = screen.getByRole('link', { name: /guide\.md/i })
    expect(link).toHaveAttribute('data-inline-reference-kind', 'file')
    expect(link).not.toHaveAttribute('title')

    fireEvent.mouseEnter(link.parentElement as HTMLElement)
    expect(await screen.findByRole('tooltip')).toHaveTextContent('./docs/guide.md')
  })

  it('copies the resolved path from a markdown file pill context menu', async () => {
    renderWithLocale('[./docs/guide.md](./docs/guide.md)')
    const link = screen.getByRole('link', { name: /guide\.md/i })

    fireEvent.contextMenu(link, { clientX: 18, clientY: 22 })
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Copy path' }))

    await waitFor(() => {
      expect(clipboardWriteText).toHaveBeenCalledWith('F:/workspace/docs/guide.md')
    })
  })

  it('does not show the local path menu for http links', () => {
    renderWithLocale('[docs](https://example.com/docs)')
    fireEvent.contextMenu(screen.getByRole('link', { name: /docs/i }), { clientX: 18, clientY: 22 })

    expect(screen.queryByRole('menuitem', { name: 'Copy path' })).toBeNull()
  })

  it('opens absolute local file links in the internal viewer', async () => {
    renderWithLocale('[report](file:///D:/docs/report.pdf)')
    fireEvent.click(screen.getByRole('link', { name: /report/i }))

    await waitFor(() => {
      expect(authorizeFile).toHaveBeenCalledWith({ absolutePath: 'D:/docs/report.pdf' })
      expect(classify).toHaveBeenCalledWith({ absolutePath: 'D:/docs/report.pdf' })
    })
    const activeTab = useUIStore.getState().activeDetailTab
    expect(activeTab.kind).toBe('viewer')
    if (activeTab.kind === 'viewer') {
      const tab = useViewerTabStore.getState().getThreadState('thread-1').tabs.find((entry) => entry.id === activeTab.id)
      expect(tab).toMatchObject({
        kind: 'file',
        absolutePath: 'D:/docs/report.pdf',
        contentClass: 'pdf'
      })
    }
  })

  it('shortens raw browser links into readable labels', () => {
    renderWithLocale('[https://docs.example.com/start](https://docs.example.com/start)')
    const link = screen.getByRole('link', { name: /docs\.example\.com\/start/i })
    expect(link).toHaveAttribute('data-inline-reference-kind', 'browser')
    expect(link.getAttribute('href')).toBe('https://docs.example.com/start')
  })

  it('renders bold and italic text', () => {
    const { container } = renderWithLocale('**bold** and _italic_')
    expect(container.querySelector('strong')?.textContent).toContain('bold')
    expect(container.querySelector('em')?.textContent).toContain('italic')
  })

  it('memoizes: does not re-render when content unchanged', () => {
    const { rerender, container } = renderWithLocale('Static text')
    const firstHTML = container.innerHTML
    rerender(
      <LocaleProvider>
        <MarkdownRenderer content="Static text" />
      </LocaleProvider>
    )
    expect(container.innerHTML).toBe(firstHTML)
  })
})
