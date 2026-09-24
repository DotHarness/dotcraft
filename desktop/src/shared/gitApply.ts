export type GitApplyPatchResult =
  | { ok: true }
  | { ok: false; code: 'not-git-repo' | 'invalid-patch' | 'apply-failed'; message: string }

export interface GitApplyPatchOptions {
  reverse: boolean
}
