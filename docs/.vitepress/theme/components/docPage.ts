import { computed } from 'vue'
import { useData, type DefaultTheme } from 'vitepress'

interface Crumb {
  text: string
  link?: string
}

const STRINGS = {
  en: { breadcrumb: 'Breadcrumb', copyLink: 'Copy link', linkCopied: 'Link copied', edit: 'Edit on GitHub', lastUpdated: 'Last updated' },
  zh: { breadcrumb: '页面位置', copyLink: '复制链接', linkCopied: '已复制链接', edit: '在 GitHub 上编辑', lastUpdated: '最后更新' }
}

function routeKey(path: string): string {
  return path
    .replace(/^\//, '')
    .replace(/\.(md|html)$/, '')
    .replace(/(^|\/)index$/, '')
    .replace(/\/$/, '')
}

function plainText(html: string): string {
  const label = /<span class="dc-side-label">([\s\S]*?)<\/span>/.exec(html)
  return (label ? label[1] : html.replace(/<[^>]*>/g, '')).trim()
}

function findTrail(items: DefaultTheme.SidebarItem[], key: string): DefaultTheme.SidebarItem[] | null {
  for (const item of items) {
    const nested = item.items ? findTrail(item.items, key) : null
    if (nested) return [item, ...nested]
    if (item.link && routeKey(item.link) === key) return [item]
  }
  return null
}

export function useDocStrings() {
  const { lang } = useData()
  return computed(() => (lang.value.startsWith('zh') ? STRINGS.zh : STRINGS.en))
}

export function useEditUrl() {
  const { theme, page, frontmatter } = useData()
  return computed(() => {
    const pattern = theme.value.editLink?.pattern
    if (!pattern || frontmatter.value.editLink === false) return ''
    return typeof pattern === 'function' ? pattern(page.value) : pattern.replace(/:path/g, page.value.filePath)
  })
}

export function useBreadcrumb() {
  const { theme, page } = useData()
  return computed<Crumb[]>(() => {
    const sidebar = theme.value.sidebar
    if (!Array.isArray(sidebar)) return []
    const trail = findTrail(sidebar, routeKey(page.value.relativePath)) ?? []
    const nav = (theme.value.nav ?? []) as DefaultTheme.NavItemWithLink[]
    return trail.map((item) => {
      const text = plainText(item.text ?? '')
      return { text, link: item.link ?? nav.find((entry) => entry.text === text)?.link }
    })
  })
}
