import { create } from 'zustand'
import type {
  MarketInstallResult,
  MarketDotCraftInstallPreparation,
  MarketSkillDetail,
  MarketSkillSummary,
  SkillMarketProviderId
} from '../../shared/skillMarket'

export type SkillMarketProviderFilter = 'all' | SkillMarketProviderId

interface SkillMarketState {
  query: string
  provider: SkillMarketProviderFilter
  results: MarketSkillSummary[]
  loading: boolean
  error: string | null
  selectedSkill: MarketSkillDetail | null
  detailLoading: boolean
  installSlug: string | null
  dotCraftInstallSlug: string | null

  setQuery(query: string): void
  setProvider(provider: SkillMarketProviderFilter): void
  search(): Promise<void>
  selectSkill(skill: MarketSkillSummary): Promise<void>
  clearSelection(): void
  installSelected(overwrite?: boolean): Promise<MarketInstallResult>
  prepareDotCraftInstall(): Promise<MarketDotCraftInstallPreparation>
}

export const useSkillMarketStore = create<SkillMarketState>((set, get) => ({
  query: '',
  provider: 'all',
  results: [],
  loading: false,
  error: null,
  selectedSkill: null,
  detailLoading: false,
  installSlug: null,
  dotCraftInstallSlug: null,

  setQuery(query) {
    set({ query })
  },

  setProvider(provider) {
    set({ provider })
  },

  async search() {
    const { query, provider } = get()
    const trimmed = query.trim()
    if (!trimmed) {
      set({ results: [], loading: false, error: null })
      return
    }
    set({ loading: true, error: null })
    try {
      const result = await window.api.skillMarket.search({
        query: trimmed,
        provider,
        limit: 24
      })
      set({ results: result.skills, loading: false })
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      set({ error: msg, loading: false })
    }
  },

  async selectSkill(skill) {
    set({ selectedSkill: { ...skill }, detailLoading: true, error: null })
    try {
      const detail = await window.api.skillMarket.detail({
        provider: skill.provider,
        slug: skill.slug
      })
      set((state) => {
        const selected = state.selectedSkill
        if (!selected || selected.provider !== skill.provider || selected.slug !== skill.slug) {
          return state
        }
        return {
          selectedSkill: { ...selected, ...detail },
          detailLoading: false
        }
      })
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      set({ error: msg, detailLoading: false })
    }
  },

  clearSelection() {
    set({ selectedSkill: null, detailLoading: false })
  },

  async installSelected(overwrite = false) {
    const selected = get().selectedSkill
    if (!selected) throw new Error('No selected skill')
    set({ installSlug: selected.slug, error: null })
    try {
      const result = await window.api.skillMarket.install({
        provider: selected.provider,
        slug: selected.slug,
        version: selected.version,
        overwrite
      })
      set((state) => ({
        installSlug: null,
        selectedSkill: state.selectedSkill
          ? { ...state.selectedSkill, installed: true, updateAvailable: false }
          : state.selectedSkill,
        results: state.results.map((skill) =>
          skill.provider === selected.provider && skill.slug === selected.slug
            ? { ...skill, installed: true, updateAvailable: false }
            : skill
        )
      }))
      return result
    } catch (e: unknown) {
      set({ installSlug: null })
      throw e
    }
  },

  async prepareDotCraftInstall() {
    const selected = get().selectedSkill
    if (!selected) throw new Error('No selected skill')
    set({ dotCraftInstallSlug: selected.slug, error: null })
    try {
      const result = await window.api.skillMarket.prepareDotCraftInstall({
        provider: selected.provider,
        slug: selected.slug,
        version: selected.version
      })
      set({ dotCraftInstallSlug: null })
      return result
    } catch (e: unknown) {
      set({ dotCraftInstallSlug: null })
      throw e
    }
  }
}))

