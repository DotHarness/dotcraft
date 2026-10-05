import { useRouter } from 'expo-router'
import { useCallback, useMemo, useState } from 'react'
import { Pressable, ScrollView, StyleSheet, TextInput, View } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { ComputerProvider, useComputer, useMobileState, useSession } from '../../app-state/SessionContext'
import { needsYou } from '../../core/chatState'
import { EMPTY_DRAFT } from '../../core/draft'
import {
  computerStatus,
  isReachable,
  projectById,
  projectsByRecentUse,
  recentChats,
  runningChats,
  stateOf,
  type ComputerStatus,
} from '../../core/state'
import { useI18n } from '../../i18n'
import { Icon } from '../icons'
import { animateLayout, CompactComposer, NewChatPane, useProjectStart } from '../chat/NewChat'
import { Screen, ScrollArea } from '../layout'
import { Mascot, MascotNote, MascotTransition, type MascotMoment } from '../mascot/Mascot'
import { MenuRow, PopoverMenu } from '../Menu'
import { ComputerStatusLine, Notice, PhoneButton, ReadOnlyNotice, RoundIconButton, Section, Txt } from '../parts'
import { ChatRow, chatTitle, ProjectRow, projectTitle } from '../rows'
import { metrics, type, useTheme } from '../theme'
import { useAddComputer } from './SettingsScreen'

function computerMoment(status: ComputerStatus, waiting: number): MascotMoment {
  if (status === 'offline' || status === 'access-off') return 'asleep'
  if (status === 'connecting') return 'look-around'
  return waiting > 0 ? 'question' : 'idle'
}

export function chatHref(computerId: string, key: string) {
  const index = key.indexOf(':')
  return {
    pathname: '/chat/[computerId]/[projectId]/[threadId]' as const,
    params: { computerId, projectId: key.slice(0, index), threadId: key.slice(index + 1) },
  }
}

export function projectHref(computerId: string, projectId: string) {
  return { pathname: '/project/[computerId]/[projectId]' as const, params: { computerId, projectId } }
}

function statusTone(status: ComputerStatus, colors: ReturnType<typeof useTheme>['colors']): string {
  if (status === 'online') return colors.success
  return status === 'connecting' ? colors.accent : colors.textDimmed
}

function ComputerChips() {
  const state = useMobileState()
  const session = useSession()
  const { colors } = useTheme()
  if (state.order.length < 2) return null
  return (
    <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.chipRow} contentContainerStyle={styles.chips}>
      {state.order.map((id) => {
        const computer = state.computers[id]
        const selected = id === state.selected
        return (
          <Pressable
            key={id}
            accessibilityRole="button"
            accessibilityState={{ selected }}
            onPress={() => session.select(id)}
            style={({ pressed }) => [
              styles.chip,
              selected && { backgroundColor: colors.bgSecondary, borderColor: colors.borderDefault },
              pressed && { backgroundColor: colors.roundFill },
            ]}
          >
            <View style={[styles.dot, { backgroundColor: statusTone(computerStatus(computer), colors) }]} />
            <Icon name="monitor" size={14} color={colors.textSecondary} strokeWidth={2} />
            <Txt numberOfLines={1} tone={selected ? undefined : 'secondary'}>
              {computer.computer.name}
            </Txt>
          </Pressable>
        )
      })}
    </ScrollView>
  )
}

function IdentityChanged() {
  const session = useSession()
  const { t } = useI18n()
  const { computer } = useComputer()
  return (
    <View style={styles.identity}>
      <MascotNote moment="wary">{t('identity.note', { computer: computer.name })}</MascotNote>
      <PhoneButton variant="danger" onPress={() => void session.removeComputer(computer.id)}>
        {t('identity.remove', { computer: computer.name })}
      </PhoneButton>
    </View>
  )
}

function RevokedNotice() {
  const state = useMobileState()
  const session = useSession()
  const { t } = useI18n()
  if (!state.revokedBy) return null
  return (
    <Notice
      icon="info"
      style={styles.notice}
      action={
        <PhoneButton variant="outline" compact onPress={() => session.acknowledgeRevoked()}>
          {t('common.close')}
        </PhoneButton>
      }
    >
      {t('pair.revoked', { computer: state.revokedBy })}
    </Notice>
  )
}

export function HomeScreen() {
  const selected = useMobileState().selected
  if (!selected) return <Screen>{null}</Screen>
  return (
    <ComputerProvider key={selected} computerId={selected}>
      <ComputerHome />
    </ComputerProvider>
  )
}

function ComputerHome() {
  const state = useComputer()
  const router = useRouter()
  const { t } = useI18n()
  const { colors } = useTheme()
  const [query, setQuery] = useState('')
  const [searching, setSearching] = useState(false)
  const [menuOpen, setMenuOpen] = useState(false)
  const [rowHeight, setRowHeight] = useState(0)
  const [composing, setComposing] = useState<string | null>(null)
  const [draft, setDraft] = useState(EMPTY_DRAFT)
  const insets = useSafeAreaInsets()
  const addComputer = useAddComputer()
  const computer = state.computer
  const status = computerStatus(state)
  const live = isReachable(status)
  const recent = useMemo(() => recentChats(state), [state])
  const visible = useMemo(() => runningChats(state), [state])
  const waiting = visible.filter((chat) => needsYou(stateOf(chat))).length
  const collapse = useCallback(() => {
    animateLayout()
    setComposing(null)
  }, [])
  useProjectStart(composing)

  const trimmed = query.trim().toLowerCase()
  const untitled = t('chat.untitled')
  const results = trimmed ? visible.filter((chat) => chatTitle(chat, untitled).toLowerCase().includes(trimmed)) : []
  const nameOf = (projectId: string) => {
    const project = projectById(state, projectId)
    return project ? projectTitle(project, t) : ''
  }
  const openChat = (key: string) => router.push(chatHref(computer.id, key))
  const menuLabel = t('home.computerMenu', { computer: computer.name })
  const lastProject = projectsByRecentUse(state)[0]
  const canStart = status === 'online' && Boolean(lastProject)
  const closeSearch = () => {
    setSearching(false)
    setQuery('')
  }

  return (
    <Screen>
      <View
        onLayout={({ nativeEvent }) => setRowHeight(nativeEvent.layout.height)}
        style={[styles.top, { backgroundColor: colors.bgPrimary }]}
      >
        {searching ? (
          <View style={styles.searchRow}>
            <View style={[styles.search, { borderColor: colors.accent, backgroundColor: colors.bgSecondary }]}>
              <Icon name="search" size={18} color={colors.textSecondary} />
              <TextInput
                value={query}
                onChangeText={setQuery}
                autoFocus
                placeholder={t('home.search')}
                placeholderTextColor={colors.textDimmed}
                accessibilityLabel={t('home.search')}
                returnKeyType="search"
                autoCorrect={false}
                style={[type.text, styles.searchInput, { color: colors.textPrimary }]}
              />
            </View>
            <RoundIconButton label={t('common.close')} icon="x" onPress={closeSearch} />
          </View>
        ) : (
          <>
            {composing ? (
              <View style={styles.backButton}>
                <RoundIconButton label={t('common.back')} icon="chevronLeft" onPress={collapse} />
              </View>
            ) : null}
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={menuLabel}
              accessibilityState={{ expanded: menuOpen }}
              style={({ pressed }) => [styles.computer, pressed && { backgroundColor: colors.roundFill }]}
              onPress={() => setMenuOpen(true)}
            >
              <Mascot moment={computerMoment(status, waiting)} size={40} style={styles.avatar} />
              <View style={styles.computerText}>
                <Txt numberOfLines={1} style={styles.computerName}>
                  {computer.name}
                </Txt>
                <ComputerStatusLine status={status} updatedAt={state.syncedAt} />
              </View>
              <Icon name="chevronDown" size={16} color={colors.textSecondary} strokeWidth={2} />
            </Pressable>
            <View style={styles.searchButton}>
              <RoundIconButton
                label={t('home.search')}
                icon="search"
                onPress={() => {
                  setComposing(null)
                  setSearching(true)
                }}
              />
            </View>
          </>
        )}
      </View>
      {searching || composing ? null : <ComputerChips />}
      {composing ? (
        <NewChatPane
          key={composing}
          projectId={composing}
          draft={draft}
          onDraft={setDraft}
          onPickProject={setComposing}
          onCollapse={collapse}
          onCreated={(key) => {
            setComposing(null)
            openChat(key)
          }}
        />
      ) : (
        <>
          <ScrollArea>
            <RevokedNotice />
            {status === 'access-off' ? <ReadOnlyNotice status={status} computer={computer.name} style={styles.notice} /> : null}
            <View style={styles.lists}>
              {state.identityChanged ? (
                <IdentityChanged />
              ) : state.syncing ? (
                <MascotTransition line={t('home.catchingUp', { computer: computer.name })} />
              ) : trimmed ? (
                <Section title={t('home.results')} grow>
                  {results.length > 0 ? (
                    results.map((chat) => (
                      <ChatRow key={chat.key} chat={chat} live={live} projectName={nameOf(chat.projectId)} onPress={() => openChat(chat.key)} />
                    ))
                  ) : (
                    <MascotNote moment="curious">{t('home.noResults', { query: query.trim() })}</MascotNote>
                  )}
                </Section>
              ) : (
                <>
                  <Section title={t('home.projects')}>
                    {state.projects.map((project) => (
                      <ProjectRow key={project.id} project={project} onPress={() => router.push(projectHref(computer.id, project.id))} />
                    ))}
                  </Section>
                  {recent.length > 0 ? (
                    <Section title={t('home.recent')}>
                      {recent.map((chat) => (
                        <ChatRow key={chat.key} chat={chat} live={live} projectName={nameOf(chat.projectId)} onPress={() => openChat(chat.key)} />
                      ))}
                    </Section>
                  ) : null}
                </>
              )}
            </View>
          </ScrollArea>
          <CompactComposer
            draft={draft}
            disabled={!canStart}
            onPress={() => {
              if (!lastProject) return
              animateLayout()
              setComposing(lastProject.id)
            }}
          />
        </>
      )}
      <PopoverMenu visible={menuOpen} label={menuLabel} anchor={{ top: insets.top + rowHeight }} onClose={() => setMenuOpen(false)}>
        <MenuRow
          icon="plus"
          label={t('settings.addComputer')}
          onPress={() => {
            setMenuOpen(false)
            addComputer()
          }}
        />
        <MenuRow
          icon="settings"
          label={t('common.settings')}
          onPress={() => {
            setMenuOpen(false)
            router.push('/settings')
          }}
        />
      </PopoverMenu>
    </Screen>
  )
}

const styles = StyleSheet.create({
  top: {
    flexDirection: 'row',
    justifyContent: 'center',
    alignItems: 'center',
    minHeight: 64,
    paddingVertical: 8,
    paddingHorizontal: metrics.gutter + 52,
  },
  backButton: { position: 'absolute', left: metrics.gutter },
  searchButton: { position: 'absolute', right: metrics.gutter },
  searchRow: { flex: 1, flexDirection: 'row', alignItems: 'center', gap: 8, marginHorizontal: -52 },
  computer: {
    flexShrink: 1,
    minWidth: 0,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 10,
    paddingVertical: 4,
    paddingLeft: 8,
    paddingRight: 12,
    borderRadius: metrics.heroRadius,
  },
  avatar: { marginLeft: -4, transform: [{ translateY: -3 }] },
  computerText: { flexShrink: 1, minWidth: 0, gap: 1 },
  computerName: { fontWeight: '600' },
  notice: { marginTop: 8 },
  chipRow: { flexGrow: 0 },
  chips: { gap: 8, paddingHorizontal: metrics.gutter, paddingBottom: 8 },
  chip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    height: 36,
    paddingHorizontal: 14,
    borderWidth: 1,
    borderColor: 'transparent',
    borderRadius: metrics.pill,
  },
  dot: { width: 7, height: 7, borderRadius: 4 },
  identity: { gap: 16, paddingTop: 24 },
  lists: { flexGrow: 1, marginTop: -12 },
  search: {
    flex: 1,
    minWidth: 0,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    height: metrics.touch,
    paddingHorizontal: 16,
    borderWidth: 1,
    borderRadius: metrics.pill,
  },
  searchInput: { flex: 1, minWidth: 0, padding: 0, outlineWidth: 0 },
})
