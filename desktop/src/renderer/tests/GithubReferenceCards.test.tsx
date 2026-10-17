import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { BranchPullRequestPill } from '../components/conversation/githubReferences/BranchPullRequestPill'
import { GithubReferenceChip } from '../components/conversation/githubReferences/GithubReferenceChip'
import { useGithubRefStore } from '../stores/githubRefStore'
import type { GithubPullSummary, GithubRefTarget } from '../../shared/githubReferences'
import { installDesktopApiMock } from './desktopApiMock'

const getReferenceSummary = vi.fn()
const getBranchPullRequest = vi.fn()

const pull: GithubPullSummary = {
  kind: 'pull',
  owner: 'octo',
  repo: 'app',
  number: 7,
  title: 'Show the branch pull request',
  author: 'octo',
  createdAt: '2026-01-01T00:00:00.000Z',
  state: 'open',
  additions: 12,
  deletions: 3,
  changedFiles: 2,
  checks: { state: 'passing', passed: 2, failed: 0, total: 2 }
}

const target: GithubRefTarget = { owner: 'octo', repo: 'app', kind: 'pull', number: 7 }

describe('GitHub reference cards', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useGithubRefStore.setState({ refs: {}, branchPulls: {}, repos: {} })
    installDesktopApiMock({
      settings: { get: vi.fn(async () => ({ locale: 'en' })) },
      github: {
        getReferenceSummary,
        getBranchPullRequest,
        getWorkspaceRepo: vi.fn(async () => null)
      },
      profile: { getGithubIdentity: vi.fn(async () => null) }
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('opens the branch pull request card and keeps it open', async () => {
    getBranchPullRequest.mockResolvedValue({ status: 'ready', ref: pull })
    render(
      <LocaleProvider>
        <BranchPullRequestPill gitPath="fixtures\\app" branch="feat/pill" />
      </LocaleProvider>
    )

    const pill = await screen.findByRole('button', { name: /#7/ })
    fireEvent.mouseEnter(pill)

    expect(await screen.findByRole('dialog')).toBeTruthy()
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(screen.getByRole('dialog')).toBeTruthy()
  })

  it('asks again for an expired reference when its card opens', async () => {
    getReferenceSummary.mockResolvedValue({ status: 'error', error: 'unavailable' })
    render(
      <LocaleProvider>
        <GithubReferenceChip
          target={target}
          href="https://github.com/octo/app/pull/7"
          text="https://github.com/octo/app/pull/7"
          linkProps={{ href: 'https://github.com/octo/app/pull/7' }}
          onOpenLink={() => {}}
        />
      </LocaleProvider>
    )
    await waitFor(() => expect(getReferenceSummary).toHaveBeenCalledTimes(1))

    const later = Date.now() + 10_000
    vi.spyOn(Date, 'now').mockReturnValue(later)
    getReferenceSummary.mockResolvedValue({ status: 'ready', ref: pull })
    fireEvent.mouseEnter(screen.getByRole('link'))

    await waitFor(() => expect(getReferenceSummary).toHaveBeenCalledTimes(2))
    expect((await screen.findAllByText('Show the branch pull request')).length).toBeGreaterThan(0)
  })
})
