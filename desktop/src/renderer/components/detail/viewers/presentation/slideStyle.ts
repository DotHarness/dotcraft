import type { CSSProperties } from 'react'
import type { SlideFill, TextParagraph, TextRun } from '../../../../../shared/viewer/presentation'

export function fillStyle(fill: SlideFill | undefined, images: Record<string, string>): CSSProperties {
  if (!fill) return {}
  if ('color' in fill) return { backgroundColor: fill.color }
  if ('gradient' in fill) {
    const stops = fill.gradient.stops.map((stop) => `${stop.color} ${Math.round(stop.offset * 1000) / 10}%`).join(', ')
    return { backgroundImage: `linear-gradient(${fill.gradient.angle + 90}deg, ${stops})` }
  }
  const source = images[fill.image]
  return source ? { backgroundImage: `url("${source}")`, backgroundSize: '100% 100%' } : {}
}

export function solidColor(fill: SlideFill | undefined): string | undefined {
  if (!fill) return undefined
  if ('color' in fill) return fill.color
  if ('gradient' in fill) return fill.gradient.stops[0]?.color
  return undefined
}

export function fontStack(font: string | undefined): string | undefined {
  const name = font?.replace(/["\\;]/g, '').trim()
  return name ? `"${name}", Arial, sans-serif` : undefined
}

export function runStyle(run: TextRun): CSSProperties {
  const decorations = [run.underline && 'underline', run.strike && 'line-through'].filter(Boolean).join(' ')
  return {
    fontWeight: run.bold ? 700 : undefined,
    fontStyle: run.italic ? 'italic' : undefined,
    textDecoration: decorations || undefined,
    fontSize: run.size,
    color: run.color,
    fontFamily: fontStack(run.font),
    verticalAlign: run.baseline ? (run.baseline > 0 ? 'super' : 'sub') : undefined
  }
}

export function paragraphStyle(paragraph: TextParagraph): CSSProperties {
  const size = paragraph.runs.reduce((largest, run) => Math.max(largest, run.size ?? 0), 0) || paragraph.emptySize
  return {
    textAlign: paragraph.align,
    paddingLeft: paragraph.marginLeft,
    textIndent: paragraph.indent,
    lineHeight: paragraph.lineSpacingPx !== undefined ? `${paragraph.lineSpacingPx}px` : 1.2 * (paragraph.lineSpacing ?? 1),
    marginTop: paragraph.spaceBefore,
    marginBottom: paragraph.spaceAfter,
    fontSize: size,
    fontFamily: fontStack(paragraph.runs[0]?.font),
    color: paragraph.runs[0]?.color
  }
}
