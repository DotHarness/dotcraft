import { defineConfig, type DefaultTheme } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'
import { withIcon } from './theme/icons'

const repo = 'https://github.com/DotHarness/dotcraft'
const base = process.env.VITEPRESS_BASE ?? '/'

function escapeMustaches(value: string): string {
  return value.replaceAll('{{', '&#123;&#123;').replaceAll('}}', '&#125;&#125;')
}

function collapseSidebarGroups(items: DefaultTheme.SidebarItem[], depth = 0): DefaultTheme.SidebarItem[] {
  return items.map((item) => {
    if (!item.items) return item

    const collapsedItems = collapseSidebarGroups(item.items, depth + 1)
    if (depth === 0) {
      return {
        ...item,
        items: collapsedItems
      }
    }

    return {
      ...item,
      collapsed: true,
      items: collapsedItems
    }
  })
}

const enSidebar: DefaultTheme.Sidebar = collapseSidebarGroups([
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
      { text: withIcon('folder', 'Project First'), link: '/features/project-first' },
      {
        text: withIcon('cpu', 'Agent System'),
        link: '/features/agent-system/memory',
        items: [
          { text: withIcon('brain', 'Memory & Dreams'), link: '/features/agent-system/memory' },
          { text: withIcon('sparkles', 'Skills & Self-Learning'), link: '/features/agent-system/skills' },
          { text: withIcon('puzzle', 'Plugins & Tools'), link: '/features/agent-system/plugins-tools' },
          { text: withIcon('workflow', 'Automations & Goals'), link: '/features/agent-system/automations' },
          { text: withIcon('users', 'SubAgents'), link: '/features/agent-system/subagents' },
          { text: withIcon('network', 'Teams'), link: '/features/agent-system/teams' }
        ]
      },
      {
        text: withIcon('grid', 'Entry Points'),
        link: '/features/entry-points/',
        items: [
          { text: withIcon('globe', 'Overview'), link: '/features/entry-points/' },
          { text: withIcon('monitor', 'Desktop'), link: '/features/entry-points/desktop' },
          { text: withIcon('terminal', 'TUI'), link: '/features/entry-points/tui' },
          { text: withIcon('code', 'IDE / Editors (ACP)'), link: '/features/entry-points/editors' },
          { text: withIcon('bot', 'Channels & Bots'), link: '/features/entry-points/channels' }
        ]
      },
      {
        text: withIcon('lockKeyhole', 'Self-hosted Control'),
        link: '/features/self-hosted/observability',
        items: [
          { text: withIcon('cloud', 'Server Deployment'), link: '/features/self-hosted/server-deployment' },
          { text: withIcon('activity', 'Observability'), link: '/features/self-hosted/observability' },
          { text: withIcon('shield', 'Security & Sandbox'), link: '/features/self-hosted/security' }
        ]
      }
    ]
  },
  {
    text: 'Developing',
    items: [
      {
        text: withIcon('route', 'Workflow'),
        link: '/developing/workflow/spec-driven-development',
        items: [
          { text: withIcon('scrollText', 'Spec-Driven Development'), link: '/developing/workflow/spec-driven-development' },
          { text: withIcon('share', 'Workspace Handoff'), link: '/developing/workflow/workspace-handoff' }
        ]
      },
      {
        text: withIcon('waypoints', 'Architecture'),
        link: '/developing/architecture/overview',
        items: [
          { text: withIcon('branch', 'Overview'), link: '/developing/architecture/overview' },
          { text: withIcon('layers', 'Unified Session Core'), link: '/developing/architecture/session-core' }
        ]
      },
      { text: withIcon('sliders', 'Configuration'), link: '/developing/configuration' },
      {
        text: withIcon('repeat', 'Lifecycle'),
        link: '/developing/lifecycle/settings-lifecycle',
        items: [
          { text: withIcon('history', 'Settings Lifecycle'), link: '/developing/lifecycle/settings-lifecycle' },
          { text: withIcon('server', 'AppServer Mode'), link: '/developing/lifecycle/appserver' },
          { text: withIcon('radio', 'Hub Local Coordination'), link: '/developing/lifecycle/hub' }
        ]
      },
      {
        text: withIcon('webhook', 'Protocols & APIs'),
        link: '/developing/protocols/appserver-protocol',
        items: [
          { text: withIcon('fileJson', 'AppServer Protocol'), link: '/developing/protocols/appserver-protocol' },
          { text: withIcon('antenna', 'Hub Protocol'), link: '/developing/protocols/hub-protocol' },
          { text: withIcon('dashboard', 'Dashboard API'), link: '/developing/protocols/dashboard-api' }
        ]
      },
      {
        text: withIcon('boxes', 'SDKs'),
        link: '/developing/sdks/',
        items: [
          { text: withIcon('package', 'Overview'), link: '/developing/sdks/' },
          { text: withIcon('rocket', 'Quickstart'), link: '/developing/sdks/quickstart' },
          { text: withIcon('workflow', 'Threads & Runs'), link: '/developing/sdks/runs' },
          { text: withIcon('puzzle', 'Tools & Approvals'), link: '/developing/sdks/tools' },
          { text: withIcon('satelliteDish', 'Channel Adapters'), link: '/developing/sdks/channels' },
          { text: withIcon('typescript', 'TypeScript'), link: '/developing/sdks/typescript' },
          { text: withIcon('fileCode', '.NET'), link: '/developing/sdks/dotnet' },
          { text: withIcon('python', 'Python'), link: '/developing/sdks/python' }
        ]
      },
      {
        text: withIcon('plugZap', 'Integrations'),
        link: '/developing/integrations/app-binding',
        items: [
          { text: withIcon('plug', 'App Binding'), link: '/developing/integrations/app-binding' },
          { text: withIcon('box', 'Build an App'), link: '/developing/integrations/build-an-app' },
          { text: withIcon('layout', 'Interactive Tool UI'), link: '/developing/integrations/interactive-tool-ui' },
          { text: withIcon('dashboard', 'Desktop Extensions'), link: '/developing/integrations/desktop-extensions' },
          { text: withIcon('blocks', 'TypeScript Module'), link: '/developing/integrations/typescript-module' }
        ]
      },
      {
        text: withIcon('satelliteDish', 'Channels'),
        items: [
          { text: withIcon('messageSquare', 'QQ'), link: '/developing/channels/qq' },
          { text: withIcon('building', 'WeCom'), link: '/developing/channels/wecom' },
          { text: withIcon('feather', 'Feishu'), link: '/developing/channels/feishu' },
          { text: withIcon('send', 'Telegram (TypeScript)'), link: '/developing/channels/telegram' },
          { text: withIcon('messagesSquare', 'Weixin'), link: '/developing/channels/weixin' },
          { text: withIcon('botMessage', 'Telegram (Python)'), link: '/developing/channels/python-telegram' }
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
])

const zhSidebar: DefaultTheme.Sidebar = collapseSidebarGroups([
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
      { text: withIcon('folder', '项目优先'), link: '/zh/features/project-first' },
      {
        text: withIcon('cpu', 'Agent 系统'),
        link: '/zh/features/agent-system/memory',
        items: [
          { text: withIcon('brain', '长期记忆与 Dreams'), link: '/zh/features/agent-system/memory' },
          { text: withIcon('sparkles', 'Skills 与自学习'), link: '/zh/features/agent-system/skills' },
          { text: withIcon('puzzle', '插件与工具'), link: '/zh/features/agent-system/plugins-tools' },
          { text: withIcon('workflow', 'Automations 与 Goals'), link: '/zh/features/agent-system/automations' },
          { text: withIcon('users', 'SubAgents'), link: '/zh/features/agent-system/subagents' },
          { text: withIcon('network', 'Teams'), link: '/zh/features/agent-system/teams' }
        ]
      },
      {
        text: withIcon('grid', '入口'),
        link: '/zh/features/entry-points/',
        items: [
          { text: withIcon('globe', '入口总览'), link: '/zh/features/entry-points/' },
          { text: withIcon('monitor', 'Desktop'), link: '/zh/features/entry-points/desktop' },
          { text: withIcon('terminal', 'TUI'), link: '/zh/features/entry-points/tui' },
          { text: withIcon('code', 'IDE / 编辑器（ACP）'), link: '/zh/features/entry-points/editors' },
          { text: withIcon('bot', 'Channels 与 Bots'), link: '/zh/features/entry-points/channels' }
        ]
      },
      {
        text: withIcon('lockKeyhole', 'Self-hosted Control'),
        link: '/zh/features/self-hosted/observability',
        items: [
          { text: withIcon('cloud', '服务器部署'), link: '/zh/features/self-hosted/server-deployment' },
          { text: withIcon('activity', '可观测性'), link: '/zh/features/self-hosted/observability' },
          { text: withIcon('shield', '安全与沙箱'), link: '/zh/features/self-hosted/security' }
        ]
      }
    ]
  },
  {
    text: '开发',
    items: [
      {
        text: withIcon('route', '工作流'),
        link: '/zh/developing/workflow/spec-driven-development',
        items: [
          { text: withIcon('scrollText', 'Spec-Driven Development'), link: '/zh/developing/workflow/spec-driven-development' },
          { text: withIcon('share', '外部 Agent 协作'), link: '/zh/developing/workflow/workspace-handoff' }
        ]
      },
      {
        text: withIcon('waypoints', '架构'),
        link: '/zh/developing/architecture/overview',
        items: [
          { text: withIcon('branch', '架构总览'), link: '/zh/developing/architecture/overview' },
          { text: withIcon('layers', '统一会话核心'), link: '/zh/developing/architecture/session-core' }
        ]
      },
      { text: withIcon('sliders', '配置'), link: '/zh/developing/configuration' },
      {
        text: withIcon('repeat', '生命周期'),
        link: '/zh/developing/lifecycle/settings-lifecycle',
        items: [
          { text: withIcon('history', '设置生效层级'), link: '/zh/developing/lifecycle/settings-lifecycle' },
          { text: withIcon('server', 'AppServer 模式'), link: '/zh/developing/lifecycle/appserver' },
          { text: withIcon('radio', 'Hub 本地协调'), link: '/zh/developing/lifecycle/hub' }
        ]
      },
      {
        text: withIcon('webhook', '协议与 API'),
        link: '/zh/developing/protocols/appserver-protocol',
        items: [
          { text: withIcon('fileJson', 'AppServer 协议'), link: '/zh/developing/protocols/appserver-protocol' },
          { text: withIcon('antenna', 'Hub 协议'), link: '/zh/developing/protocols/hub-protocol' },
          { text: withIcon('dashboard', 'Dashboard API'), link: '/zh/developing/protocols/dashboard-api' }
        ]
      },
      {
        text: withIcon('boxes', 'SDK'),
        link: '/zh/developing/sdks/',
        items: [
          { text: withIcon('package', '总览'), link: '/zh/developing/sdks/' },
          { text: withIcon('rocket', '快速开始'), link: '/zh/developing/sdks/quickstart' },
          { text: withIcon('workflow', '线程与运行'), link: '/zh/developing/sdks/runs' },
          { text: withIcon('puzzle', '工具与审批'), link: '/zh/developing/sdks/tools' },
          { text: withIcon('satelliteDish', '渠道适配器'), link: '/zh/developing/sdks/channels' },
          { text: withIcon('typescript', 'TypeScript'), link: '/zh/developing/sdks/typescript' },
          { text: withIcon('fileCode', '.NET'), link: '/zh/developing/sdks/dotnet' },
          { text: withIcon('python', 'Python'), link: '/zh/developing/sdks/python' }
        ]
      },
      {
        text: withIcon('plugZap', '集成'),
        link: '/zh/developing/integrations/app-binding',
        items: [
          { text: withIcon('plug', 'App Binding'), link: '/zh/developing/integrations/app-binding' },
          { text: withIcon('box', '构建应用'), link: '/zh/developing/integrations/build-an-app' },
          { text: withIcon('layout', '交互式工具 UI'), link: '/zh/developing/integrations/interactive-tool-ui' },
          { text: withIcon('dashboard', 'Desktop 扩展'), link: '/zh/developing/integrations/desktop-extensions' },
          { text: withIcon('blocks', 'TypeScript Module'), link: '/zh/developing/integrations/typescript-module' }
        ]
      },
      {
        text: withIcon('satelliteDish', 'Channels'),
        items: [
          { text: withIcon('messageSquare', 'QQ'), link: '/zh/developing/channels/qq' },
          { text: withIcon('building', '企业微信'), link: '/zh/developing/channels/wecom' },
          { text: withIcon('feather', '飞书'), link: '/zh/developing/channels/feishu' },
          { text: withIcon('send', 'Telegram (TypeScript)'), link: '/zh/developing/channels/telegram' },
          { text: withIcon('messagesSquare', '微信'), link: '/zh/developing/channels/weixin' },
          { text: withIcon('botMessage', 'Telegram (Python)'), link: '/zh/developing/channels/python-telegram' }
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
])

const enNav: DefaultTheme.NavItem[] = [
  { text: 'Overview', link: '/' },
  { text: 'Getting Started', link: '/getting-started' },
  { text: 'Features', link: '/features/project-first' },
  { text: 'Developing', link: '/developing/workflow/spec-driven-development' },
  { text: 'Samples', link: '/resources/samples' }
]

const zhNav: DefaultTheme.NavItem[] = [
  { text: '总览', link: '/zh/' },
  { text: '快速开始', link: '/zh/getting-started' },
  { text: '功能', link: '/zh/features/project-first' },
  { text: '开发', link: '/zh/developing/workflow/spec-driven-development' },
  { text: '示例', link: '/zh/resources/samples' }
]

const redirectMap: Record<string, string> = {
  'reference.md': 'developing/architecture/overview.md',
  'features.md': 'features/project-first.md',
  'getting-started.md': 'getting-started.md',
  'config_guide.md': 'features/project-first.md',
  'desktop_guide.md': 'features/entry-points/desktop.md',
  'tui_guide.md': 'features/entry-points/tui.md',
  'acp_guide.md': 'features/entry-points/editors.md',
  'unity_guide.md': 'features/entry-points/editors.md',
  'appserver_guide.md': 'developing/lifecycle/appserver.md',
  'hub_guide.md': 'developing/lifecycle/hub.md',
  'dash_board_guide.md': 'features/self-hosted/observability.md',
  'subagents_guide.md': 'features/agent-system/subagents.md',
  'external_cli_subagents_guide.md': 'features/agent-system/subagents.md',
  'automations_guide.md': 'features/agent-system/automations.md',
  'hooks_guide.md': 'features/agent-system/automations.md',
  'automations/reference.md': 'features/agent-system/automations.md',
  'hooks/reference.md': 'features/agent-system/automations.md',
  'config/security.md': 'features/self-hosted/security.md',
  'settings-lifecycle.md': 'developing/lifecycle/settings-lifecycle.md',
  'features/workspace-handoff.md': 'developing/workflow/workspace-handoff.md',
  'developing/context-export-cli.md': 'developing/workflow/workspace-handoff.md',
  'typescript-module-integration.md': 'developing/integrations/typescript-module.md',
  'reference/config.md': 'developing/configuration.md',
  'reference/appserver-protocol.md': 'developing/protocols/appserver-protocol.md',
  'reference/hub-protocol.md': 'developing/protocols/hub-protocol.md',
  'reference/dashboard-api.md': 'developing/protocols/dashboard-api.md',
  'sdk/index.md': 'developing/sdks/index.md',
  'sdk/python.md': 'developing/sdks/python.md',
  'sdk/typescript.md': 'developing/sdks/typescript.md',
  'sdk/dotnet.md': 'developing/sdks/dotnet.md',
  'sdk/python-telegram.md': 'developing/channels/python-telegram.md',
  'sdk/typescript-feishu.md': 'developing/channels/feishu.md',
  'sdk/typescript-telegram.md': 'developing/channels/telegram.md',
  'sdk/typescript-weixin.md': 'developing/channels/weixin.md',
  'sdk/typescript-qq.md': 'developing/channels/qq.md',
  'sdk/typescript-wecom.md': 'developing/channels/wecom.md',
  'skills/agent-self-learning.md': 'features/agent-system/skills.md',
  'skills/marketplace.md': 'features/agent-system/skills.md',
  'plugins/install.md': 'features/agent-system/plugins-tools.md',
  'plugins/build.md': 'features/agent-system/plugins-tools.md',
  'features/workspace.md': 'features/project-first.md',
  'features/project-design/workspace.md': 'features/project-first.md',
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
  'developing/workspace-handoff.md': 'developing/workflow/workspace-handoff.md',
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
  'developing/sdk-python.md': 'developing/sdks/python.md',
  'developing/app-binding.md': 'developing/integrations/app-binding.md',
  'developing/typescript-module.md': 'developing/integrations/typescript-module.md',
  'samples/index.md': 'resources/samples.md',
  'samples/automations.md': 'resources/samples.md',
  'samples/bootstrap.md': 'resources/samples.md',
  'samples/hooks.md': 'resources/samples.md',
  'samples/skills.md': 'resources/samples.md'
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
