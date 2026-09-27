const GROUP_SIZE = 64

export interface EntryGroup {
  key: number
  startIndex: number
  entryKeys: string[]
}

/** Keys grouped before keep their group, so inserting or replacing entries leaves other groups untouched. */
export function groupEntries(entryKeys: readonly string[], previous: readonly EntryGroup[] = []): EntryGroup[] {
  const previousGroupOf = new Map(previous.flatMap((group) => group.entryKeys.map((key) => [key, group.key] as const)))
  const nextKnownGroup = new Array<number | undefined>(entryKeys.length)
  let following: number | undefined
  for (let index = entryKeys.length - 1; index >= 0; index--) {
    following = previousGroupOf.get(entryKeys[index]) ?? following
    nextKnownGroup[index] = following
  }

  let nextKey = previous.reduce((max, group) => Math.max(max, group.key), -1) + 1
  const usedKeys = new Set<number>()
  const groups: EntryGroup[] = []
  entryKeys.forEach((entryKey, index) => {
    const known = previousGroupOf.get(entryKey)
    let group = groups[groups.length - 1]
    const startsGroup = !group ||
      (known !== undefined && known !== group.key) ||
      (known === undefined && group.entryKeys.length >= GROUP_SIZE && nextKnownGroup[index] !== group.key)
    if (startsGroup) {
      const key = known !== undefined && !usedKeys.has(known) ? known : nextKey++
      group = { key, startIndex: index, entryKeys: [] }
      groups.push(group)
      usedKeys.add(key)
    }
    group.entryKeys.push(entryKey)
  })
  return groups
}
