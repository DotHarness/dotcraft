import type { ForgeBranchPullResult, ForgeRefResult, ForgeRefTarget, ForgeRepo } from '../shared/forgeReferences'

export interface ForgeReader {
  readRef(target: ForgeRefTarget): Promise<ForgeRefResult>
  readBranchPull(repo: ForgeRepo, branch: string): Promise<ForgeBranchPullResult>
}
