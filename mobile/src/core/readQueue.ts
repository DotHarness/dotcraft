export class ReadQueue {
  private active = 0
  private readonly waiting: (() => void)[] = []

  constructor(private readonly limit: number) {}

  async run<T>(read: () => Promise<T>): Promise<T> {
    if (this.active >= this.limit) await new Promise<void>((resolve) => this.waiting.push(resolve))
    else this.active += 1
    try { return await read() }
    finally {
      const next = this.waiting.shift()
      if (next) next()
      else this.active -= 1
    }
  }
}
