import { defineConfig } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'
import { createLlmsBuildEnd } from './llms'
import { enSections, sectionNav, sectionSidebar, zhSections } from './sidebar'

const repo = 'https://github.com/DotHarness/dotcraft'
const base = process.env.VITEPRESS_BASE ?? '/'
const siteOrigin = 'https://www.dotcraft.net'
// Sitemap and llms.txt need absolute URLs; VitePress does not prepend `base` for them.
const absoluteBase = new URL(base, siteOrigin).href

function escapeMustaches(value: string): string {
  return value.replaceAll('{{', '&#123;&#123;').replaceAll('}}', '&#125;&#125;')
}

const codeActionLabels = {
  en: { copy: 'Copy code', copied: 'Copied', wrap: 'Wrap lines' },
  zh: { copy: '复制代码', copied: '已复制', wrap: '自动换行' }
}

const desktopThemeSelectors = {
  postcssPlugin: 'desktop-theme-selectors',
  Rule(rule: { selector: string; source?: { input: { file?: string } } }) {
    if (!/[\\/]desktop[\\/]src[\\/]renderer[\\/]styles[\\/]/.test(rule.source?.input.file ?? '')) return
    rule.selector = rule.selector
      .replaceAll('[data-theme="light"]', ':not(.dark)')
      .replaceAll('[data-theme="dark"]', '.dark')
  }
}

export default withMermaid(defineConfig({
  title: 'DotCraft',
  description: 'An open-source, self-hosted AI agent runtime built with C#/.NET. Run the Desktop app, add the runtime to your own .NET application, and extend both with plugins.',
  base,
  cleanUrls: true,
  lastUpdated: true,
  srcExclude: ['demo/README.md'],
  scrollOffset: 112,
  sitemap: { hostname: absoluteBase },
  buildEnd: createLlmsBuildEnd({ hostname: absoluteBase }),
  vite: {
    css: { postcss: { plugins: [desktopThemeSelectors] } }
  },
  head: [
    ['meta', { name: 'theme-color', media: '(prefers-color-scheme: light)', content: '#f3f3f3' }],
    ['meta', { name: 'theme-color', media: '(prefers-color-scheme: dark)', content: '#202020' }],
    ['link', { rel: 'icon', href: `${base}dotcraft-logo.svg` }]
  ],
  markdown: {
    image: {
      lazyLoading: true
    },
    config(md) {
      const defaultFence = md.renderer.rules.fence
      const defaultCodeBlock = md.renderer.rules.code_block
      const renderToken: NonNullable<typeof defaultFence> = (tokens, idx, options, _env, self) =>
        self.renderToken(tokens, idx, options)
      const tableOpen = md.renderer.rules.table_open ?? renderToken
      const tableClose = md.renderer.rules.table_close ?? renderToken

      md.renderer.rules.table_open = (...args) => `<div class="dc-table">${tableOpen(...args)}`
      md.renderer.rules.table_close = (...args) => `${tableClose(...args)}</div>`

      md.renderer.rules.text = (tokens, idx) => escapeMustaches(md.utils.escapeHtml(tokens[idx].content))

      md.renderer.rules.code_inline = (tokens, idx) =>
        `<code>${escapeMustaches(md.utils.escapeHtml(tokens[idx].content))}</code>`

      md.renderer.rules.fence = (tokens, idx, options, env, self) => {
        const labels = env.relativePath?.startsWith('zh/') ? codeActionLabels.zh : codeActionLabels.en
        const html = defaultFence
          ? defaultFence(tokens, idx, options, env, self)
          : self.renderToken(tokens, idx, options)
        return escapeMustaches(
          html.replace(
            /<button title="[^"]*" class="copy"><\/button>/,
            `<button type="button" class="wrap" aria-pressed="false" aria-label="${labels.wrap}" data-tip="${labels.wrap}"></button>` +
              `<button type="button" class="copy" aria-label="${labels.copy}" data-tip="${labels.copy}" data-tip-done="${labels.copied}"></button>`
          )
        )
      }

      md.renderer.rules.code_block = (tokens, idx, options, env, self) =>
        escapeMustaches(
          defaultCodeBlock
            ? defaultCodeBlock(tokens, idx, options, env, self)
            : `<pre><code>${md.utils.escapeHtml(tokens[idx].content)}</code></pre>\n`
        )
    }
  },
  themeConfig: {
    logo: `${base}dotcraft-logo.svg`,
    siteTitle: 'DotCraft',
    search: {
      provider: 'local',
      options: {
        translations: { button: { buttonText: 'Search docs', buttonAriaLabel: 'Search docs' } },
        locales: {
          zh: {
            translations: {
              button: { buttonText: '搜索文档', buttonAriaLabel: '搜索文档' },
              modal: {
                displayDetails: '显示详情',
                resetButtonTitle: '清除搜索',
                backButtonTitle: '关闭搜索',
                noResultsText: '没有匹配的页面',
                footer: {
                  selectText: '打开',
                  selectKeyAriaLabel: '回车',
                  navigateText: '移动',
                  navigateUpKeyAriaLabel: '上箭头',
                  navigateDownKeyAriaLabel: '下箭头',
                  closeText: '关闭',
                  closeKeyAriaLabel: 'Esc'
                }
              }
            }
          }
        }
      }
    },
    socialLinks: [{ icon: 'github', link: repo }],
    editLink: {
      pattern: `${repo}/edit/main/docs/:path`,
      text: 'Edit this page on GitHub'
    },
    footer: {
      copyright: 'Copyright © DotHarness'
    }
  },
  locales: {
    root: {
      label: 'English',
      lang: 'en-US',
      title: 'DotCraft',
      description: 'An open-source, self-hosted AI agent runtime built with C#/.NET. Run the Desktop app, add the runtime to your own .NET application, and extend both with plugins.',
      themeConfig: {
        nav: sectionNav(enSections),
        sidebar: sectionSidebar(enSections),
        outline: { label: 'On this page' },
        docFooter: {
          prev: 'Previous',
          next: 'Next'
        },
        returnToTopLabel: 'Back to top',
        editLink: {
          pattern: `${repo}/edit/main/docs/:path`,
          text: 'Edit this page on GitHub'
        }
      }
    },
    zh: {
      label: '简体中文',
      lang: 'zh-CN',
      title: 'DotCraft',
      description: '基于 C#/.NET 构建的开源、自托管 AI Agent Runtime。既可以直接使用 Desktop 应用，也可以把运行时引入你自己的 .NET 应用，两者都能用插件扩展。',
      themeConfig: {
        nav: sectionNav(zhSections),
        sidebar: sectionSidebar(zhSections),
        outline: { label: '本页目录' },
        docFooter: {
          prev: '上一页',
          next: '下一页'
        },
        lastUpdated: {
          text: '最后更新'
        },
        editLink: {
          pattern: `${repo}/edit/main/docs/:path`,
          text: '在 GitHub 上编辑此页'
        },
        langMenuLabel: '语言',
        returnToTopLabel: '回到顶部',
        sidebarMenuLabel: '菜单',
        darkModeSwitchLabel: '外观',
        lightModeSwitchTitle: '切换到浅色模式',
        darkModeSwitchTitle: '切换到深色模式'
      }
    }
  }
}))
