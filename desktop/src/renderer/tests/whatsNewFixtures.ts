import {
  WHATS_NEW_REMOTE_MEDIA_BASE_URL,
  type WhatsNewRelease
} from '../../shared/whatsNew'

export const WHATS_NEW_TEST_RELEASE_0_1_7: WhatsNewRelease = {
  version: '0.1.7',
  cards: [
    {
      id: 'agent-builder',
      title: {
        en: 'Agent Builder',
        'zh-Hans': 'Agent Builder'
      },
      summary: {
        en: 'Create a specialized Agent Profile through conversation.',
        'zh-Hans': '通过对话创建专门用途的 Agent Profile。'
      },
      media: {
        fileName: 'agent-builder.gif',
        url: `${WHATS_NEW_REMOTE_MEDIA_BASE_URL}agent-builder.gif`,
        sizeBytes: 4180309,
        sha256: '57DF3E8605B9DE58A5BE6B0F8ABEFEFCEB9F10284095852569FEC7B34ECF690D'
      }
    }
  ]
}

export const WHATS_NEW_TEST_RELEASES: WhatsNewRelease[] = [
  {
    version: '0.1.6',
    cards: [
      {
        id: 'connect-im',
        title: {
          en: 'Background Channels',
          'zh-Hans': 'Background Channels'
        },
        summary: {
          en: 'Keep DotCraft connected to social channels in the background, even when Desktop is closed.',
          'zh-Hans': 'DotCraft 支持后台社交渠道长连接，关闭桌面端也能继续工作。'
        },
        media: {
          fileName: 'channels.gif',
          url: `${WHATS_NEW_REMOTE_MEDIA_BASE_URL}channels.gif`,
          sizeBytes: 1966072,
          sha256: 'CA31C3BDB7EC722BB262EA9ED9D692EA9E398EECD577C48B0F59D6DC334A4352'
        }
      },
      {
        id: 'dreams',
        title: {
          en: 'Dreams',
          'zh-Hans': 'Dreams'
        },
        summary: {
          en: 'Let DotCraft organize project memory overnight.',
          'zh-Hans': '让 DotCraft 在晚上整理项目记忆'
        },
        media: {
          fileName: 'dreams.gif',
          url: `${WHATS_NEW_REMOTE_MEDIA_BASE_URL}dreams.gif`,
          sizeBytes: 3491022,
          sha256: '089E5DA315CA92FD77FFDCB49F88E79AF8351BE014D8F677683C835FE1E549BC'
        }
      },
      {
        id: 'goal',
        title: {
          en: 'Goal',
          'zh-Hans': 'Goal'
        },
        summary: {
          en: 'Let DotCraft keep pushing long-running tasks forward.',
          'zh-Hans': '让 DotCraft 持续推进任务'
        },
        media: {
          fileName: 'goal.gif',
          url: `${WHATS_NEW_REMOTE_MEDIA_BASE_URL}goal.gif`,
          sizeBytes: 1471959,
          sha256: '0F480003CA34965CA5E1B122326910D762601141271F5D4FF6F47C6601931AAB'
        }
      }
    ]
  }
]
