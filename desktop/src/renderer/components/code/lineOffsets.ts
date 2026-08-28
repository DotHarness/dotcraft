// A Fenwick tree, because with word wrap on both "height above row N" and "row at
// pixel Y" change on every scroll frame and each must stay logarithmic.
export class LineOffsets {
  private readonly tree: Float64Array
  private readonly heights: Float64Array
  readonly count: number

  constructor(count: number, estimate: number) {
    this.count = count
    this.heights = new Float64Array(count).fill(estimate)
    // Linear build: seed each node, then fold it into its parent.
    this.tree = new Float64Array(count + 1)
    for (let index = 1; index <= count; index++) {
      this.tree[index] += estimate
      const parent = index + (index & -index)
      if (parent <= count) this.tree[parent] += this.tree[index]
    }
  }

  heightOf(index: number): number {
    return index >= 0 && index < this.count ? this.heights[index] : 0
  }

  /** Returns the change in height, for anchoring the scroll position. */
  setHeight(index: number, height: number): number {
    if (index < 0 || index >= this.count) return 0
    const delta = height - this.heights[index]
    if (delta === 0) return 0
    this.heights[index] = height
    for (let node = index + 1; node <= this.count; node += node & -node) {
      this.tree[node] += delta
    }
    return delta
  }

  get totalHeight(): number {
    return this.offsetOf(this.count)
  }

  /** Height of all rows before `index`. */
  offsetOf(index: number): number {
    let sum = 0
    for (let node = Math.max(0, Math.min(index, this.count)); node > 0; node -= node & -node) {
      sum += this.tree[node]
    }
    return sum
  }

  indexAtOffset(offset: number): number {
    if (offset <= 0 || this.count === 0) return 0
    let position = 0
    let remaining = offset
    // Standard Fenwick prefix search, walking down from the highest power of two.
    let step = 1
    while (step * 2 <= this.count) step *= 2
    for (; step > 0; step = Math.floor(step / 2)) {
      const next = position + step
      if (next <= this.count && this.tree[next] <= remaining) {
        position = next
        remaining -= this.tree[next]
      }
    }
    return Math.min(position, this.count - 1)
  }
}
