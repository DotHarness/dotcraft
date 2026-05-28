import { defineConfig, type DefaultTheme } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'
import { withIcon } from './theme/icons'

const repo = 'https://github.com/DotHarness/dotcraft'
const base = process.env.VITEPRESS_BASE ?? (process.env.GITHUB_ACTIONS ? '/dotcraft/' : '/')

function escapeMustaches(value: string): string {
  return value.replaceAll('{{', '&#123;&#123;').replaceAll('}}', '&#125;&#125;')
}

const enSidebar: DefaultTheme.Sidebar = [
  {
    text: 'Overview',
    items: [
      { text: withIcon('diamond', 'What is DotCraft'), link: '/' },
      { text: withIcon('play', 'Getting Started'), link: '/getting-started' }
    ]
  },
  {
    text: 'Features',
    items: [
      { text: withIcon('folder', 'Project Workspace'), link: '/features/workspace' },
      { text: withIcon('brain', 'Memory & Dreams'), link: '/features/memory' },
      { text: withIcon('sparkles', 'Skills & Self-Learning'), link: '/features/skills' },
      { text: withIcon('layers', 'Unified Session Core'), link: '/features/session-core' },
      {
        text: withIcon('grid', 'Entry Points'),
        link: '/features/entry-points/',
        collapsed: false,
        items: [
          { text: withIcon('grid', 'Overview'), link: '/features/entry-points/' },
          { text: withIcon('monitor', 'Desktop'), link: '/features/entry-points/desktop' },
          { text: withIcon('terminal', 'TUI'), link: '/features/entry-points/tui' },
          { text: withIcon('code', 'IDE / Editors (ACP)'), link: '/features/entry-points/editors' },
          { text: withIcon('bot', 'Channels & Bots'), link: '/features/entry-points/channels' }
        ]
      },
      { text: withIcon('puzzle', 'Plugins & Tools'), link: '/features/plugins-tools' },
      { text: withIcon('plug', 'App Binding'), link: '/features/app' },
      { text: withIcon('users', 'SubAgents'), link: '/features/subagents' },
      { text: withIcon('network', 'Teams'), link: '/features/teams' },
      { text: withIcon('workflow', 'Automations & Goals'), link: '/features/automations' },
      { text: withIcon('activity', 'Observability'), link: '/features/observability' },
      { text: withIcon('shield', 'Security & Sandbox'), link: '/features/security' }
    ]
  },
  {
    text: 'Developing',
    items: [
      { text: withIcon('workflow', 'Spec-Driven Development'), link: '/developing/spec-driven-development' },
      { text: withIcon('branch', 'Architecture'), link: '/developing/architecture' },
      { text: withIcon('cog', 'Configuration Reference'), link: '/developing/configuration' },
      { text: withIcon('layers', 'Settings Lifecycle'), link: '/developing/settings-lifecycle' },
      { text: withIcon('server', 'AppServer Mode'), link: '/developing/appserver' },
      { text: withIcon('network', 'Hub Local Coordination'), link: '/developing/hub' },
      { text: withIcon('fileCode', 'AppServer Protocol'), link: '/developing/appserver-protocol' },
      { text: withIcon('fileCode', 'Hub Protocol'), link: '/developing/hub-protocol' },
      { text: withIcon('fileCode', 'Dashboard API'), link: '/developing/dashboard-api' },
      {
        text: withIcon('package', 'SDKs'),
        link: '/developing/sdk',
        collapsed: false,
        items: [
          { text: withIcon('package', 'Overview'), link: '/developing/sdk' },
          { text: withIcon('typescript', 'TypeScript'), link: '/developing/sdk-typescript' },
          { text: withIcon('code', '.NET'), link: '/developing/sdk-dotnet' },
          { text: withIcon('python', 'Python'), link: '/developing/sdk-python' }
        ]
      },
      { text: withIcon('plug', 'App Binding Integration'), link: '/developing/app-binding' },
      { text: withIcon('package', 'TypeScript Module Integration'), link: '/developing/typescript-module' },
      {
        text: withIcon('plug', 'Channel Adapters'),
        collapsed: true,
        items: [
          { text: withIcon('plug', 'QQ'), link: '/developing/channels/qq' },
          { text: withIcon('plug', 'WeCom'), link: '/developing/channels/wecom' },
          { text: withIcon('plug', 'Feishu'), link: '/developing/channels/feishu' },
          { text: withIcon('plug', 'Telegram (TypeScript)'), link: '/developing/channels/telegram' },
          { text: withIcon('plug', 'Weixin'), link: '/developing/channels/weixin' },
          { text: withIcon('plug', 'Telegram (Python)'), link: '/developing/channels/python-telegram' }
        ]
      }
    ]
  },
  {
    text: 'Resources',
    items: [
      { text: withIcon('book', 'Samples & Templates'), link: '/resources/samples' },
      { text: withIcon('tag', 'GitHub Releases'), link: 'https://github.com/DotHarness/dotcraft/releases' }
    ]
  }
]

const zhSidebar: DefaultTheme.Sidebar = [
  {
    text: '总览',
    items: [
      { text: withIcon('diamond', 'DotCraft 是什么'), link: '/zh/' },
      { text: withIcon('play', '快速开始'), link: '/zh/getting-started' }
    ]
  },
  {
    text: '功能',
    items: [
      { text: withIcon('folder', '项目级工作区'), link: '/zh/features/workspace' },
      { text: withIcon('brain', '长期记忆与 Dreams'), link: '/zh/features/memory' },
      { text: withIcon('sparkles', 'Skills 与自学习'), link: '/zh/features/skills' },
      { text: withIcon('layers', '统一会话核心'), link: '/zh/features/session-core' },
      {
        text: withIcon('grid', '入口'),
        link: '/zh/features/entry-points/',
        collapsed: false,
        items: [
          { text: withIcon('grid', '入口总览'), link: '/zh/features/entry-points/' },
          { text: withIcon('monitor', 'Desktop'), link: '/zh/features/entry-points/desktop' },
          { text: withIcon('terminal', 'TUI'), link: '/zh/features/entry-points/tui' },
          { text: withIcon('code', 'IDE / 编辑器（ACP）'), link: '/zh/features/entry-points/editors' },
          { text: withIcon('bot', 'Channels 与 Bots'), link: '/zh/features/entry-points/channels' }
        ]
      },
      { text: withIcon('puzzle', '插件与工具'), link: '/zh/features/plugins-tools' },
      { text: withIcon('plug', 'App Binding'), link: '/zh/features/app' },
      { text: withIcon('users', 'SubAgents'), link: '/zh/features/subagents' },
      { text: withIcon('network', 'Teams'), link: '/zh/features/teams' },
      { text: withIcon('workflow', 'Automations 与 Goals'), link: '/zh/features/automations' },
      { text: withIcon('activity', '可观测性'), link: '/zh/features/observability' },
      { text: withIcon('shield', '安全与沙箱'), link: '/zh/features/security' }
    ]
  },
  {
    text: '开发者',
    items: [
      { text: withIcon('workflow', 'Spec-Driven Development'), link: '/zh/developing/spec-driven-development' },
      { text: withIcon('branch', '架构总览'), link: '/zh/developing/architecture' },
      { text: withIcon('cog', '配置参考'), link: '/zh/developing/configuration' },
      { text: withIcon('layers', '设置生效层级'), link: '/zh/developing/settings-lifecycle' },
      { text: withIcon('server', 'AppServer 模式'), link: '/zh/developing/appserver' },
      { text: withIcon('network', 'Hub 本地协调'), link: '/zh/developing/hub' },
      { text: withIcon('fileCode', 'AppServer 协议'), link: '/zh/developing/appserver-protocol' },
      { text: withIcon('fileCode', 'Hub 协议'), link: '/zh/developing/hub-protocol' },
      { text: withIcon('fileCode', 'Dashboard API'), link: '/zh/developing/dashboard-api' },
      {
        text: withIcon('package', 'SDK'),
        link: '/zh/developing/sdk',
        collapsed: false,
        items: [
          { text: withIcon('package', '总览'), link: '/zh/developing/sdk' },
          { text: withIcon('typescript', 'TypeScript'), link: '/zh/developing/sdk-typescript' },
          { text: withIcon('code', '.NET'), link: '/zh/developing/sdk-dotnet' },
          { text: withIcon('python', 'Python'), link: '/zh/developing/sdk-python' }
        ]
      },
      { text: withIcon('plug', 'App Binding 集成'), link: '/zh/developing/app-binding' },
      { text: withIcon('package', 'TypeScript Module 集成'), link: '/zh/developing/typescript-module' },
      {
        text: withIcon('plug', 'Channel 适配器'),
        collapsed: true,
        items: [
          { text: withIcon('plug', 'QQ'), link: '/zh/developing/channels/qq' },
          { text: withIcon('plug', '企业微信'), link: '/zh/developing/channels/wecom' },
          { text: withIcon('plug', '飞书'), link: '/zh/developing/channels/feishu' },
          { text: withIcon('plug', 'Telegram (TypeScript)'), link: '/zh/developing/channels/telegram' },
          { text: withIcon('plug', '微信'), link: '/zh/developing/channels/weixin' },
          { text: withIcon('plug', 'Telegram (Python)'), link: '/zh/developing/channels/python-telegram' }
        ]
      }
    ]
  },
  {
    text: '资源',
    items: [
      { text: withIcon('book', '示例与模板'), link: '/zh/resources/samples' },
      { text: withIcon('tag', 'GitHub Releases'), link: 'https://github.com/DotHarness/dotcraft/releases' }
    ]
  }
]

const enNav: DefaultTheme.NavItem[] = [
  { text: 'Overview', link: '/' },
  { text: 'Getting Started', link: '/getting-started' },
  { text: 'Features', link: '/features/workspace' },
  { text: 'Developing', link: '/developing/spec-driven-development' },
  { text: 'Samples', link: '/resources/samples' }
]

const zhNav: DefaultTheme.NavItem[] = [
  { text: '总览', link: '/zh/' },
  { text: '快速开始', link: '/zh/getting-started' },
  { text: '功能', link: '/zh/features/workspace' },
  { text: '开发者', link: '/zh/developing/spec-driven-development' },
  { text: '示例', link: '/zh/resources/samples' }
]

const redirectMap: Record<string, string> = {
  'reference.md': 'developing/architecture.md',
  'features.md': 'features/workspace.md',
  'getting-started.md': 'getting-started.md',
  'config_guide.md': 'features/workspace.md',
  'desktop_guide.md': 'features/entry-points/desktop.md',
  'tui_guide.md': 'features/entry-points/tui.md',
  'acp_guide.md': 'features/entry-points/editors.md',
  'unity_guide.md': 'features/entry-points/editors.md',
  'appserver_guide.md': 'developing/appserver.md',
  'hub_guide.md': 'developing/hub.md',
  'dash_board_guide.md': 'features/observability.md',
  'subagents_guide.md': 'features/subagents.md',
  'external_cli_subagents_guide.md': 'features/subagents.md',
  'automations_guide.md': 'features/automations.md',
  'hooks_guide.md': 'features/automations.md',
  'automations/reference.md': 'features/automations.md',
  'hooks/reference.md': 'features/automations.md',
  'config/security.md': 'features/security.md',
  'settings-lifecycle.md': 'developing/settings-lifecycle.md',
  'typescript-module-integration.md': 'developing/typescript-module.md',
  'reference/config.md': 'developing/configuration.md',
  'reference/appserver-protocol.md': 'developing/appserver-protocol.md',
  'reference/hub-protocol.md': 'developing/hub-protocol.md',
  'reference/dashboard-api.md': 'developing/dashboard-api.md',
  'sdk/index.md': 'developing/sdk.md',
  'sdk/python.md': 'developing/sdk-python.md',
  'sdk/typescript.md': 'developing/sdk-typescript.md',
  'sdk/dotnet.md': 'developing/sdk-dotnet.md',
  'sdk/python-telegram.md': 'developing/channels/python-telegram.md',
  'sdk/typescript-feishu.md': 'developing/channels/feishu.md',
  'sdk/typescript-telegram.md': 'developing/channels/telegram.md',
  'sdk/typescript-weixin.md': 'developing/channels/weixin.md',
  'sdk/typescript-qq.md': 'developing/channels/qq.md',
  'sdk/typescript-wecom.md': 'developing/channels/wecom.md',
  'skills/agent-self-learning.md': 'features/skills.md',
  'skills/marketplace.md': 'features/skills.md',
  'plugins/install.md': 'features/plugins-tools.md',
  'plugins/build.md': 'features/plugins-tools.md',
  'samples/index.md': 'resources/samples.md',
  'samples/automations.md': 'resources/samples.md',
  'samples/bootstrap.md': 'resources/samples.md',
  'samples/hooks.md': 'resources/samples.md',
  'samples/skills.md': 'resources/samples.md',
  'samples/workspace.md': 'resources/samples.md'
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

export default withMermaid(defineConfig({
  title: 'DotCraft',
  description: 'AI Agent lives in your project. All in one workspace.',
  base,
  cleanUrls: true,
  lastUpdated: true,
  rewrites,
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
    logo: '/dotcraft-logo.svg',
    siteTitle: 'DotCraft',
    search: { provider: 'local' },
    socialLinks: [{ icon: 'github', link: repo }],
    editLink: {
      pattern: `${repo}/edit/master/docs/:path`,
      text: 'Edit this page on GitHub'
    },
    footer: {
      message: 'Apache License 2.0',
      copyright: 'Copyright © DotHarness'
    }
  },
  locales: {
    root: {
      label: 'English',
      lang: 'en-US',
      title: 'DotCraft',
      description: 'AI Agent lives in your project. All in one workspace.',
      themeConfig: {
        nav: enNav,
        sidebar: enSidebar,
        outline: { label: 'On this page' },
        editLink: {
          pattern: `${repo}/edit/master/docs/:path`,
          text: 'Edit this page on GitHub'
        }
      }
    },
    zh: {
      label: '简体中文',
      lang: 'zh-CN',
      title: 'DotCraft',
      description: '最适合您项目的 AI Agent，所有能力尽在工作区内。',
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
