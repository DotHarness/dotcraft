import { useCallback, useEffect, useRef, useState } from 'react'
import type { ProviderAuthLoginState, ProviderAuthStatusResult } from '@dotcraft/sdk/contracts'
import { useT } from '../../../contexts/LocaleContext'
import { describeLoginFailure, describeProviderAuthError } from './providerAuthErrors'
import { isLoginTerminal, ProviderLoginTracker } from './providerLoginTracker'

const LOGIN_REQUEST_TIMEOUT_MS = 30_000
const AUTH_REQUEST_TIMEOUT_MS = 30_000

export interface ProviderSignInOptions {
  providerId: string | null
  method: string
  prepare?: () => Promise<string | null>
  onSignedIn?: (providerId: string) => void | Promise<void>
  onSignedOut?: (providerId: string) => void | Promise<void>
}

export interface ProviderSignIn {
  status: ProviderAuthStatusResult | null
  login: ProviderAuthLoginState | null
  pending: boolean
  busy: boolean
  error: string | null
  signIn: () => Promise<void>
  cancel: () => Promise<void>
  answer: (value: string) => Promise<boolean>
  signOut: () => Promise<void>
}

export function useProviderSignIn({ providerId, method, prepare, onSignedIn, onSignedOut }: ProviderSignInOptions): ProviderSignIn {
  const t = useT()
  const [status, setStatus] = useState<ProviderAuthStatusResult | null>(null)
  const [login, setLogin] = useState<ProviderAuthLoginState | null>(null)
  const [starting, setStarting] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const tracker = useRef(new ProviderLoginTracker())
  const mounted = useRef(true)
  const statusRequest = useRef(0)
  const handled = useRef<{ completed: string | null; openedUrl: string | null }>({ completed: null, openedUrl: null })
  const latest = useRef({ t, onSignedIn, onSignedOut })
  latest.current = { t, onSignedIn, onSignedOut }

  const readStatus = useCallback(async (id: string): Promise<void> => {
    const request = ++statusRequest.current
    try {
      const result = await window.api.appServer.sendRequest(
        'provider/auth/status', { providerId: id }, AUTH_REQUEST_TIMEOUT_MS
      ) as ProviderAuthStatusResult
      if (mounted.current && request === statusRequest.current) setStatus(result)
    } catch (cause) {
      if (mounted.current && request === statusRequest.current) setError(describeProviderAuthError(cause, latest.current.t))
    }
  }, [])

  const apply = useCallback((state: ProviderAuthLoginState): void => {
    if (!mounted.current) return
    setLogin(state)
    const event = state.event
    if (event?.kind === 'auth_url' && event.url) {
      const key = `${state.loginId}\n${event.url}`
      if (handled.current.openedUrl !== key) {
        handled.current.openedUrl = key
        void window.api.shell.openExternal(event.url).catch(() => undefined)
      }
    }
    if (state.status === 'failed') setError(describeLoginFailure(state, latest.current.t))
    if (state.status === 'completed' && handled.current.completed !== state.loginId) {
      handled.current.completed = state.loginId
      statusRequest.current++
      setStatus(state.authentication ?? { isAuthenticated: true })
      void readStatus(state.providerId)
      void latest.current.onSignedIn?.(state.providerId)
    }
  }, [readStatus])

  useEffect(() => {
    mounted.current = true
    const unsubscribe = window.api.appServer.onNotification((payload) => {
      if (payload?.method !== 'provider/auth/updated') return
      const next = tracker.current.receive(payload.params as ProviderAuthLoginState)
      if (next) apply(next)
    })
    return () => {
      mounted.current = false
      unsubscribe()
      const loginId = tracker.current.activeLoginId
      if (loginId) void window.api.appServer.sendRequest('provider/auth/cancel', { loginId }).catch(() => undefined)
    }
  }, [apply])

  useEffect(() => {
    statusRequest.current++
    setStatus(null)
    if (providerId) void readStatus(providerId)
  }, [providerId, readStatus])

  async function recover(loginId: string): Promise<void> {
    try {
      const state = await window.api.appServer.sendRequest(
        'provider/auth/state', { loginId }, AUTH_REQUEST_TIMEOUT_MS
      ) as ProviderAuthLoginState
      const merged = tracker.current.merge(state)
      if (merged) apply(merged)
    } catch {
      return
    }
  }

  async function signIn(): Promise<void> {
    if (starting || tracker.current.activeLoginId) return
    setError(null)
    setStarting(true)
    try {
      const id = prepare ? await prepare() : providerId
      if (!id || !mounted.current) return
      if (id !== providerId) {
        const current = await window.api.appServer.sendRequest(
          'provider/auth/status', { providerId: id }, AUTH_REQUEST_TIMEOUT_MS
        ) as ProviderAuthStatusResult
        if (!mounted.current) return
        if (current.isAuthenticated) {
          setStatus(current)
          await latest.current.onSignedIn?.(id)
          return
        }
      }
      tracker.current.begin(id)
      let response: ProviderAuthLoginState
      try {
        response = await window.api.appServer.sendRequest(
          'provider/auth/login', { providerId: id, method }, LOGIN_REQUEST_TIMEOUT_MS
        ) as ProviderAuthLoginState
      } catch (cause) {
        tracker.current.abandon()
        throw cause
      }
      const state = tracker.current.started(response)
      if (!mounted.current) {
        if (!isLoginTerminal(state)) {
          void window.api.appServer.sendRequest('provider/auth/cancel', { loginId: state.loginId }).catch(() => undefined)
        }
        return
      }
      apply(state)
      if (!isLoginTerminal(state)) await recover(state.loginId)
    } catch (cause) {
      if (mounted.current) setError(describeProviderAuthError(cause, latest.current.t))
    } finally {
      if (mounted.current) setStarting(false)
    }
  }

  async function cancel(): Promise<void> {
    const loginId = tracker.current.activeLoginId
    if (!loginId) return
    try {
      await window.api.appServer.sendRequest('provider/auth/cancel', { loginId }, AUTH_REQUEST_TIMEOUT_MS)
      await recover(loginId)
    } catch (cause) {
      if (mounted.current) setError(describeProviderAuthError(cause, latest.current.t))
    }
  }

  async function answer(value: string): Promise<boolean> {
    const loginId = tracker.current.activeLoginId
    const promptId = login?.promptId
    if (!loginId || !promptId || login?.loginId !== loginId) return false
    setBusy(true)
    setError(null)
    try {
      await window.api.appServer.sendRequest(
        'provider/auth/input', { loginId, promptId, value }, AUTH_REQUEST_TIMEOUT_MS
      )
      return true
    } catch (cause) {
      if (mounted.current) setError(describeProviderAuthError(cause, latest.current.t))
      return false
    } finally {
      if (mounted.current) setBusy(false)
    }
  }

  async function signOut(): Promise<void> {
    if (!providerId) return
    setBusy(true)
    setError(null)
    try {
      await window.api.appServer.sendRequest('provider/auth/logout', { providerId }, AUTH_REQUEST_TIMEOUT_MS)
      if (!mounted.current) return
      setLogin(null)
      await readStatus(providerId)
      await latest.current.onSignedOut?.(providerId)
    } catch (cause) {
      if (mounted.current) setError(describeProviderAuthError(cause, latest.current.t))
    } finally {
      if (mounted.current) setBusy(false)
    }
  }

  const loginActive = login != null && !isLoginTerminal(login)
  return {
    status,
    login: loginActive ? login : null,
    pending: starting || loginActive,
    busy,
    error,
    signIn,
    cancel,
    answer,
    signOut
  }
}
