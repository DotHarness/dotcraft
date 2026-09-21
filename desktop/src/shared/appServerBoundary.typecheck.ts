import type { ClientRequestMethods } from '@dotcraft/sdk/contracts'
import type {
  AppServerRequestMethod,
  KnownNotificationPayload,
  RawNotificationPayload,
  TypedAppServerRequestApi
} from './appServerBoundary'

type Equal<Left, Right> =
  (<Value>() => Value extends Left ? 1 : 2) extends
  (<Value>() => Value extends Right ? 1 : 2)
    ? true
    : false

type Expect<Value extends true> = Value

type ExpectedSendRequest = <Method extends keyof ClientRequestMethods>(
  method: Method,
  params: ClientRequestMethods[Method]['params'],
  timeoutMs?: number | null
) => Promise<ClientRequestMethods[Method]['result']>

type ThreadRenamedNotification = Extract<
  KnownNotificationPayload,
  { method: 'thread/renamed' }
>

export type AppServerBoundaryTypeAssertions = [
  Expect<Equal<TypedAppServerRequestApi['sendRequest'], ExpectedSendRequest>>,
  Expect<Equal<Extract<AppServerRequestMethod, 'turn/start'>, 'turn/start'>>,
  Expect<Equal<Extract<AppServerRequestMethod, 'turn/strat'>, never>>,
  Expect<Equal<ThreadRenamedNotification['params']['threadId'], string | undefined>>,
  Expect<
    Equal<ThreadRenamedNotification['params']['displayName'], string | null | undefined>
  >,
  Expect<Equal<RawNotificationPayload['method'], string>>
]
