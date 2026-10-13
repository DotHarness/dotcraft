import type { AppLocale } from '../../../../desktop/src/shared/locales'
import type { WorkspaceProjectSummary } from '../../../../desktop/src/shared/workspaceProjects'

const NOW = Date.now()

function minutesAgo(minutes: number): string {
  return new Date(NOW - minutes * 60_000).toISOString()
}

interface SeedThread {
  id: string
  name: string
  minutes: number
  runtime?: { running?: boolean; waitingOnApproval?: boolean }
}

interface SeedProject {
  path: string
  name: string
  state: WorkspaceProjectSummary['state']
  threads: SeedThread[]
}

function englishProjects(): SeedProject[] {
  return [
    {
      path: '/home/dev/projects/atlas-api',
      name: 'atlas-api',
      state: 'secondary',
      threads: [
        { id: 'atlas-oauth', name: 'Move sign-in to OAuth 2.1', minutes: 2, runtime: { running: true } },
        { id: 'atlas-migration', name: 'Drop the legacy sessions table', minutes: 18, runtime: { waitingOnApproval: true } },
        { id: 'atlas-rate-limit', name: 'Rate-limit the public search endpoint', minutes: 340 }
      ]
    },
    {
      path: '/home/dev/projects/arena-client',
      name: 'arena-client',
      state: 'secondary',
      threads: [
        { id: 'arena-loading', name: 'Profile the slow level loading', minutes: 75 },
        { id: 'arena-input', name: 'Rebind controls for gamepads', minutes: 1460 }
      ]
    },
    {
      path: '/home/dev/projects/handbook',
      name: 'handbook',
      state: 'cold',
      threads: []
    }
  ]
}

function chineseProjects(): SeedProject[] {
  return [
    {
      path: '/home/dev/projects/atlas-api',
      name: 'atlas-api',
      state: 'secondary',
      threads: [
        { id: 'atlas-oauth', name: '登录迁移到 OAuth 2.1', minutes: 2, runtime: { running: true } },
        { id: 'atlas-migration', name: '删除旧的 sessions 表', minutes: 18, runtime: { waitingOnApproval: true } },
        { id: 'atlas-rate-limit', name: '为公开搜索接口加限流', minutes: 340 }
      ]
    },
    {
      path: '/home/dev/projects/arena-client',
      name: 'arena-client',
      state: 'secondary',
      threads: [
        { id: 'arena-loading', name: '分析关卡加载慢的原因', minutes: 75 },
        { id: 'arena-input', name: '为手柄重新绑定按键', minutes: 1460 }
      ]
    },
    {
      path: '/home/dev/projects/handbook',
      name: 'handbook',
      state: 'cold',
      threads: []
    }
  ]
}

export function getDemoProjects(locale: AppLocale): WorkspaceProjectSummary[] {
  const projects = locale === 'zh-Hans' ? chineseProjects() : englishProjects()
  return projects.map((project) => ({
    projectId: project.path,
    kind: 'local',
    path: project.path,
    name: project.name,
    state: project.state,
    running: project.state !== 'cold',
    loaded: project.state !== 'cold',
    threadCount: project.threads.length,
    pinned: false,
    lastOpenedAt: minutesAgo(project.threads[0]?.minutes ?? 4320),
    threads: project.threads.map((thread) => ({
      id: thread.id,
      workspacePath: project.path,
      displayName: thread.name,
      status: 'active',
      originChannel: 'dotcraft-desktop',
      createdAt: minutesAgo(thread.minutes + 30),
      lastActiveAt: minutesAgo(thread.minutes),
      runtime: thread.runtime && {
        running: thread.runtime.running === true,
        waitingOnApproval: thread.runtime.waitingOnApproval === true,
        waitingOnPlanConfirmation: false
      }
    }))
  }))
}
