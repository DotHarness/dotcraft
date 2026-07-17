import { Fragment } from 'react'
import { MarkdownRenderer } from './MarkdownRenderer'
import { InlineVisualizationFrame } from './InlineVisualizationFrame'
import { hideStreamingVisualizationTail, parseInlineVisualizations, stripInlineVisualizationDirectives } from './inlineVisualizationParser'

interface Props { text: string; streaming: boolean; threadId?: string; turnId?: string; itemId?: string }

export function InlineVisualizationMessage({ text, streaming, threadId, turnId, itemId }: Props): JSX.Element {
  if (streaming) return <MarkdownRenderer content={stripInlineVisualizationDirectives(hideStreamingVisualizationTail(text))} />
  if (!threadId || !turnId || !itemId) return <MarkdownRenderer content={text} />
  const directives = parseInlineVisualizations(text)
  if (directives.length === 0) return <MarkdownRenderer content={text} />
  const nodes: JSX.Element[] = []
  let cursor = 0
  directives.forEach((directive, index) => {
    const markdown = text.slice(cursor, directive.start)
    if (markdown.trim()) nodes.push(<MarkdownRenderer key={`markdown-${index}`} content={markdown} />)
    nodes.push(<InlineVisualizationFrame key={`visual-${index}-${directive.file}`} threadId={threadId} turnId={turnId} itemId={itemId} file={directive.file} />)
    cursor = directive.end
    if (text[cursor] === '\n') cursor++
  })
  const tail = text.slice(cursor)
  if (tail.trim()) nodes.push(<MarkdownRenderer key="markdown-tail" content={tail} />)
  return <Fragment>{nodes}</Fragment>
}
