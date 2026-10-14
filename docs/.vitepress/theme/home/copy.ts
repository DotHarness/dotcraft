export type Locale = 'en' | 'zh'

export interface Link {
  text: string
  href: string
}

export interface Story {
  icon: string
  name: string
  line: string
  media: { light: string; dark: string; alt: string; width: number; height: number }
  links: Link[]
  team?: boolean
}

export interface Product {
  id: ProductId
  label: string
  hint: string
  get: string
  href: string
}

const raw = 'https://github.com/DotHarness/resources/raw/master/'

function stories(text: { name: string; line: string; alt: string; links: string[] }[]): Story[] {
  const shape = [
    { icon: 'monitor', src: 'dotcraft/docs/tour-desktop', hrefs: ['/start/desktop', '/start/'] },
    { icon: 'bot', src: 'dotcraft/docs/tour-agents', hrefs: ['/guide/agent-profiles', '/guide/subagents', '/guide/automations'], team: true },
    { icon: 'puzzle', src: 'dotcraft/docs/tour-plugins', hrefs: ['/extend/dotnet-plugins', '/extend/desktop-plugins'] },
    { icon: 'blocks', src: 'dotcraft/docs/tour-apps', hrefs: ['/extend/harness/', '/extend/app-binding', '/extend/sdks/'] }
  ]
  return shape.map((story, index) => ({
    icon: story.icon,
    name: text[index].name,
    line: text[index].line,
    media: { light: `${raw}${story.src}-light.webp`, dark: `${raw}${story.src}-dark.webp`, alt: text[index].alt, width: 1600, height: 900 },
    links: story.hrefs.map((href, at) => ({ text: text[index].links[at], href })),
    team: story.team
  }))
}

const productHrefs: Record<ProductId, string> = {
  desktop: '/start/desktop',
  mobile: '/start/mobile',
  cli: '/start/entry-points'
}

export type ProductId = 'desktop' | 'mobile' | 'cli'

function products(rows: [ProductId, string, string, string][]): Product[] {
  return rows.map(([id, label, hint, get]) => ({ id, label, hint, get, href: productHrefs[id] }))
}

export const homeCopy = {
  en: {
    hero: {
      eyebrow: 'Open source, built on .NET 10',
      lines: ['An agent runtime you', 'embed and extend'],
      stop: '.'
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
    tour: {
      title: 'From the app to your own product.',
      readMore: 'Read more',
      stories: stories([
        { name: 'DotCraft Desktop', line: 'Plan, build, review, and automate in one app.', alt: 'DotCraft Desktop working through a Ship dark mode plan: two subagents run, the in-app browser checks the settings page, and a weekly contrast check is set up as an automation', links: ['Desktop', 'Getting Started'] },
        { name: 'Agent Builder + Profiles', line: 'Build your own Agent team through conversation.', alt: 'Agent Builder turning a short description into a contrast-checker agent with its own avatar, tools and skills', links: ['Agent Profiles', 'Subagents', 'Automations'] },
        { name: 'Plugins', line: 'Extend the runtime in C#, and Desktop in TypeScript.', alt: 'A .NET plugin adding review tools and lifecycle hooks, then a Desktop plugin changing the app wallpaper', links: ['.NET Plugins', 'Desktop Plugins'] },
        { name: 'Built for applications', line: 'Bring DotCraft into your own product.', alt: 'Code that hosts DotCraft Harness in a .NET app, binds a connected app to a thread, and streams a reply with the SDK', links: ['DotCraft Harness', 'DotCraft App', 'SDKs'] }
      ])
    },
    products: {
      title: 'Explore DotCraft',
      rows: products([
        ['desktop', 'Desktop', 'Plans, builds and checks the work in your projects.', 'Download'],
        ['mobile', 'Mobile', 'Follow and answer your chats from your phone.', 'Download'],
        ['cli', 'CLI', 'One command, and the answer is in your terminal.', 'Install script']
      ])
    },
    harness: {
      kicker: 'Agent Harness for .NET',
      title: 'Bring a complete agent runtime into any .NET application.',
      points: [
        { icon: 'cpu', title: 'Runs in your process', text: 'No separate agent service to deploy or operate.' },
        { icon: 'dotnet', title: 'Built the .NET way', text: 'Plugs into the Generic Host and dependency injection.' },
        { icon: 'layers', title: 'More than an agent loop', text: 'Sessions, tools, skills, approvals and providers come wired.' }
      ],
      overview: { text: 'Harness overview', href: '/extend/harness/' },
      nuget: { text: 'NuGet package', href: '/extend/harness/nuget-package' },
      viewer: 'Harness example'
    },
    close: { title: 'All set — over to you.' },
    footer: { copyright: 'Copyright © DotHarness · Apache License 2.0', label: 'Project' }
  },
  zh: {
    hero: {
      eyebrow: '开源，基于 .NET 10 构建',
      lines: ['可嵌入、可扩展的', 'Agent Runtime'],
      stop: '。'
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
      poster: raw + 'dotcraft/desktop_banner.png',
      alt: 'DotCraft Desktop 预览',
      try: '试一试 Live Demo',
      load: '加载交互演示',
      frame: 'DotCraft Desktop 交互演示'
    },
    tour: {
      title: '从应用到你自己的产品。',
      readMore: '了解更多',
      stories: stories([
        { name: 'DotCraft Desktop', line: '规划、执行、审阅和自动化，都在一个桌面应用里完成。', alt: 'DotCraft Desktop 按「Ship dark mode」计划推进：两个子智能体并行工作，应用内浏览器检查设置页，再把每周对比度检查设为自动化任务', links: ['Desktop', '快速开始'] },
        { name: 'Agent Builder + Profiles', line: '通过对话，打造属于你的 Agent 团队。', alt: 'Agent Builder 把一段描述变成 contrast-checker Agent，带上它自己的形象、工具和技能', links: ['Agent Profiles', 'Subagents', '自动化'] },
        { name: '插件', line: '用 C# 扩展运行时，用 TypeScript 扩展 Desktop。', alt: '一个 .NET 插件添加评审工具和生命周期钩子，再由一个 Desktop 插件更换应用壁纸', links: ['.NET 插件', 'Desktop Plugins'] },
        { name: '为应用而生', line: '把 DotCraft 带进你自己的产品。', alt: '在 .NET 应用中托管 DotCraft Harness、把已连接的应用绑定到线程，并用 SDK 流式输出回复的代码', links: ['DotCraft Harness', 'DotCraft App', 'SDK'] }
      ])
    },
    products: {
      title: '探索 DotCraft',
      rows: products([
        ['desktop', 'Desktop', '在你的项目里规划、动手、自己验收。', '下载'],
        ['mobile', '手机 App', '在手机上跟进和处理你的聊天。', '下载'],
        ['cli', 'CLI', '一条命令，答案直接回到终端。', '安装脚本']
      ])
    },
    harness: {
      kicker: '面向 .NET 的 Agent Harness',
      title: '将完整的 Agent Runtime 嵌入任何 .NET 应用。',
      points: [
        { icon: 'cpu', title: '运行在你的进程里', text: '无需额外部署和维护 Agent 服务。' },
        { icon: 'dotnet', title: '遵循 .NET 的开发方式', text: '沿用 Generic Host 与依赖注入。' },
        { icon: 'layers', title: '不止一个 Agentic Loop', text: '会话、工具、Skills、审批与模型 Provider 已组合就绪。' }
      ],
      overview: { text: 'Harness 总览', href: '/extend/harness/' },
      nuget: { text: 'NuGet 包', href: '/extend/harness/nuget-package' },
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
