import { Fragment, useEffect, useMemo, useRef, useState, type HTMLAttributes } from 'react'
import { Check, Copy, WrapText } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { CompactIconButton } from '../ui/CompactIconButton'
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
  const [copied, setCopied] = useState(false)
  const [wordWrap, setWordWrap] = useState(true)
  const [hovered, setHovered] = useState(false)
  const [focusedWithin, setFocusedWithin] = useState(false)
  const copyResetTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(() => {
    return () => {
      if (copyResetTimerRef.current != null) clearTimeout(copyResetTimerRef.current)
    }
  }, [])

  async function handleCopy(): Promise<void> {
    const text = extractText(children)
    if (!text) return
    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
      addToast(t('toast.copied'), 'success', 2000)
      if (copyResetTimerRef.current != null) clearTimeout(copyResetTimerRef.current)
      copyResetTimerRef.current = setTimeout(() => {
        setCopied(false)
        copyResetTimerRef.current = null
      }, 1500)
    } catch {
      // Clipboard access can be denied; there is nothing useful to report.
    }
  }

  const copyLabel = t(copied ? 'markdown.codeCopied' : 'markdown.copyCode')
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
        <CompactIconButton
          icon={<WrapText size={14} aria-hidden />}
          label={wrapLabel}
          active={wordWrap}
          aria-pressed={wordWrap}
          onClick={() => setWordWrap((current) => !current)}
        />
        <CompactIconButton
          icon={copied ? <Check size={14} aria-hidden /> : <Copy size={14} aria-hidden />}
          label={copyLabel}
          active={copied}
          activeColor="var(--success)"
          onClick={() => { void handleCopy() }}
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
