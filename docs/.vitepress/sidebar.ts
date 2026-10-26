import type { DefaultTheme } from 'vitepress'
import { withIcon } from './theme/icons'

export interface DocSection {
  text: string
  base: string
  link: string
  groups: DefaultTheme.SidebarItem[]
}

export const enSections: DocSection[] = [
  {
    text: 'Get started',
    base: '/start/',
    link: '/start/',
    groups: [
      {
        text: 'Get started',
        items: [
          { text: withIcon('play', 'Getting Started'), link: '/start/' }
        ]
      },
      {
        text: 'Entry points',
        items: [
          { text: withIcon('grid', 'Overview'), link: '/start/entry-points' },
          { text: withIcon('monitor', 'Desktop'), link: '/start/desktop' },
          { text: withIcon('code', 'IDE / Editors (ACP)'), link: '/start/editors' },
          { text: withIcon('smartphone', 'Phone app'), link: '/start/mobile' }
        ]
      }
    ]
  },
  {
    text: 'Use DotCraft',
    base: '/guide/',
    link: '/guide/',
    groups: [
      {
        text: 'Use DotCraft',
        items: [
          { text: withIcon('cpu', 'Overview'), link: '/guide/' }
        ]
      },
      {
        text: 'Conversations',
        items: [
          { text: withIcon('pointerClick', 'Interactive Answers'), link: '/guide/interactive-answers' },
          { text: withIcon('chartLine', 'Visualizations'), link: '/guide/visualizations' },
          { text: withIcon('users', 'Subagents'), link: '/guide/subagents' },
          { text: withIcon('branch', 'Worktrees'), link: '/guide/worktrees' },
          { text: withIcon('share', 'Workspace Handoff'), link: '/guide/workspace-handoff' },
          { text: withIcon('download', 'Import From Agents'), link: '/guide/session-import' }
        ]
      },
      {
        text: 'Agents',
        items: [
          { text: withIcon('bot', 'Agent Profiles'), link: '/guide/agent-profiles' },
          { text: withIcon('sparkles', 'Skills'), link: '/guide/skills' },
          { text: withIcon('brain', 'Memory & Dreams'), link: '/guide/memory' },
          { text: withIcon('anchor', 'Lifecycle Hooks'), link: '/guide/hooks' }
        ]
      },
      {
        text: 'Automation',
        items: [
          { text: withIcon('automation', 'Automations & Goals'), link: '/guide/automations' },
          { text: withIcon('workflow', 'Dynamic Workflows'), link: '/guide/dynamic-workflows' }
        ]
      },
      {
        text: 'Tools & apps',
        items: [
          { text: withIcon('puzzle', 'Plugins & Tools'), link: '/guide/plugins-tools' },
          { text: withIcon('package', 'Plugin Marketplaces'), link: '/guide/plugin-marketplaces' },
          { text: withIcon('plug', 'Connected Apps'), link: '/guide/connected-apps' },
          { text: withIcon('monitor', 'Computer Use'), link: '/guide/computer-use' },
          { text: withIcon('satelliteDish', 'DotCraft Satellite'), link: '/guide/satellite' }
        ]
      },
      {
        text: 'Channels & bots',
        items: [
          { text: withIcon('globe', 'Overview'), link: '/guide/channels/' },
          { text: withIcon('qq', 'QQ'), link: '/guide/channels/qq' },
          { text: withIcon('wecom', 'WeCom'), link: '/guide/channels/wecom' },
          { text: withIcon('feishu', 'Feishu'), link: '/guide/channels/feishu' },
          { text: withIcon('telegram', 'Telegram'), link: '/guide/channels/telegram' },
          { text: withIcon('weixin', 'Weixin'), link: '/guide/channels/weixin' }
        ]
      },
      {
        text: 'Oratorio',
        items: [
          { text: withIcon('oratorio', 'Overview'), link: '/guide/oratorio/' },
          { text: withIcon('workflow', 'Workflow'), link: '/guide/oratorio/workflow' },
          { text: withIcon('github', 'GitHub'), link: '/guide/oratorio/github' },
          { text: withIcon('gitlab', 'GitLab'), link: '/guide/oratorio/gitlab' },
          { text: withIcon('sliders', 'Settings'), link: '/guide/oratorio/settings' }
        ]
      }
    ]
  },
  {
    text: 'Self-hosting',
    base: '/self-hosting/',
    link: '/self-hosting/server-deployment',
    groups: [
      {
        text: 'Deployment',
        items: [
          { text: withIcon('server', 'Server Deployment'), link: '/self-hosting/server-deployment' },
          { text: withIcon('cloud', 'Model Service'), link: '/self-hosting/model-service' }
        ]
      },
      {
        text: 'Operations',
        items: [
          { text: withIcon('activity', 'Observability'), link: '/self-hosting/observability' },
          { text: withIcon('shield', 'Security'), link: '/self-hosting/security' }
        ]
      }
    ]
  },
  {
    text: 'Configuration',
    base: '/configuration/',
    link: '/configuration/',
    groups: [
      {
        text: 'Configuration',
        items: [
          { text: withIcon('sliders', 'Overview'), link: '/configuration/' },
          { text: withIcon('history', 'Settings Lifecycle'), link: '/configuration/settings-lifecycle' }
        ]
      },
      {
        text: 'Models & conversations',
        items: [
          { text: withIcon('cpu', 'Models & Providers'), link: '/configuration/models' },
          { text: withIcon('messageSquare', 'Conversations'), link: '/configuration/conversations' },
          { text: withIcon('brain', 'Memory & Skills'), link: '/configuration/memory-skills' }
        ]
      },
      {
        text: 'Agents',
        items: [
          { text: withIcon('users', 'Subagents'), link: '/configuration/subagents' },
          { text: withIcon('automation', 'Automations & Goals'), link: '/configuration/automations' },
          { text: withIcon('anchor', 'Hooks'), link: '/configuration/hooks' }
        ]
      },
      {
        text: 'Tools & permissions',
        items: [
          { text: withIcon('cog', 'Tools'), link: '/configuration/tools' },
          { text: withIcon('shield', 'Permissions'), link: '/configuration/permissions' },
          { text: withIcon('puzzle', 'Plugins, MCP & LSP'), link: '/configuration/plugins' }
        ]
      },
      {
        text: 'Hosts',
        items: [
          { text: withIcon('server', 'Entry Points & Services'), link: '/configuration/services' },
          { text: withIcon('globe', 'Channels'), link: '/configuration/channels' }
        ]
      }
    ]
  },
  {
    text: 'Extend',
    base: '/extend/',
    link: '/extend/desktop-extensions',
    groups: [
      {
        text: 'Plugins',
        items: [
          { text: withIcon('dashboard', 'Desktop Extensions'), link: '/extend/desktop-extensions' },
          { text: withIcon('code', 'Desktop Extension API'), link: '/extend/desktop-extension-api' },
          { text: withIcon('dotnet', '.NET Extensions'), link: '/extend/dotnet-extensions' },
          { text: withIcon('code', '.NET Extension API'), link: '/extend/dotnet-extension-api' },
          { text: withIcon('package', 'Plugin Market'), link: '/extend/plugin-market' }
        ]
      },
      {
        text: 'Apps & channels',
        items: [
          { text: withIcon('plug', 'DotCraft App'), link: '/extend/app-binding' },
          { text: withIcon('mcp', 'MCP Apps'), link: '/extend/mcp-apps' },
          { text: withIcon('blocks', 'Channel Modules'), link: '/extend/channel-modules' }
        ]
      },
      {
        text: 'SDKs',
        items: [
          { text: withIcon('package', 'Overview'), link: '/extend/sdks/' },
          { text: withIcon('rocket', 'Quickstart'), link: '/extend/sdks/quickstart' },
          { text: withIcon('workflow', 'Threads & Runs'), link: '/extend/sdks/runs' },
          { text: withIcon('puzzle', 'Tools & Approvals'), link: '/extend/sdks/tools' },
          { text: withIcon('mcp', 'MCP Runtime'), link: '/extend/sdks/mcp-runtime' },
          { text: withIcon('satelliteDish', 'Channel Adapters'), link: '/extend/sdks/channels' },
          { text: withIcon('typescript', 'TypeScript'), link: '/extend/sdks/typescript' },
          { text: withIcon('dotnet', '.NET'), link: '/extend/sdks/dotnet' }
        ]
      },
      {
        text: 'DotCraft Harness',
        items: [
          { text: withIcon('harness', 'Overview'), link: '/extend/harness/' },
          { text: withIcon('nuget', 'NuGet Package'), link: '/extend/harness/nuget-package' },
          { text: withIcon('repeat', 'Hosting & Lifecycle'), link: '/extend/harness/hosting-lifecycle' },
          { text: withIcon('sliders', 'Configuration & Paths'), link: '/extend/harness/configuration-paths' },
          { text: withIcon('workflow', 'Threads & Turns'), link: '/extend/harness/threads-turns' },
          { text: withIcon('puzzle', 'Tools & Approvals'), link: '/extend/harness/tools-approvals' },
          { text: withIcon('cpu', 'Model Providers'), link: '/extend/harness/model-providers' }
        ]
      },
      {
        text: 'Protocols & APIs',
        items: [
          { text: withIcon('fileJson', 'AppServer Protocol'), link: '/extend/protocols/appserver' },
          { text: withIcon('antenna', 'Hub Protocol'), link: '/extend/protocols/hub' },
          { text: withIcon('dashboard', 'Dashboard API'), link: '/extend/protocols/dashboard-api' }
        ]
      }
    ]
  },
  {
    text: 'Contribute',
    base: '/contribute/',
    link: '/contribute/spec-driven-development',
    groups: [
      {
        text: 'Development',
        items: [
          { text: withIcon('route', 'SDD Workflow'), link: '/contribute/spec-driven-development' },
          { text: withIcon('monitor', 'Debug Desktop'), link: '/contribute/debug-desktop' }
        ]
      },
      {
        text: 'Architecture',
        items: [
          { text: withIcon('waypoints', 'Overview'), link: '/contribute/architecture/' },
          { text: withIcon('layers', 'Unified Session Core'), link: '/contribute/architecture/session-core' },
          { text: withIcon('database', 'Session Persistence'), link: '/contribute/architecture/session-persistence' },
          { text: withIcon('server', 'Remote Tool Calls'), link: '/contribute/architecture/remote-tool-host' },
          { text: withIcon('oratorio', 'Oratorio Integration'), link: '/contribute/architecture/oratorio' }
        ]
      },
      {
        text: 'Lifecycle',
        items: [
          { text: withIcon('server', 'AppServer Mode'), link: '/contribute/lifecycle/appserver' },
          { text: withIcon('radio', 'Hub Local Coordination'), link: '/contribute/lifecycle/hub' }
        ]
      }
    ]
  }
]

export const zhSections: DocSection[] = [
  {
    text: '快速开始',
    base: '/zh/start/',
    link: '/zh/start/',
    groups: [
      {
        text: '快速开始',
        items: [
          { text: withIcon('play', '快速开始'), link: '/zh/start/' }
        ]
      },
      {
        text: '入口',
        items: [
          { text: withIcon('grid', '入口总览'), link: '/zh/start/entry-points' },
          { text: withIcon('monitor', 'Desktop'), link: '/zh/start/desktop' },
          { text: withIcon('code', 'IDE / 编辑器（ACP）'), link: '/zh/start/editors' },
          { text: withIcon('smartphone', '手机 App'), link: '/zh/start/mobile' }
        ]
      }
    ]
  },
  {
    text: '使用 DotCraft',
    base: '/zh/guide/',
    link: '/zh/guide/',
    groups: [
      {
        text: '使用 DotCraft',
        items: [
          { text: withIcon('cpu', '总览'), link: '/zh/guide/' }
        ]
      },
      {
        text: '对话',
        items: [
          { text: withIcon('pointerClick', '交互式回答'), link: '/zh/guide/interactive-answers' },
          { text: withIcon('chartLine', '交互式可视化'), link: '/zh/guide/visualizations' },
          { text: withIcon('users', 'Subagents'), link: '/zh/guide/subagents' },
          { text: withIcon('branch', '工作树'), link: '/zh/guide/worktrees' },
          { text: withIcon('share', '外部 Agent 协作'), link: '/zh/guide/workspace-handoff' },
          { text: withIcon('download', '从其他 Agent 导入'), link: '/zh/guide/session-import' }
        ]
      },
      {
        text: 'Agent',
        items: [
          { text: withIcon('bot', 'Agent 预设'), link: '/zh/guide/agent-profiles' },
          { text: withIcon('sparkles', '技能'), link: '/zh/guide/skills' },
          { text: withIcon('brain', '记忆与梦境'), link: '/zh/guide/memory' },
          { text: withIcon('anchor', '生命周期 Hooks'), link: '/zh/guide/hooks' }
        ]
      },
      {
        text: '自动化',
        items: [
          { text: withIcon('automation', '自动化与目标'), link: '/zh/guide/automations' },
          { text: withIcon('workflow', '动态工作流'), link: '/zh/guide/dynamic-workflows' }
        ]
      },
      {
        text: '工具与应用',
        items: [
          { text: withIcon('puzzle', '插件与工具'), link: '/zh/guide/plugins-tools' },
          { text: withIcon('package', '插件市场'), link: '/zh/guide/plugin-marketplaces' },
          { text: withIcon('plug', '应用连接'), link: '/zh/guide/connected-apps' },
          { text: withIcon('monitor', '电脑操控'), link: '/zh/guide/computer-use' },
          { text: withIcon('satelliteDish', 'DotCraft 卫星'), link: '/zh/guide/satellite' }
        ]
      },
      {
        text: '社交渠道',
        items: [
          { text: withIcon('globe', '总览'), link: '/zh/guide/channels/' },
          { text: withIcon('qq', 'QQ'), link: '/zh/guide/channels/qq' },
          { text: withIcon('wecom', '企业微信'), link: '/zh/guide/channels/wecom' },
          { text: withIcon('feishu', '飞书'), link: '/zh/guide/channels/feishu' },
          { text: withIcon('telegram', 'Telegram'), link: '/zh/guide/channels/telegram' },
          { text: withIcon('weixin', '微信'), link: '/zh/guide/channels/weixin' }
        ]
      },
      {
        text: 'Oratorio',
        items: [
          { text: withIcon('oratorio', '总览'), link: '/zh/guide/oratorio/' },
          { text: withIcon('workflow', '工作流'), link: '/zh/guide/oratorio/workflow' },
          { text: withIcon('github', 'GitHub'), link: '/zh/guide/oratorio/github' },
          { text: withIcon('gitlab', 'GitLab'), link: '/zh/guide/oratorio/gitlab' },
          { text: withIcon('sliders', '设置'), link: '/zh/guide/oratorio/settings' }
        ]
      }
    ]
  },
  {
    text: '自托管',
    base: '/zh/self-hosting/',
    link: '/zh/self-hosting/server-deployment',
    groups: [
      {
        text: '部署',
        items: [
          { text: withIcon('server', '服务器部署'), link: '/zh/self-hosting/server-deployment' },
          { text: withIcon('cloud', '模型服务'), link: '/zh/self-hosting/model-service' }
        ]
      },
      {
        text: '运维',
        items: [
          { text: withIcon('activity', '可观测性'), link: '/zh/self-hosting/observability' },
          { text: withIcon('shield', '安全'), link: '/zh/self-hosting/security' }
        ]
      }
    ]
  },
  {
    text: '配置',
    base: '/zh/configuration/',
    link: '/zh/configuration/',
    groups: [
      {
        text: '配置',
        items: [
          { text: withIcon('sliders', '总览'), link: '/zh/configuration/' },
          { text: withIcon('history', '设置生效层级'), link: '/zh/configuration/settings-lifecycle' }
        ]
      },
      {
        text: '模型与对话',
        items: [
          { text: withIcon('cpu', '模型与 Provider'), link: '/zh/configuration/models' },
          { text: withIcon('messageSquare', '对话'), link: '/zh/configuration/conversations' },
          { text: withIcon('brain', '记忆与技能'), link: '/zh/configuration/memory-skills' }
        ]
      },
      {
        text: '智能体',
        items: [
          { text: withIcon('users', 'Subagent'), link: '/zh/configuration/subagents' },
          { text: withIcon('automation', '自动化与目标'), link: '/zh/configuration/automations' },
          { text: withIcon('anchor', 'Hooks'), link: '/zh/configuration/hooks' }
        ]
      },
      {
        text: '工具与权限',
        items: [
          { text: withIcon('cog', '工具'), link: '/zh/configuration/tools' },
          { text: withIcon('shield', '权限'), link: '/zh/configuration/permissions' },
          { text: withIcon('puzzle', '插件、MCP 与 LSP'), link: '/zh/configuration/plugins' }
        ]
      },
      {
        text: '宿主',
        items: [
          { text: withIcon('server', '入口与服务'), link: '/zh/configuration/services' },
          { text: withIcon('globe', '渠道'), link: '/zh/configuration/channels' }
        ]
      }
    ]
  },
  {
    text: '扩展',
    base: '/zh/extend/',
    link: '/zh/extend/desktop-extensions',
    groups: [
      {
        text: '插件',
        items: [
          { text: withIcon('dashboard', '桌面扩展'), link: '/zh/extend/desktop-extensions' },
          { text: withIcon('code', '桌面扩展 API'), link: '/zh/extend/desktop-extension-api' },
          { text: withIcon('dotnet', '.NET 扩展'), link: '/zh/extend/dotnet-extensions' },
          { text: withIcon('code', '.NET 扩展 API'), link: '/zh/extend/dotnet-extension-api' },
          { text: withIcon('package', '插件市场'), link: '/zh/extend/plugin-market' }
        ]
      },
      {
        text: '应用与渠道',
        items: [
          { text: withIcon('plug', 'DotCraft App'), link: '/zh/extend/app-binding' },
          { text: withIcon('mcp', 'MCP Apps'), link: '/zh/extend/mcp-apps' },
          { text: withIcon('blocks', '渠道模块'), link: '/zh/extend/channel-modules' }
        ]
      },
      {
        text: 'SDK',
        items: [
          { text: withIcon('package', '总览'), link: '/zh/extend/sdks/' },
          { text: withIcon('rocket', '快速开始'), link: '/zh/extend/sdks/quickstart' },
          { text: withIcon('workflow', '线程与运行'), link: '/zh/extend/sdks/runs' },
          { text: withIcon('puzzle', '工具与审批'), link: '/zh/extend/sdks/tools' },
          { text: withIcon('mcp', 'MCP 运行时'), link: '/zh/extend/sdks/mcp-runtime' },
          { text: withIcon('satelliteDish', '渠道适配器'), link: '/zh/extend/sdks/channels' },
          { text: withIcon('typescript', 'TypeScript'), link: '/zh/extend/sdks/typescript' },
          { text: withIcon('dotnet', '.NET'), link: '/zh/extend/sdks/dotnet' }
        ]
      },
      {
        text: 'DotCraft Harness',
        items: [
          { text: withIcon('harness', '总览'), link: '/zh/extend/harness/' },
          { text: withIcon('nuget', 'NuGet 包'), link: '/zh/extend/harness/nuget-package' },
          { text: withIcon('repeat', '托管与生命周期'), link: '/zh/extend/harness/hosting-lifecycle' },
          { text: withIcon('sliders', '配置与路径'), link: '/zh/extend/harness/configuration-paths' },
          { text: withIcon('workflow', '线程与轮次'), link: '/zh/extend/harness/threads-turns' },
          { text: withIcon('puzzle', '工具与审批'), link: '/zh/extend/harness/tools-approvals' },
          { text: withIcon('cpu', '模型 Provider'), link: '/zh/extend/harness/model-providers' }
        ]
      },
      {
        text: '协议与 API',
        items: [
          { text: withIcon('fileJson', 'AppServer 协议'), link: '/zh/extend/protocols/appserver' },
          { text: withIcon('antenna', 'Hub 协议'), link: '/zh/extend/protocols/hub' },
          { text: withIcon('dashboard', 'Dashboard API'), link: '/zh/extend/protocols/dashboard-api' }
        ]
      }
    ]
  },
  {
    text: '参与贡献',
    base: '/zh/contribute/',
    link: '/zh/contribute/spec-driven-development',
    groups: [
      {
        text: '开发流程',
        items: [
          { text: withIcon('route', 'SDD 工作流'), link: '/zh/contribute/spec-driven-development' },
          { text: withIcon('monitor', '调试 Desktop'), link: '/zh/contribute/debug-desktop' }
        ]
      },
      {
        text: '架构',
        items: [
          { text: withIcon('waypoints', '架构总览'), link: '/zh/contribute/architecture/' },
          { text: withIcon('layers', '统一会话核心'), link: '/zh/contribute/architecture/session-core' },
          { text: withIcon('database', '会话持久化'), link: '/zh/contribute/architecture/session-persistence' },
          { text: withIcon('server', '远程工具调用'), link: '/zh/contribute/architecture/remote-tool-host' },
          { text: withIcon('oratorio', 'Oratorio 集成'), link: '/zh/contribute/architecture/oratorio' }
        ]
      },
      {
        text: '生命周期',
        items: [
          { text: withIcon('server', 'AppServer 模式'), link: '/zh/contribute/lifecycle/appserver' },
          { text: withIcon('radio', 'Hub 本地协调'), link: '/zh/contribute/lifecycle/hub' }
        ]
      }
    ]
  }
]

export function sectionNav(sections: DocSection[]): DefaultTheme.NavItemWithLink[] {
  return sections.map(({ text, base, link }) => ({ text, link, activeMatch: `^${base}` }))
}

export function sectionSidebar(sections: DocSection[]): DefaultTheme.SidebarMulti {
  return Object.fromEntries(sections.map(({ base, groups }) => [base, groups]))
}
