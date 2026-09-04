import { defineConfig, type DefaultTheme } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'
import { createLlmsBuildEnd } from './llms'
import { enSidebar, zhSidebar } from './sidebar'

const repo = 'https://github.com/DotHarness/dotcraft'
const base = process.env.VITEPRESS_BASE ?? '/'
const siteOrigin = 'https://www.dotcraft.net'
// Sitemap and llms.txt need absolute URLs; VitePress does not prepend `base` for them.
const absoluteBase = new URL(base, siteOrigin).href

function escapeMustaches(value: string): string {
  return value.replaceAll('{{', '&#123;&#123;').replaceAll('}}', '&#125;&#125;')
}

const enNav: DefaultTheme.NavItem[] = [
  { text: 'Overview', link: '/' },
  { text: 'Getting Started', link: '/getting-started' },
  { text: 'Features', link: '/features/agent-system/' },
  { text: 'Developing', link: '/developing/workflow/spec-driven-development' }
]

const zhNav: DefaultTheme.NavItem[] = [
  { text: '总览', link: '/zh/' },
  { text: '快速开始', link: '/zh/getting-started' },
  { text: '功能', link: '/zh/features/agent-system/' },
  { text: '开发', link: '/zh/developing/workflow/spec-driven-development' }
]

const redirectMap: Record<string, string> = {
  'reference.md': 'developing/architecture/overview.md',
  'features.md': 'features/agent-system/index.md',
  'getting-started.md': 'getting-started.md',
  'config_guide.md': 'developing/configuration.md',
  'desktop_guide.md': 'features/entry-points/desktop.md',
  'acp_guide.md': 'features/entry-points/editors.md',
  'unity_guide.md': 'features/entry-points/editors.md',
  'appserver_guide.md': 'developing/lifecycle/appserver.md',
  'hub_guide.md': 'developing/lifecycle/hub.md',
  'dash_board_guide.md': 'features/self-hosted/observability.md',
  'subagents_guide.md': 'features/agent-system/subagents.md',
  'external_cli_subagents_guide.md': 'features/agent-system/subagents.md',
  'automations_guide.md': 'features/agent-system/automations.md',
  'hooks_guide.md': 'features/agent-system/hooks.md',
  'automations/reference.md': 'features/agent-system/automations.md',
  'hooks/reference.md': 'developing/configuration.md#automations-goals-and-hooks',
  'config/security.md': 'features/self-hosted/security.md',
  'settings-lifecycle.md': 'developing/lifecycle/settings-lifecycle.md',
  'features/workspace-handoff.md': 'features/agent-system/workspace-handoff.md',
  'developing/context-export-cli.md': 'features/agent-system/workspace-handoff.md',
  'developing/workflow/workspace-handoff.md': 'features/agent-system/workspace-handoff.md',
  'typescript-module-integration.md': 'developing/integrations/typescript-module.md',
  'reference/config.md': 'developing/configuration.md',
  'reference/appserver-protocol.md': 'developing/protocols/appserver-protocol.md',
  'reference/hub-protocol.md': 'developing/protocols/hub-protocol.md',
  'reference/dashboard-api.md': 'developing/protocols/dashboard-api.md',
  'sdk/index.md': 'developing/sdks/index.md',
  'sdk/python.md': 'developing/sdks/index.md',
  'sdk/typescript.md': 'developing/sdks/typescript.md',
  'sdk/dotnet.md': 'developing/sdks/dotnet.md',
  'sdk/python-telegram.md': 'developing/sdks/channels.md',
  'sdk/typescript-feishu.md': 'features/channels/feishu.md',
  'sdk/typescript-telegram.md': 'features/channels/telegram.md',
  'sdk/typescript-weixin.md': 'features/channels/weixin.md',
  'sdk/typescript-qq.md': 'features/channels/qq.md',
  'sdk/typescript-wecom.md': 'features/channels/wecom.md',
  'developing/channels/qq.md': 'features/channels/qq.md',
  'developing/channels/wecom.md': 'features/channels/wecom.md',
  'developing/channels/feishu.md': 'features/channels/feishu.md',
  'developing/channels/telegram.md': 'features/channels/telegram.md',
  'developing/channels/weixin.md': 'features/channels/weixin.md',
  'developing/channels/reference.md': 'features/channels/reference.md',
  'features/entry-points/channels.md': 'features/channels/index.md',
  'skills/agent-self-learning.md': 'features/agent-system/skills.md',
  'skills/marketplace.md': 'features/agent-system/skills.md',
  'plugins/install.md': 'features/agent-system/plugins-tools.md',
  'plugins/build.md': 'features/agent-system/plugins-tools.md',
  'features/workspace.md': 'getting-started.md',
  'features/project-design/workspace.md': 'getting-started.md',
  'features/memory.md': 'features/agent-system/memory.md',
  'features/skills.md': 'features/agent-system/skills.md',
  'features/plugins-tools.md': 'features/agent-system/plugins-tools.md',
  'features/automations.md': 'features/agent-system/automations.md',
  'features/subagents.md': 'features/agent-system/subagents.md',
  'features/teams.md': 'features/agent-system/teams.md',
  'features/observability.md': 'features/self-hosted/observability.md',
  'features/security.md': 'features/self-hosted/security.md',
  'features/session-core.md': 'developing/architecture/session-core.md',
  'features/app.md': 'developing/integrations/app-binding.md',
  'developing/spec-driven-development.md': 'developing/workflow/spec-driven-development.md',
  'developing/workspace-handoff.md': 'features/agent-system/workspace-handoff.md',
  'developing/architecture.md': 'developing/architecture/overview.md',
  'developing/configuration/reference.md': 'developing/configuration.md',
  'developing/settings-lifecycle.md': 'developing/lifecycle/settings-lifecycle.md',
  'developing/appserver.md': 'developing/lifecycle/appserver.md',
  'developing/hub.md': 'developing/lifecycle/hub.md',
  'developing/server-deployment.md': 'features/self-hosted/server-deployment.md',
  'developing/lifecycle/server-deployment.md': 'features/self-hosted/server-deployment.md',
  'developing/appserver-protocol.md': 'developing/protocols/appserver-protocol.md',
  'developing/hub-protocol.md': 'developing/protocols/hub-protocol.md',
  'developing/dashboard-api.md': 'developing/protocols/dashboard-api.md',
  'developing/sdk.md': 'developing/sdks/index.md',
  'developing/sdk-typescript.md': 'developing/sdks/typescript.md',
  'developing/sdk-dotnet.md': 'developing/sdks/dotnet.md',
  'developing/sdk-python.md': 'developing/sdks/index.md',
  'developing/app-binding.md': 'developing/integrations/app-binding.md',
  'developing/typescript-module.md': 'developing/integrations/typescript-module.md'
}

const rewrites: Record<string, string> = {}
for (const [from, to] of Object.entries(redirectMap)) {
  // English root keeps short path
  rewrites[from] = to
  // Chinese moves under /zh/
  rewrites[`zh/${from}`] = `zh/${to}`
  // Old /en/ URLs redirect to root (English is now default)
  rewrites[`en/${from}`] = to
}
// Map any leftover /en/<page> to root
rewrites['en/index.md'] = 'index.md'
rewrites['en/getting-started.md'] = 'getting-started.md'
rewrites['zh/hooks/reference.md'] = 'zh/developing/configuration.md#automations-goals-与-hooks'

export default withMermaid(defineConfig({
  title: 'DotCraft',
  description: 'A project-native AI agent runtime for building extensible agents that evolve with your projects.',
  base,
  cleanUrls: true,
  lastUpdated: true,
  srcExclude: ['demo/README.md'],
  rewrites,
  sitemap: { hostname: absoluteBase },
  buildEnd: createLlmsBuildEnd({ hostname: absoluteBase }),
  head: [
    ['meta', { name: 'theme-color', content: '#4A7FA5' }],
    ['link', { rel: 'icon', href: `${base}dotcraft-logo.svg` }]
  ],
  markdown: {
    image: {
      lazyLoading: true
    },
    config(md) {
      const defaultFence = md.renderer.rules.fence
      const defaultCodeBlock = md.renderer.rules.code_block

      md.renderer.rules.text = (tokens, idx) => escapeMustaches(md.utils.escapeHtml(tokens[idx].content))

      md.renderer.rules.code_inline = (tokens, idx) =>
        `<code>${escapeMustaches(md.utils.escapeHtml(tokens[idx].content))}</code>`

      md.renderer.rules.fence = (tokens, idx, options, env, self) =>
        escapeMustaches(
          defaultFence
            ? defaultFence(tokens, idx, options, env, self)
            : self.renderToken(tokens, idx, options)
        )

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
    search: { provider: 'local' },
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
      description: 'A project-native AI agent runtime for building extensible agents that evolve with your projects.',
      themeConfig: {
        nav: enNav,
        sidebar: enSidebar,
        outline: { label: 'On this page' },
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
      description: '用于构建可扩展 AI Agent 的项目原生运行时，让 Agent 随项目持续演进。',
      themeConfig: {
        nav: zhNav,
        sidebar: zhSidebar,
        outline: { label: '本页目录' },
        docFooter: {
          prev: '上一页',
          next: '下一页'
        },
        lastUpdated: {
          text: '最后更新'
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
