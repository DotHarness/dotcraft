const MAX_LINES = 2_000
const MAX_LINE_CHARS = 4_000
const ANSI = /\u001b\[[0-9;?]*[ -/]*[@-~]|\u001b\][^\u0007]*(?:\u0007|\u001b\\)/g

export class ServerLogBuffer {
  private readonly complete: string[] = []
  private partial = ''

  append(chunk: string): void {
    const text = (this.partial + chunk.replace(ANSI, '')).replace(/\r\n/g, '\n')
    const parts = text.split('\n')
    this.partial = parts.pop() ?? ''
    for (const part of parts) this.push(part)
    if (this.partial.length > MAX_LINE_CHARS) {
      this.push(this.partial)
      this.partial = ''
    }
  }

  lines(): string[] {
    return this.partial ? [...this.complete, this.partial] : [...this.complete]
  }

  private push(line: string): void {
    const text = line.includes('\r') ? line.slice(line.lastIndexOf('\r') + 1) : line
    this.complete.push(text.length > MAX_LINE_CHARS ? text.slice(0, MAX_LINE_CHARS) : text)
    if (this.complete.length > MAX_LINES) this.complete.splice(0, this.complete.length - MAX_LINES)
  }
}
