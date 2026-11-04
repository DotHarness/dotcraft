import { Terminal, type IBuffer } from '@xterm/headless'

const SCROLLBACK_LINES = 10_000

export interface RenderedTerminal {
  lines: string[]
  lost: boolean
}

export function renderTerminalLines(
  data: string,
  size: { cols: number; rows: number },
  outputTrimmed: boolean
): Promise<RenderedTerminal> {
  const terminal = new Terminal({
    cols: size.cols,
    rows: size.rows,
    scrollback: SCROLLBACK_LINES,
    allowProposedApi: true
  })
  const replay = outputTrimmed ? fromFirstLineStart(data) : data
  return new Promise((resolve, reject) => {
    terminal.write(replay, () => {
      try {
        const lost = outputTrimmed || terminal.buffer.normal.length >= SCROLLBACK_LINES + size.rows
        resolve({ lines: logicalLines(terminal.buffer.active), lost })
      } catch (error) {
        reject(error)
      } finally {
        terminal.dispose()
      }
    })
  })
}

function fromFirstLineStart(data: string): string {
  const lineFeed = data.indexOf('\n')
  return lineFeed < 0 ? data : data.slice(lineFeed + 1)
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
