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
      { text: withIcon('play', 'Getting Started'), link: '/getting-started' }
    ]
  },
  {
    text: 'Features',
    items: [
      {
        text: withIcon('cpu', 'Agent System'),
        link: '/features/agent-system/',
        items: [
          { text: withIcon('cpu', 'Overview'), link: '/features/agent-system/' },
          { text: withIcon('brain', 'Memory & Dreams'), link: '/features/agent-system/memory' },
          { text: withIcon('sparkles', 'Skills & Self-Learning'), link: '/features/agent-system/skills' },
          { text: withIcon('puzzle', 'Plugins & Tools'), link: '/features/agent-system/plugins-tools' },
          { text: withIcon('package', 'Plugin Marketplaces'), link: '/features/agent-system/plugin-marketplaces' },
          { text: withIcon('plug', 'Connected Apps'), link: '/features/agent-system/connected-apps' },
          { text: withIcon('automation', 'Automations & Goals'), link: '/features/agent-system/automations' },
          { text: withIcon('workflow', 'Dynamic Workflows'), link: '/features/agent-system/dynamic-workflows' },
          { text: withIcon('anchor', 'Lifecycle Hooks'), link: '/features/agent-system/hooks' },
          { text: withIcon('bot', 'Agent Profiles'), link: '/features/agent-system/agent-profiles' },
          { text: withIcon('users', 'Subagents'), link: '/features/agent-system/subagents' },
          { text: withIcon('network', 'Teams'), link: '/features/agent-system/teams' },
          { text: withIcon('share', 'Workspace Handoff'), link: '/features/agent-system/workspace-handoff' },
          { text: withIcon('activity', 'Observability'), link: '/features/self-hosted/observability' },
          { text: withIcon('shield', 'Security & Sandbox'), link: '/features/self-hosted/security' }
        ]
      },
      {
        text: withIcon('grid', 'Entry Points'),
        link: '/features/entry-points/',
        items: [
          { text: withIcon('globe', 'Overview'), link: '/features/entry-points/' },
          { text: withIcon('monitor', 'Desktop'), link: '/features/entry-points/desktop' },
          { text: withIcon('code', 'IDE / Editors (ACP)'), link: '/features/entry-points/editors' },
          { text: withIcon('cloud', 'Server Deployment'), link: '/features/self-hosted/server-deployment' }
        ]
      },
      {
        text: withIcon('bot', 'Channels & Bots'),
        link: '/features/channels/',
        items: [
          { text: withIcon('globe', 'Overview'), link: '/features/channels/' },
          { text: withIcon('qq', 'QQ'), link: '/features/channels/qq' },
          { text: withIcon('wecom', 'WeCom'), link: '/features/channels/wecom' },
          { text: withIcon('feishu', 'Feishu'), link: '/features/channels/feishu' },
          { text: withIcon('telegram', 'Telegram'), link: '/features/channels/telegram' },
          { text: withIcon('weixin', 'Weixin'), link: '/features/channels/weixin' },
          { text: withIcon('fileJson', 'Configuration Reference'), link: '/features/channels/reference' }
        ]
      },
      {
        text: withIcon('oratorio', 'Oratorio'),
        link: '/features/oratorio',
        items: [
          { text: withIcon('dashboard', 'Overview'), link: '/features/oratorio' },
          { text: withIcon('workflow', 'Workflow'), link: '/features/oratorio/workflow' },
          { text: withIcon('github', 'GitHub'), link: '/features/oratorio/github' },
          { text: withIcon('gitlab', 'GitLab'), link: '/features/oratorio/gitlab' },
          { text: withIcon('sliders', 'Settings'), link: '/features/oratorio/settings' }
        ]
      }
    ]
  },
  {
    text: 'Developing',
    items: [
      { text: withIcon('route', 'SDD Workflow'), link: '/developing/workflow/spec-driven-development' },
      {
        text: withIcon('activity', 'Debugging'),
        link: '/developing/debugging/desktop',
        items: [
          { text: withIcon('monitor', 'Desktop'), link: '/developing/debugging/desktop' }
        ]
      },
      {
        text: withIcon('waypoints', 'Architecture'),
        link: '/developing/architecture/overview',
        items: [
          { text: withIcon('branch', 'Overview'), link: '/developing/architecture/overview' },
          { text: withIcon('layers', 'Unified Session Core'), link: '/developing/architecture/session-core' },
          { text: withIcon('database', 'Session Persistence'), link: '/developing/architecture/session-persistence' }
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
        text: withIcon('harness', 'DotCraft Harness'),
        link: '/developing/harness/',
        items: [
          { text: withIcon('branch', 'Overview'), link: '/developing/harness/' },
          { text: withIcon('repeat', 'Hosting & Lifecycle'), link: '/developing/harness/hosting-lifecycle' },
          { text: withIcon('sliders', 'Configuration & Paths'), link: '/developing/harness/configuration-paths' },
          { text: withIcon('workflow', 'Threads & Turns'), link: '/developing/harness/threads-turns' },
          { text: withIcon('puzzle', 'Tools & Approvals'), link: '/developing/harness/tools-approvals' },
          { text: withIcon('cpu', 'Model Providers'), link: '/developing/harness/model-providers' },
          { text: withIcon('nuget', 'NuGet Package'), link: '/developing/harness/nuget-package' }
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
          { text: withIcon('mcp', 'MCP Runtime'), link: '/developing/sdks/mcp-runtime' },
          { text: withIcon('satelliteDish', 'Channel Adapters'), link: '/developing/sdks/channels' },
          { text: withIcon('typescript', 'TypeScript'), link: '/developing/sdks/typescript' },
          { text: withIcon('dotnet', '.NET'), link: '/developing/sdks/dotnet' }
        ]
      },
      {
        text: withIcon('plugZap', 'Integrations'),
        link: '/developing/integrations/app-binding',
        items: [
          { text: withIcon('plug', 'DotCraft App'), link: '/developing/integrations/app-binding' },
          { text: withIcon('package', 'Plugin Market'), link: '/developing/integrations/plugin-market' },
          { text: withIcon('mcp', 'MCP Apps'), link: '/developing/integrations/mcp-apps' },
          { text: withIcon('dotnet', '.NET Plugins'), link: '/developing/integrations/dotnet-plugins' },
          { text: withIcon('code', '.NET Plugin API'), link: '/developing/integrations/dotnet-plugin-reference' },
          { text: withIcon('dashboard', 'Desktop Plugins'), link: '/developing/integrations/desktop-plugins' },
          { text: withIcon('code', 'Desktop Plugin API'), link: '/developing/integrations/desktop-plugin-api' },
          { text: withIcon('oratorio', 'Oratorio'), link: '/developing/integrations/oratorio' },
          { text: withIcon('blocks', 'Channel Module'), link: '/developing/integrations/typescript-module' }
        ]
      }
    ]
  }
])

const zhSidebar: DefaultTheme.Sidebar = collapseSidebarGroups([
  {
    text: '总览',
    items: [
      { text: withIcon('play', '快速开始'), link: '/zh/getting-started' }
    ]
  },
  {
    text: '功能',
    items: [
      {
        text: withIcon('cpu', 'Agent 系统'),
        link: '/zh/features/agent-system/',
        items: [
          { text: withIcon('cpu', '总览'), link: '/zh/features/agent-system/' },
          { text: withIcon('brain', '长期记忆与梦境'), link: '/zh/features/agent-system/memory' },
          { text: withIcon('sparkles', '技能与自学习'), link: '/zh/features/agent-system/skills' },
          { text: withIcon('puzzle', '插件与工具'), link: '/zh/features/agent-system/plugins-tools' },
          { text: withIcon('package', '插件市场'), link: '/zh/features/agent-system/plugin-marketplaces' },
          { text: withIcon('plug', '应用连接'), link: '/zh/features/agent-system/connected-apps' },
          { text: withIcon('automation', '自动化与目标'), link: '/zh/features/agent-system/automations' },
          { text: withIcon('workflow', '动态工作流'), link: '/zh/features/agent-system/dynamic-workflows' },
          { text: withIcon('anchor', '生命周期 Hooks'), link: '/zh/features/agent-system/hooks' },
          { text: withIcon('bot', 'Agent 预设'), link: '/zh/features/agent-system/agent-profiles' },
          { text: withIcon('users', 'Subagents'), link: '/zh/features/agent-system/subagents' },
          { text: withIcon('network', 'Agent 团队'), link: '/zh/features/agent-system/teams' },
          { text: withIcon('share', '外部 Agent 协作'), link: '/zh/features/agent-system/workspace-handoff' },
          { text: withIcon('activity', '可观测性'), link: '/zh/features/self-hosted/observability' },
          { text: withIcon('shield', '安全与沙箱'), link: '/zh/features/self-hosted/security' }
        ]
      },
      {
        text: withIcon('grid', '入口'),
        link: '/zh/features/entry-points/',
        items: [
          { text: withIcon('globe', '入口总览'), link: '/zh/features/entry-points/' },
          { text: withIcon('monitor', 'Desktop'), link: '/zh/features/entry-points/desktop' },
          { text: withIcon('code', 'IDE / 编辑器（ACP）'), link: '/zh/features/entry-points/editors' },
          { text: withIcon('cloud', '服务器部署'), link: '/zh/features/self-hosted/server-deployment' }
        ]
      },
      {
        text: withIcon('bot', '社交渠道'),
        link: '/zh/features/channels/',
        items: [
          { text: withIcon('globe', '总览'), link: '/zh/features/channels/' },
          { text: withIcon('qq', 'QQ'), link: '/zh/features/channels/qq' },
          { text: withIcon('wecom', '企业微信'), link: '/zh/features/channels/wecom' },
          { text: withIcon('feishu', '飞书'), link: '/zh/features/channels/feishu' },
          { text: withIcon('telegram', 'Telegram'), link: '/zh/features/channels/telegram' },
          { text: withIcon('weixin', '微信'), link: '/zh/features/channels/weixin' },
          { text: withIcon('fileJson', '配置参考'), link: '/zh/features/channels/reference' }
        ]
      },
      {
        text: withIcon('oratorio', 'Oratorio'),
        link: '/zh/features/oratorio',
        items: [
          { text: withIcon('dashboard', '总览'), link: '/zh/features/oratorio' },
          { text: withIcon('workflow', '工作流'), link: '/zh/features/oratorio/workflow' },
          { text: withIcon('github', 'GitHub'), link: '/zh/features/oratorio/github' },
          { text: withIcon('gitlab', 'GitLab'), link: '/zh/features/oratorio/gitlab' },
          { text: withIcon('sliders', '设置'), link: '/zh/features/oratorio/settings' }
        ]
      }
    ]
  },
  {
    text: '开发',
    items: [
      { text: withIcon('route', 'SDD 工作流'), link: '/zh/developing/workflow/spec-driven-development' },
      {
        text: withIcon('activity', '调试'),
        link: '/zh/developing/debugging/desktop',
        items: [
          { text: withIcon('monitor', 'Desktop'), link: '/zh/developing/debugging/desktop' }
        ]
      },
      {
        text: withIcon('waypoints', '架构'),
        link: '/zh/developing/architecture/overview',
        items: [
          { text: withIcon('branch', '架构总览'), link: '/zh/developing/architecture/overview' },
          { text: withIcon('layers', '统一会话核心'), link: '/zh/developing/architecture/session-core' },
          { text: withIcon('database', '会话持久化'), link: '/zh/developing/architecture/session-persistence' }
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
        text: withIcon('harness', 'DotCraft Harness'),
        link: '/zh/developing/harness/',
        items: [
          { text: withIcon('branch', '总览'), link: '/zh/developing/harness/' },
          { text: withIcon('repeat', '托管与生命周期'), link: '/zh/developing/harness/hosting-lifecycle' },
          { text: withIcon('sliders', '配置与路径'), link: '/zh/developing/harness/configuration-paths' },
          { text: withIcon('workflow', '线程与轮次'), link: '/zh/developing/harness/threads-turns' },
          { text: withIcon('puzzle', '工具与审批'), link: '/zh/developing/harness/tools-approvals' },
          { text: withIcon('cpu', '模型 Provider'), link: '/zh/developing/harness/model-providers' },
          { text: withIcon('nuget', 'NuGet 包'), link: '/zh/developing/harness/nuget-package' }
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
          { text: withIcon('mcp', 'MCP 运行时'), link: '/zh/developing/sdks/mcp-runtime' },
          { text: withIcon('satelliteDish', '渠道适配器'), link: '/zh/developing/sdks/channels' },
          { text: withIcon('typescript', 'TypeScript'), link: '/zh/developing/sdks/typescript' },
          { text: withIcon('dotnet', '.NET'), link: '/zh/developing/sdks/dotnet' }
        ]
      },
      {
        text: withIcon('plugZap', '集成'),
        link: '/zh/developing/integrations/app-binding',
        items: [
          { text: withIcon('plug', 'DotCraft App'), link: '/zh/developing/integrations/app-binding' },
          { text: withIcon('package', '插件市场'), link: '/zh/developing/integrations/plugin-market' },
          { text: withIcon('mcp', 'MCP Apps'), link: '/zh/developing/integrations/mcp-apps' },
          { text: withIcon('dotnet', '.NET 插件'), link: '/zh/developing/integrations/dotnet-plugins' },
          { text: withIcon('code', '.NET 插件 API'), link: '/zh/developing/integrations/dotnet-plugin-reference' },
          { text: withIcon('dashboard', 'Desktop Plugins'), link: '/zh/developing/integrations/desktop-plugins' },
          { text: withIcon('code', 'Desktop Plugin API'), link: '/zh/developing/integrations/desktop-plugin-api' },
          { text: withIcon('oratorio', 'Oratorio'), link: '/zh/developing/integrations/oratorio' },
          { text: withIcon('blocks', '渠道模块'), link: '/zh/developing/integrations/typescript-module' }
        ]
      }
    ]
  }
])

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
      pattern: `${repo}/edit/master/docs/:path`,
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
          pattern: `${repo}/edit/master/docs/:path`,
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
