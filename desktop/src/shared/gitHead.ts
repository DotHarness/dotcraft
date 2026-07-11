export type GitHeadInspection =
  | { kind: 'branch'; label: string }
  | { kind: 'detached'; label: string }
  | { kind: 'none' }
