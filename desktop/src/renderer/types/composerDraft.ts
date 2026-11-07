export type ComposerDraftSegment =
  | { type: 'text'; value: string }
  | { type: 'file'; relativePath: string }
  | { type: 'skill'; skillName: string }
  | { type: 'thread'; threadId: string; title: string }
