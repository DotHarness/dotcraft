export class LruMap<K, V> {
  private readonly entries = new Map<K, V>()

  constructor(readonly limit: number) {}

  get size(): number {
    return this.entries.size
  }

  get(key: K): V | undefined {
    if (!this.entries.has(key)) return undefined
    const value = this.entries.get(key) as V
    // Map preserves insertion order, so re-inserting moves the entry to the end.
    this.entries.delete(key)
    this.entries.set(key, value)
    return value
  }

  has(key: K): boolean {
    return this.entries.has(key)
  }

  set(key: K, value: V): void {
    if (this.entries.has(key)) this.entries.delete(key)
    this.entries.set(key, value)
    while (this.entries.size > this.limit) {
      const oldest = this.entries.keys().next()
      if (oldest.done === true) break
      this.entries.delete(oldest.value)
    }
  }

  delete(key: K): boolean {
    return this.entries.delete(key)
  }

  clear(): void {
    this.entries.clear()
  }
}
