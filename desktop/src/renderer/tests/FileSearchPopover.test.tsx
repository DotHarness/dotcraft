import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, render, screen, waitFor, cleanup } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { FileSearchPopover } from '../components/conversation/FileSearchPopover'

const settingsGet = vi.fn()
const searchFiles = vi.fn()

interface SearchFilesResult {
  files: Array<{ name: string; relativePath: string; dir: string }>
  indexStatus?: 'empty' | 'building' | 'ready'
  indexedCount?: number
  stale?: boolean
}

function installApi(): void {
  Object.defineProperty(window, 'api', {
    configurable: true,
    value: {
      settings: { get: settingsGet },
      workspace: { searchFiles }
    }
  })
}

function renderWithLocale(node: JSX.Element): void {
  render(<LocaleProvider>{node}</LocaleProvider>)
}

const DEBOUNCE_MS = 80
const POLL_MS = 1500

describe('FileSearchPopover', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    installApi()
  })

  afterEach(() => {
    cleanup()
    vi.useRealTimers()
  })

  async function flushPromises(): Promise<void> {
    await Promise.resolve()
    await Promise.resolve()
  }

  it('renders the indexing message while the workspace index is still being built', async () => {
    searchFiles.mockResolvedValue<SearchFilesResult>({
      files: [],
      indexStatus: 'building',
      indexedCount: 0,
      stale: true
    })

    renderWithLocale(
      <FileSearchPopover
        query="re"
        visible
        workspacePath="/workspace"
        onSelect={() => {}}
        onDismiss={() => {}}
      />
    )

    await waitFor(() => {
      expect(searchFiles).toHaveBeenCalledTimes(1)
    })
    await waitFor(() => {
      expect(screen.getByText('Indexing files…')).toBeTruthy()
    })
    expect(screen.getByRole('progressbar', { name: 'Indexing files…' })).toBeTruthy()
    expect(screen.getAllByTestId('file-search-skeleton-row')).toHaveLength(3)
  })

  it('renders progress count when the index reports indexedCount > 0', async () => {
    searchFiles.mockResolvedValue<SearchFilesResult>({
      files: [],
      indexStatus: 'building',
      indexedCount: 1234,
      stale: true
    })

    renderWithLocale(
      <FileSearchPopover
        query="x"
        visible
        workspacePath="/workspace"
        onSelect={() => {}}
        onDismiss={() => {}}
      />
    )

    await waitFor(() => {
      expect(screen.getByText('Indexing files… (1234 indexed)')).toBeTruthy()
    })
    expect(screen.getByRole('progressbar', { name: 'Indexing files… (1234 indexed)' })).toBeTruthy()
  })

  it('automatically polls the IPC while the index is still building, then renders results when ready', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })

    let call = 0
    searchFiles.mockImplementation(async () => {
      call += 1
      if (call === 1) {
        return {
          files: [],
          indexStatus: 'building',
          indexedCount: 100,
          stale: true
        } satisfies SearchFilesResult
      }
      return {
        files: [{ name: 'Real.cs', relativePath: 'Source/Real.cs', dir: 'Source' }],
        indexStatus: 'ready',
        indexedCount: 200,
        stale: false
      } satisfies SearchFilesResult
    })

    renderWithLocale(
      <FileSearchPopover
        query="Real"
        visible
        workspacePath="/workspace"
        onSelect={() => {}}
        onDismiss={() => {}}
      />
    )

    // First debounce -> first IPC call returns building.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(DEBOUNCE_MS + 1)
      await flushPromises()
    })
    await waitFor(() => {
      expect(searchFiles).toHaveBeenCalledTimes(1)
    })
    expect(screen.getByText('Indexing files… (100 indexed)')).toBeTruthy()

    // Polling timer fires -> second IPC call returns ready.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(POLL_MS + 1)
      await flushPromises()
    })
    await waitFor(() => {
      expect(searchFiles).toHaveBeenCalledTimes(2)
    })
    await waitFor(() => {
      expect(screen.getByRole('option', { name: /Real\.cs/ })).toBeTruthy()
    })
    expect(screen.queryByRole('progressbar')).toBeNull()
    expect(screen.queryAllByTestId('file-search-skeleton-row')).toHaveLength(0)
  })

  it('shows "no matching files" only when the index is ready and the query has no hits', async () => {
    searchFiles.mockResolvedValue<SearchFilesResult>({
      files: [],
      indexStatus: 'ready',
      indexedCount: 42,
      stale: false
    })

    renderWithLocale(
      <FileSearchPopover
        query="zzznotfound"
        visible
        workspacePath="/workspace"
        onSelect={() => {}}
        onDismiss={() => {}}
      />
    )

    await waitFor(() => {
      expect(screen.getByText('No matching files')).toBeTruthy()
    })
    expect(screen.queryByRole('progressbar')).toBeNull()
    expect(screen.queryAllByTestId('file-search-skeleton-row')).toHaveLength(0)
  })
})
