# Provider authentication

| Field | Value |
|---|---|
| Version | 0.9.1 |
| Status | Living |
| Date | 2026-10-19 |

Provider authentication signs a configured provider in and out and reports its account state and
usage. One Core coordinator serves built-in providers and [model backends](../architecture/model-backend-plugins.md);
AppServer projects it through the methods below and the CLI runs the same coordinator. ChatGPT
subscription credentials, refresh, and routing belong to [OpenAI subscription authentication](../architecture/openai-subscription-auth.md).

## Methods

The methods are available when `capabilities.providerManagement` is advertised.

| Method | Kind | Behavior |
|---|---|---|
| `provider/backend/list` | request | Returns active backend descriptors, protocols and authentication methods |
| `provider/auth/login` | request | Starts one login for a configured provider; returns its current state immediately |
| `provider/auth/state` | request | Reads the owning connection's login snapshot |
| `provider/auth/input` | request | Supplies one answer for the identified login and prompt |
| `provider/auth/cancel` | request | Cancels the owning connection's login |
| `provider/auth/status` | request | Returns authentication status for a provider |
| `provider/auth/logout` | request | Cancels pending login, logs out and invalidates credentials/catalog identity |
| `provider/auth/usage` | request | Returns provider account usage |
| `provider/auth/updated` | notification | Publishes a revisioned login snapshot to the initiating connection |
| `provider/auth/usage/updated` | notification | Publishes changed usage for one provider |

## Authentication methods

A provider's `AuthMethod` names its configured method. Built-in OpenAI-protocol providers offer
`apiKey` and `chatgptOAuth`; built-in Anthropic providers offer `apiKey`. A backend declares its
methods with an id, display name and kind: `apiKey`, `oauth`, `cli` or `ambient`.

Login is available for `chatgptOAuth` and for every backend method except `ambient`. A built-in
`apiKey` method is configured through configuration writes and rejects login. `provider/auth/login`
uses the configured method unless `method` names another method the provider offers. Login and
logout never write configuration; a client that lets the user choose a method saves `AuthMethod`
first.

ChatGPT credentials are account-wide: every `chatgptOAuth` provider on a host shares one sign-in,
and signing out through one provider signs out all of them. Login and logout invalidate cached
clients and model catalogs of every affected provider.

## Login lifecycle

Login state contains `loginId`, `providerId`, `revision`, `status`, optional event, optional prompt
and optional authentication status or error. Status is running, waiting, completed, cancelled or failed.
Input contains `loginId`, `promptId` and `value`. Replayed or stale prompt answers fail.
Prompt kinds are text, secret, select and manual_code. Events include auth_url, device_code, progress
and info. A device-code event includes its URL and user code. Clients never echo secret answers.

An `auth_url` event asks the client to open the URL in a browser; the host never opens one for an
AppServer client. `chatgptOAuth` emits one `auth_url` event and completes when the loopback callback
on the host succeeds.

Login notifications can arrive before the login request returns. Clients subscribe before starting
login, correlate login IDs, and merge only increasing revisions. Reading state recovers missed
notifications. Terminal states are retained for fifteen minutes after creation for reconciliation.
A connection cannot inspect or control another connection's login.

An instance can have only one login in progress. Explicit cancel, connection closure, generation
stopping and a fifteen-minute timeout cancel pending prompts and provider work. Logout waits for
cancelled login to terminate before removing credentials.

## Status

`provider/auth/status` takes `providerId` and optional `includeToken` and `refreshToken`. The result
contains `isAuthenticated`, `accountId`, `displayName`, `email`, `planType`, `failureCode` and
`failureMessage`. `refreshToken` forces a credential refresh before answering. `authToken` is
returned only for an explicit `includeToken: true` when the host stores the provider's credentials
locally and its method exposes an access token (`chatgptOAuth`); refresh failure omits it. Clients
use a returned token only for their own requests to the same service and never persist or log it.

`provider/list` reports `isAuthenticated` for every provider whose configured method supports login
and carries no account metadata.

## Usage

`provider/auth/usage` takes `providerId` and returns `available`, `fetchedAt`, `planType`,
`primary` and `secondary` windows (`usedPercent`, `windowSeconds`, `resetAt`), `credits`
(`hasCredits`, `unlimited`, `balance`) and `limitReachedKind`. A provider without usage returns
`available: false`. `provider/auth/usage/updated` carries `providerId` and the same fields; the host
sends one for each configured provider whose usage changed after polling, login or logout.

## Model service

Providers managed by a remote [model service](../architecture/model-service.md) report the service's
sign-in state and usage without credentials. Login, input, cancel and logout for them return
InvalidRequest naming the management location.

## Backends

Backend descriptors do not contain credentials. Backend IDs and settings are configured through
existing configuration writes. Provider listings add `backendId`; missing backend code does not
remove saved configuration.

## CLI

```
dotcraft auth login  <provider-id> [--method <id>] [--workspace <path>] [--no-browser]
dotcraft auth login  --stdin [--method <id>] [--no-browser]
dotcraft auth status <provider-id> [--workspace <path>]
dotcraft auth logout <provider-id> [--workspace <path>]
dotcraft auth verify [<provider-id>] [--workspace <path>]
```

The CLI runs the coordinator in-process. It prints `auth_url` and device-code events, opens the URL
unless `--no-browser` is given, and answers prompts on the console. `--stdin` reads an unsaved
provider draft, in the shape `model-catalog --stdin` accepts, so setup flows can sign in before the
provider is saved.

`verify` checks a deployment before it serves requests: it refreshes the credentials of the given
provider, or the selected one, and fails unless the provider is signed in and its preferred model is
listed in its catalog.
