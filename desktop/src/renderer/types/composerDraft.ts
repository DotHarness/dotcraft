export type ComposerDraftSegment =
  | { type: 'text'; value: string }
  | { type: 'file'; relativePath: string }
  | { type: 'command'; command: string }
  | { type: 'skill'; skillName: string }
