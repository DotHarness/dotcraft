import { release } from 'node:os'
import { Terminal, type IBuffer } from '@xterm/headless'

const SCROLLBACK_LINES = 10_000

export interface TerminalResizeMark {
  at: number
  cols: number
  rows: number
}

export interface TerminalScreenSizes {
  cols: number
  rows: number
  resizes: TerminalResizeMark[]
}

export interface RenderedTerminal {
  lines: string[]
  lost: boolean
}

export function renderTerminalLines(
  data: string,
  sizes: TerminalScreenSizes,
  outputTrimmed: boolean
): Promise<RenderedTerminal> {
  const terminal = new Terminal({
    cols: sizes.cols,
    rows: sizes.rows,
    scrollback: SCROLLBACK_LINES,
    convertEol: true,
    allowProposedApi: true,
    ...(process.platform === 'win32'
      ? { windowsPty: { backend: 'conpty' as const, buildNumber: Number(release().split('.')[2]) } }
      : {})
  })
  let replayed = outputTrimmed ? firstLineStart(data) : 0
  for (const { at, cols, rows } of sizes.resizes) {
    const end = Math.max(replayed, at)
    terminal.write(data.slice(replayed, end), () => terminal.resize(cols, rows))
    replayed = end
  }
  return new Promise((resolve, reject) => {
    terminal.write(data.slice(replayed), () => {
      try {
        const lost = outputTrimmed || terminal.buffer.normal.length >= SCROLLBACK_LINES + terminal.rows
        resolve({ lines: logicalLines(terminal.buffer.active), lost })
      } catch (error) {
        reject(error)
      } finally {
        terminal.dispose()
      }
    })
  })
}

function firstLineStart(data: string): number {
  return data.indexOf('\n') + 1
}

function logicalLines(buffer: IBuffer): string[] {
  const lines: string[] = []
  for (let y = 0; y < buffer.length; y++) {
    const line = buffer.getLine(y)
    if (!line) continue
    const text = line.translateToString(buffer.getLine(y + 1)?.isWrapped !== true)
    if (line.isWrapped && lines.length > 0) {
      lines[lines.length - 1] += text
    } else {
      lines.push(text)
    }
  }
  while (lines.length > 0 && lines[0].trim() === '') lines.shift()
  while (lines.length > 0 && lines[lines.length - 1].trim() === '') lines.pop()
  return lines
}
