export type Locale = 'en' | 'zh'

export interface Link {
  text: string
  href: string
}

export interface Row {
  icon: string
  label: string
  hint: string
  href: string
}

export interface Story {
  icon: string
  name: string
  line: string
  media: { src: string; alt: string; width: number; height: number }
  links: Link[]
  team?: boolean
}

export interface Product {
  label: string
  hint: string
  get: string
  href: string
  art: string
}

const raw = 'https://github.com/DotHarness/resources/raw/master/'
const cdn = 'https://cdn.jsdelivr.net/gh/DotHarness/resources@master/'

function stories(base: string, text: { name: string; line: string; alt: string; links: string[] }[]): Story[] {
  const shape = [
    { icon: 'monitor', src: 'dotcraft/docs/tour-desktop', hrefs: ['/features/entry-points/desktop', '/getting-started'] },
    { icon: 'bot', src: 'dotcraft/docs/tour-agents', hrefs: ['/features/agent-system/agent-profiles', '/features/agent-system/subagents', '/features/agent-system/automations'], team: true },
    { icon: 'puzzle', src: 'dotcraft/docs/tour-plugins', hrefs: ['/developing/integrations/dotnet-plugins', '/developing/integrations/desktop-plugins'] },
    { icon: 'blocks', src: 'dotcraft/docs/tour-apps', hrefs: ['/developing/harness/', '/developing/integrations/app-binding', '/developing/sdks/'] }
  ]
  return shape.map((story, index) => ({
    icon: story.icon,
    name: text[index].name,
    line: text[index].line,
    media: { src: `${base}${story.src}.webp`, alt: text[index].alt, width: 1600, height: 900 },
    links: story.hrefs.map((href, at) => ({ text: text[index].links[at], href })),
    team: story.team
  }))
}

const productHrefs = {
  desktop: '/features/entry-points/desktop',
  mobile: '/features/entry-points/mobile',
  cli: '/features/entry-points/',
  satellite: '/features/agent-system/satellite',
  oratorio: '/features/oratorio',
  chatBots: '/features/channels/',
  harness: '/developing/harness/',
  sdks: '/developing/sdks/'
}

type ProductId = 'desktop' | 'mobile' | 'cli' | 'satellite' | 'oratorio' | 'chat-bots' | 'harness' | 'sdks' | 'avatar'

function products(rows: [ProductId, string, string, string, string][]): Product[] {
  return rows.map(([id, label, hint, get, href]) => ({ label, hint, get, href, art: `${raw}dotcraft/docs/product-${id}.webp` }))
}

export const homeCopy = {
  en: {
    hero: {
      eyebrow: 'Open source, built on .NET 10',
      lines: ['An agent runtime you', 'embed and extend'],
      stop: '.',
      lead: 'Run the app, or add the package.'
    },
    start: 'Get started',
    download: {
      fallback: 'Download release',
      forPlatform: 'Download for',
      choose: 'Choose platform',
      all: 'All releases',
      detected: 'Detected'
    },
    install: { tablist: 'CLI installation platform', copy: 'Copy', copied: 'Copied', copyLabel: 'Copy install command' },
    demo: {
      poster: raw + 'dotcraft/desktop_banner.png',
      alt: 'DotCraft Desktop preview',
      try: 'Try the live demo',
      load: 'Load the live demo',
      frame: 'DotCraft Desktop live demo'
    },
    why: {
      title: 'Why DotCraft?',
      lead: 'Run it as a desktop app, add it to your own .NET application, and extend both with plugins.',
      groups: [
        {
          title: 'Run the app',
          link: { text: 'Getting Started', href: '/getting-started' },
          rows: [
            { icon: 'sparkles', label: 'Ready out of the box', hint: 'Plan, subagents, automations and the in-app browser, built in.', href: '/features/agent-system/' },
            { icon: 'app-window', label: 'Works in your apps', hint: 'Agents operate the Windows apps you allow.', href: '/features/entry-points/desktop' },
            { icon: 'layers', label: 'Pick up anywhere', hint: 'Desktop, CLI, editors and chat bots share one workspace.', href: '/features/entry-points/' },
            { icon: 'server', label: 'Your models, your costs', hint: 'Any compatible provider or ChatGPT, with keys kept on one machine.', href: '/features/self-hosted/server-deployment' }
          ]
        },
        {
          title: 'Embed and extend',
          link: { text: 'DotCraft Harness', href: '/developing/harness/' },
          rows: [
            { icon: 'circuit-board', label: 'Build it into your product', hint: 'Embed the runtime behind Desktop in your .NET app.', href: '/developing/harness/' },
            { icon: 'plug-zap', label: 'Connect existing products', hint: 'Bring agents in through the SDKs and App Binding.', href: '/developing/sdks/' },
            { icon: 'dotnet', label: '.NET plugins', hint: 'Add tools and commands; the agent can write and hot-swap them.', href: '/developing/integrations/dotnet-plugins' },
            { icon: 'layout-dashboard', label: 'Desktop plugins', hint: "React plugins reshape Desktop's interface.", href: '/developing/integrations/desktop-plugins' }
          ]
        }
      ]
    },
    tour: {
      title: 'From the app to your own product.',
      readMore: 'Read more',
      stories: stories(raw, [
        { name: 'DotCraft Desktop', line: 'Plan, build, review, and automate in one app.', alt: 'DotCraft Desktop working through a Ship dark mode plan: two subagents run, the in-app browser checks the settings page, and a weekly contrast check is set up as an automation', links: ['Desktop', 'Getting Started'] },
        { name: 'Agent Builder + Profiles', line: 'Build your own Agent team through conversation.', alt: 'Agent Builder turning a short description into a contrast-checker agent with its own avatar, tools and skills', links: ['Agent Profiles', 'Subagents', 'Automations'] },
        { name: 'Plugins', line: 'Extend the runtime in C#, and Desktop in TypeScript.', alt: 'A .NET plugin adding review tools and lifecycle hooks, then a Desktop plugin changing the app wallpaper', links: ['.NET Plugins', 'Desktop Plugins'] },
        { name: 'Built for applications', line: 'Bring DotCraft into your own product.', alt: 'Code that hosts DotCraft Harness in a .NET app, binds a connected app to a thread, and streams a reply with the SDK', links: ['DotCraft Harness', 'DotCraft App', 'SDKs'] }
      ])
    },
    products: {
      title: 'Explore DotCraft',
      rows: products([
        ['desktop', 'Desktop', 'Plans, builds and checks the work in your projects.', 'Download', productHrefs.desktop],
        ['mobile', 'Mobile', 'Follow and answer your chats from your phone.', 'Download', productHrefs.mobile],
        ['cli', 'CLI', 'One command, and the answer is in your terminal.', 'Install script', productHrefs.cli],
        ['satellite', 'Satellite', 'Your agent works on another Windows PC.', 'Download', productHrefs.satellite],
        ['oratorio', 'Oratorio', 'Every task, from hand-off to review, on one board.', 'Built in', productHrefs.oratorio],
        ['chat-bots', 'Chat bots', 'Ask about your project right in the group chat.', 'Built in', productHrefs.chatBots],
        ['harness', 'Harness', 'The whole agent runtime, inside your .NET app.', 'NuGet', productHrefs.harness],
        ['sdks', 'SDKs', 'Make your own app a DotCraft client.', 'npm · NuGet', productHrefs.sdks],
        ['avatar', 'Avatar', 'A face for your agent that moves and dresses up.', 'npm', '/developing/sdks/typescript#avatar-package']
      ])
    },
    harness: {
      kicker: 'Agent Harness for .NET',
      title: 'Bring a complete agent runtime into any .NET application.',
      points: [
        { icon: 'cpu', title: 'Runs where .NET runs', text: 'Embed the full runtime directly in desktop, server, CLI, or automation applications. No separate agent service to deploy or operate.' },
        { icon: 'dotnet', title: 'Built the .NET way', text: 'Use familiar Generic Host and dependency injection patterns. Your application stays in control of configuration, lifecycle, and user experience.' },
        { icon: 'layers', title: 'More than an agent loop', text: 'Durable sessions, tools, skills, approvals, and model providers are already composed. Start with your product, not the plumbing.' }
      ],
      overview: { text: 'Harness overview', href: '/developing/harness/' },
      nuget: { text: 'NuGet package', href: '/developing/harness/nuget-package' },
      viewer: 'Harness example'
    },
    close: { title: 'All set — over to you.' },
    footer: { copyright: 'Copyright © DotHarness · Apache License 2.0', label: 'Project' }
  },
  zh: {
    hero: {
      eyebrow: '开源，基于 .NET 10 构建',
      lines: ['可嵌入、可扩展的', 'Agent Runtime'],
      stop: '。',
      lead: '直接运行，或装进你的应用。'
    },
    start: '开始使用',
    download: {
      fallback: '下载 Release',
      forPlatform: '下载',
      choose: '选择平台',
      all: '全部版本',
      detected: '当前系统'
    },
    install: { tablist: 'CLI 安装平台', copy: '复制', copied: '已复制', copyLabel: '复制安装命令' },
    demo: {
      poster: cdn + 'dotcraft/desktop_banner.png',
      alt: 'DotCraft Desktop 预览',
      try: '试一试 Live Demo',
      load: '加载交互演示',
      frame: 'DotCraft Desktop 交互演示'
    },
    why: {
      title: '为什么选择 DotCraft？',
      lead: '它可以作为桌面应用直接运行，也可以引入你自己的 .NET 应用，两者都能用插件扩展。',
      groups: [
        {
          title: '直接运行',
          link: { text: '快速开始', href: '/getting-started' },
          rows: [
            { icon: 'sparkles', label: '开箱即用', hint: '计划、子智能体、自动化和应用内浏览器都已内置。', href: '/features/agent-system/' },
            { icon: 'app-window', label: '操作你的应用', hint: 'Agent 可以操作你允许的 Windows 应用。', href: '/features/entry-points/desktop' },
            { icon: 'layers', label: '随处接着做', hint: 'Desktop、CLI、编辑器和聊天机器人共用一个工作区。', href: '/features/entry-points/' },
            { icon: 'server', label: '模型和成本由你掌控', hint: '任选兼容的模型服务或 ChatGPT 订阅，密钥可由一台机器统一保管。', href: '/features/self-hosted/server-deployment' }
          ]
        },
        {
          title: '嵌入与扩展',
          link: { text: 'DotCraft Harness', href: '/developing/harness/' },
          rows: [
            { icon: 'circuit-board', label: '装进你的产品', hint: '把 Desktop 背后的运行时嵌入你的 .NET 应用。', href: '/developing/harness/' },
            { icon: 'plug-zap', label: '接入现有产品', hint: '通过 SDK 和 App Binding 把 Agent 带进现有产品。', href: '/developing/sdks/' },
            { icon: 'dotnet', label: '.NET 插件', hint: '添加工具和命令，Agent 还能自己编写并热替换。', href: '/developing/integrations/dotnet-plugins' },
            { icon: 'layout-dashboard', label: 'Desktop 插件', hint: 'React 插件可以改造 Desktop 的界面。', href: '/developing/integrations/desktop-plugins' }
          ]
        }
      ]
    },
    tour: {
      title: '从应用到你自己的产品。',
      readMore: '了解更多',
      stories: stories(cdn, [
        { name: 'DotCraft Desktop', line: '规划、执行、审阅和自动化，都在一个桌面应用里完成。', alt: 'DotCraft Desktop 按「Ship dark mode」计划推进：两个子智能体并行工作，应用内浏览器检查设置页，再把每周对比度检查设为自动化任务', links: ['Desktop', '快速开始'] },
        { name: 'Agent Builder + Profiles', line: '通过对话，打造属于你的 Agent 团队。', alt: 'Agent Builder 把一段描述变成 contrast-checker Agent，带上它自己的形象、工具和技能', links: ['Agent Profiles', 'Subagents', '自动化'] },
        { name: '插件', line: '用 C# 扩展运行时，用 TypeScript 扩展 Desktop。', alt: '一个 .NET 插件添加评审工具和生命周期钩子，再由一个 Desktop 插件更换应用壁纸', links: ['.NET 插件', 'Desktop Plugins'] },
        { name: '为应用而生', line: '把 DotCraft 带进你自己的产品。', alt: '在 .NET 应用中托管 DotCraft Harness、把已连接的应用绑定到线程，并用 SDK 流式输出回复的代码', links: ['DotCraft Harness', 'DotCraft App', 'SDK'] }
      ])
    },
    products: {
      title: '探索 DotCraft',
      rows: products([
        ['desktop', 'Desktop', '在你的项目里规划、动手、自己验收。', '下载', productHrefs.desktop],
        ['mobile', '手机 App', '在手机上跟进和处理你的聊天。', '下载', productHrefs.mobile],
        ['cli', 'CLI', '一条命令，答案直接回到终端。', '安装脚本', productHrefs.cli],
        ['satellite', '卫星', '你的 Agent 在另一台 Windows 电脑上工作。', '下载', productHrefs.satellite],
        ['oratorio', 'Oratorio', '每个任务从派发到评审，都在一块看板上。', '已内置', productHrefs.oratorio],
        ['chat-bots', '聊天机器人', '在群聊里直接问项目的事。', '已内置', productHrefs.chatBots],
        ['harness', 'Harness', '整套 Agent 运行时，装进你的 .NET 应用。', 'NuGet', productHrefs.harness],
        ['sdks', 'SDK', '让你的应用成为 DotCraft 客户端。', 'npm · NuGet', productHrefs.sdks],
        ['avatar', 'Avatar', '给你的 Agent 一张会动、能换装的脸。', 'npm', '/developing/sdks/typescript#avatar-包']
      ])
    },
    harness: {
      kicker: '面向 .NET 的 Agent Harness',
      title: '将完整的 Agent Runtime 嵌入任何 .NET 应用。',
      points: [
        { icon: 'cpu', title: '运行在你的 .NET 应用里', text: '将完整 Runtime 直接嵌入桌面、服务端、CLI 或自动化应用。无需额外部署和维护 Agent 服务。' },
        { icon: 'dotnet', title: '遵循 .NET 的开发方式', text: '沿用熟悉的 Generic Host 与依赖注入模式。配置、生命周期和用户体验始终由你的应用掌控。' },
        { icon: 'layers', title: '不止一个 Agentic Loop', text: '持久化会话、工具、Skills、审批与模型 Provider 已经组合就绪。从产品能力开始，而不是重复搭建 Agent 基础设施。' }
      ],
      overview: { text: 'Harness 总览', href: '/developing/harness/' },
      nuget: { text: 'NuGet 包', href: '/developing/harness/nuget-package' },
      viewer: 'Harness 示例'
    },
    close: { title: '一切就绪，就等你了。' },
    footer: { copyright: 'Copyright © DotHarness · Apache License 2.0', label: '项目' }
  }
} satisfies Record<Locale, unknown>

export type HomeCopy = (typeof homeCopy)['en']

export const installCommands = {
  windows: 'irm https://www.dotcraft.net/install.ps1 | iex',
  unix: 'curl -fsSL https://www.dotcraft.net/install.sh | bash'
}

export const projectLinks = [
  { text: 'GitHub', href: 'https://github.com/DotHarness/dotcraft', icon: 'github' },
  { text: 'Releases', href: 'https://github.com/DotHarness/dotcraft/releases' },
  { text: 'Discussions', href: 'https://github.com/DotHarness/dotcraft/discussions' },
  { text: 'NuGet', href: 'https://www.nuget.org/profiles/DotHarness', icon: 'nuget' },
  { text: 'npm', href: 'https://www.npmjs.com/org/dotcraft', icon: 'npm' }
]
