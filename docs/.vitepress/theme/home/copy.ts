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
  look: 'desktop' | 'harness' | 'oratorio' | 'satellite' | 'avatar'
  label: string
  hint: string
  href: string
}

const raw = 'https://github.com/DotHarness/resources/raw/master/'
const cdn = 'https://cdn.jsdelivr.net/gh/DotHarness/resources@master/'

function stories(base: string, text: { name: string; line: string; alt: string; links: string[] }[]): Story[] {
  const shape = [
    { icon: 'monitor', src: 'dotcraft/whats-new/multi-workspace.gif', width: 1280, height: 720, hrefs: ['/features/entry-points/desktop', '/getting-started'] },
    { icon: 'bot', src: 'dotcraft/whats-new/agent-builder.gif', width: 1280, height: 720, hrefs: ['/features/agent-system/agent-profiles', '/features/agent-system/subagents', '/features/agent-system/automations'], team: true },
    { icon: 'puzzle', src: 'dotcraft/whats-new/desktop-plugins.gif', width: 1280, height: 720, hrefs: ['/developing/integrations/dotnet-plugins', '/developing/integrations/desktop-plugins'] },
    { icon: 'blocks', src: 'dotcraft-unity/app-binding.gif', width: 1376, height: 774, hrefs: ['/developing/harness/', '/developing/integrations/app-binding', '/developing/sdks/'] }
  ]
  return shape.map((story, index) => ({
    icon: story.icon,
    name: text[index].name,
    line: text[index].line,
    media: { src: base + story.src, alt: text[index].alt, width: story.width, height: story.height },
    links: story.hrefs.map((href, at) => ({ text: text[index].links[at], href })),
    team: story.team
  }))
}

const productHrefs = {
  desktop: '/features/entry-points/desktop',
  harness: '/developing/harness/',
  oratorio: '/features/oratorio',
  satellite: '/features/agent-system/satellite'
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
            { icon: 'sparkles', label: 'Ready out of the box', hint: 'Plan, subagents, Automations, Goals, Dreams, and Dynamic Workflows are built in. Agent Builder turns what you describe into a reusable agent.', href: '/features/agent-system/' },
            { icon: 'app-window', label: 'Works in your apps', hint: 'With Computer use, agents operate the Windows apps you allow.', href: '/features/entry-points/desktop' },
            { icon: 'layers', label: 'Pick up anywhere', hint: 'Desktop, the CLI, editors, and chat bots share one workspace. Connect to DotCraft on a server over SSH, or let agents work on another computer through Satellite.', href: '/features/entry-points/' },
            { icon: 'server', label: 'Your deployment, your costs', hint: 'Run locally or on your own server with a compatible model provider. Byte-stable prompt prefixes improve provider cache reuse.', href: '/features/self-hosted/server-deployment' }
          ]
        },
        {
          title: 'Embed and extend',
          link: { text: 'DotCraft Harness', href: '/developing/harness/' },
          rows: [
            { icon: 'circuit-board', label: 'Build it into your product', hint: 'Embed the runtime behind DotCraft Desktop in your .NET apps.', href: '/developing/harness/' },
            { icon: 'plug-zap', label: 'Connect existing products', hint: 'Bring agents into products you already ship through AppServer, SDKs, and App Binding.', href: '/developing/sdks/' },
            { icon: 'dotnet', label: '.NET plugins', hint: 'Add tools, commands, and lifecycle logic. The agent can write one and swap it in while the host keeps running.', href: '/developing/integrations/dotnet-plugins' },
            { icon: 'layout-dashboard', label: 'Desktop plugins', hint: "React plugins reshape Desktop's interface.", href: '/developing/integrations/desktop-plugins' }
          ]
        }
      ]
    },
    tour: {
      title: 'From the app to your own product.',
      readMore: 'Read more',
      stories: stories(raw, [
        { name: 'DotCraft Desktop', line: 'Plan, build, review, and automate in one app.', alt: 'Switching between projects in DotCraft Desktop', links: ['Desktop', 'Getting Started'] },
        { name: 'Agent Builder + Profiles', line: 'Build your own Agent team through conversation.', alt: 'Customizing a specialized agent through conversation', links: ['Agent Profiles', 'Subagents', 'Automations'] },
        { name: 'Plugins', line: 'Extend the runtime in C#, and Desktop in TypeScript.', alt: 'Installing a Desktop Plugin and enabling its visual customization in DotCraft Desktop', links: ['.NET Plugins', 'Desktop Plugins'] },
        { name: 'Built for applications', line: 'Bring DotCraft into your own product.', alt: 'An agent driving Unity from DotCraft through App Binding', links: ['DotCraft Harness', 'DotCraft App', 'SDKs'] }
      ])
    },
    products: {
      title: 'Explore DotCraft',
      loop: { src: raw + 'dotcraft/products.webp', alt: 'DotCraft Desktop, DotCraft.Harness, Oratorio, DotCraft Satellite and @dotcraft/avatar' },
      rows: [
        { look: 'desktop', label: 'Desktop', hint: 'Work with agents on your projects in one desktop app.', href: productHrefs.desktop },
        { look: 'harness', label: 'Harness', hint: 'Embed a complete agent runtime in your .NET applications.', href: productHrefs.harness },
        { look: 'oratorio', label: 'Oratorio', hint: 'Manage agent tasks from assignment to review on one board.', href: productHrefs.oratorio },
        { look: 'satellite', label: 'Satellite', hint: 'Let your agents work in an approved shared folder on another computer.', href: productHrefs.satellite },
        { look: 'avatar', label: 'Avatar', hint: 'Give your agents personality with expressive, customizable avatars.', href: '/developing/sdks/typescript#avatar-package' }
      ] as Product[]
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
            { icon: 'sparkles', label: '开箱即用', hint: 'Plan、subagents、Automations、Goals、Dreams 和 Dynamic Workflows 都已内置。Agent Builder 能把你的描述变成可复用的 Agent。', href: '/features/agent-system/' },
            { icon: 'app-window', label: '操作你的应用', hint: '借助电脑操控，Agent 可以在你允许的 Windows 应用里工作。', href: '/features/entry-points/desktop' },
            { icon: 'layers', label: '随处接着做', hint: 'Desktop、CLI、编辑器和聊天机器人共用同一个工作区。你还可以通过 SSH 连接服务器上的 DotCraft，或借助卫星让 Agent 在另一台电脑上工作。', href: '/features/entry-points/' },
            { icon: 'server', label: '部署和成本由你掌控', hint: '在本地或自己的服务器上运行，选用兼容的模型服务。提示词前缀保持逐字节稳定，提高缓存复用率。', href: '/features/self-hosted/server-deployment' }
          ]
        },
        {
          title: '嵌入与扩展',
          link: { text: 'DotCraft Harness', href: '/developing/harness/' },
          rows: [
            { icon: 'circuit-board', label: '装进你的产品', hint: '把 DotCraft Desktop 背后的运行时嵌入你的 .NET 应用。', href: '/developing/harness/' },
            { icon: 'plug-zap', label: '接入现有产品', hint: '通过 AppServer、SDK 和 App Binding，把 Agent 带进你已经在交付的产品。', href: '/developing/sdks/' },
            { icon: 'dotnet', label: '.NET 插件', hint: '添加工具、命令和生命周期逻辑。Agent 能自己编写插件，并在宿主运行时直接替换。', href: '/developing/integrations/dotnet-plugins' },
            { icon: 'layout-dashboard', label: 'Desktop 插件', hint: 'React 插件可以改造 Desktop 的界面。', href: '/developing/integrations/desktop-plugins' }
          ]
        }
      ]
    },
    tour: {
      title: '从应用到你自己的产品。',
      readMore: '了解更多',
      stories: stories(cdn, [
        { name: 'DotCraft Desktop', line: '规划、执行、审阅和自动化，都在一个桌面应用里完成。', alt: '在 DotCraft Desktop 中切换项目', links: ['Desktop', '快速开始'] },
        { name: 'Agent Builder + Profiles', line: '通过对话，打造属于你的 Agent 团队。', alt: '通过对话定制一个专属 Agent', links: ['Agent Profiles', 'Subagents', '自动化'] },
        { name: '插件', line: '用 C# 扩展运行时，用 TypeScript 扩展 Desktop。', alt: '安装 Desktop Plugin 并在 DotCraft Desktop 中启用视觉定制', links: ['.NET 插件', 'Desktop Plugins'] },
        { name: '为应用而生', line: '把 DotCraft 带进你自己的产品。', alt: 'Agent 通过 App Binding 从 DotCraft 驱动 Unity', links: ['DotCraft Harness', 'DotCraft App', 'SDK'] }
      ])
    },
    products: {
      title: '探索 DotCraft',
      loop: { src: raw + 'dotcraft/products.webp', alt: 'DotCraft Desktop、DotCraft.Harness、Oratorio、DotCraft Satellite 和 @dotcraft/avatar' },
      rows: [
        { look: 'desktop', label: 'Desktop', hint: '在一个桌面应用中与 Agent 一起处理项目。', href: productHrefs.desktop },
        { look: 'harness', label: 'Harness', hint: '将完整的 Agent 运行时嵌入你的 .NET 应用。', href: productHrefs.harness },
        { look: 'oratorio', label: 'Oratorio', hint: '在同一看板上管理 Agent 任务，从分配到审阅。', href: productHrefs.oratorio },
        { look: 'satellite', label: '卫星', hint: '让你的 Agent 在另一台电脑获准共享的文件夹中工作。', href: productHrefs.satellite },
        { look: 'avatar', label: 'Avatar', hint: '用表情丰富、可自由搭配的头像，为你的 Agent 赋予鲜明个性。', href: '/developing/sdks/typescript#avatar-包' }
      ] as Product[]
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
