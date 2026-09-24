import { Fragment, useMemo, useState, type HTMLAttributes } from 'react'
import { WrapText } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { CopyButton } from '../ui/CopyButton'
import { IconButton } from '../ui/IconButton'
import { LineSpans } from '../code/CodeSpans'
import {
  fileCacheKey,
  normalizeNewlines,
  resolveLanguage,
  splitLines,
  useFileHighlight
} from '../../highlight'
import { MermaidDiagram } from './MermaidDiagram'
import { extractText, getCodeBlockLanguage, isMermaidLanguage } from './markdownText'

export function CodeBlock({
  children,
  enableMermaid,
  ...props
}: HTMLAttributes<HTMLPreElement> & { enableMermaid?: boolean }): JSX.Element {
  const language = getCodeBlockLanguage(children)
  if (enableMermaid && language && isMermaidLanguage(language)) {
    return (
      <MermaidDiagram
        source={extractText(children)}
        fallback={<PlainCodeBlock {...props}>{children}</PlainCodeBlock>}
      />
    )
  }

  return <PlainCodeBlock {...props}>{children}</PlainCodeBlock>
}

export function PlainCodeBlock({
  children,
  style,
  ...props
}: HTMLAttributes<HTMLPreElement>): JSX.Element {
  const t = useT()
  const [wordWrap, setWordWrap] = useState(true)
  const [hovered, setHovered] = useState(false)
  const [focusedWithin, setFocusedWithin] = useState(false)

  const wrapLabel = t(wordWrap ? 'markdown.disableWordWrap' : 'markdown.enableWordWrap')
  const actionsVisible = hovered || focusedWithin

  return (
    <div
      data-testid="markdown-code-block"
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocusCapture={() => setFocusedWithin(true)}
      onBlurCapture={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) {
          setFocusedWithin(false)
        }
      }}
      style={{ position: 'relative', minWidth: 0, maxWidth: '100%', margin: '8px 0 10px' }}
    >
      <pre
        className="dc-code"
        style={{
          maxWidth: '100%',
          boxSizing: 'border-box',
          backgroundColor: 'var(--code-block-bg)',
          borderRadius: '10px',
          padding: '12px 14px',
          paddingRight: '72px',
          overflowX: wordWrap ? 'hidden' : 'auto',
          whiteSpace: wordWrap ? 'pre-wrap' : 'pre',
          overflowWrap: wordWrap ? 'anywhere' : 'normal',
          fontFamily: 'var(--font-mono)',
          fontSize: 'var(--text-code-size)',
          lineHeight: 'var(--text-code-line-height)',
          margin: 0,
          ...style
        }}
        {...props}
      >
        {children}
      </pre>
      <div
        data-testid="markdown-code-actions"
        style={{
          position: 'absolute',
          top: '6px',
          right: '8px',
          display: 'flex',
          alignItems: 'center',
          gap: '4px',
          opacity: actionsVisible ? 1 : 0,
          pointerEvents: actionsVisible ? 'auto' : 'none',
          transition: 'opacity 120ms ease'
        }}
      >
        <IconButton
          size={24}
          radius={6}
          icon={<WrapText size={14} aria-hidden />}
          label={wrapLabel}
          tooltipLabel={wrapLabel}
          tooltipPlacement="top"
          active={wordWrap}
          activeTone="neutral"
          aria-pressed={wordWrap}
          onClick={() => setWordWrap((current) => !current)}
        />
        <CopyButton
          getText={() => extractText(children)}
          label={t('markdown.copyCode')}
          copiedLabel={t('markdown.codeCopied')}
        />
      </div>
    </div>
  )
}

export interface HighlightedCodeProps extends HTMLAttributes<HTMLElement> {
  className?: string
}

/** Renders the fence's own text until a grammar is available, so a streaming message stays readable. */
export function HighlightedCode({ children, className, ...props }: HighlightedCodeProps): JSX.Element {
  const text = extractText(children)
  const label = /(?:^|\s)language-([^\s]+)/.exec(className ?? '')?.[1]?.toLowerCase()

  const request = useMemo(() => {
    const lang = resolveLanguage(label)
    if (lang === undefined || text.length === 0) return undefined
    return {
      cacheKey: fileCacheKey('fence', lang, text),
      name: 'fence',
      lang,
      contents: text
    }
  }, [label, text])

  const highlighted = useFileHighlight(request)
  const lines = useMemo(() => splitLines(normalizeNewlines(text)), [text])

  if (highlighted === undefined) {
    return (
      <code className={className} style={inheritWrapping} {...props}>
        {children}
      </code>
    )
  }

  return (
    <code className={className} style={inheritWrapping} {...props}>
      {lines.map((line, index) => (
        <Fragment key={index}>
          {index > 0 && '\n'}
          <span data-line={index + 1}>
            <LineSpans line={highlighted.lines[index]} text={line} />
          </span>
        </Fragment>
      ))}
    </code>
  )
}

const inheritWrapping = { whiteSpace: 'inherit', overflowWrap: 'inherit' } as const
