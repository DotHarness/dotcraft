# Provider backend authentication

Backend management is available in the local App host under the existing provider-management
capability. Independent ModelService configuration remains remote-managed.

| Method | Kind | Behavior |
|---|---|---|
| `provider/backend/list` | request | Returns active backend descriptors, protocols and authentication methods |
| `provider/auth/login` | request | Starts one login for a configured provider; returns its current state immediately |
| `provider/auth/state` | request | Reads the owning connection's login snapshot |
| `provider/auth/input` | request | Supplies one answer for the identified login and prompt |
| `provider/auth/cancel` | request | Cancels the owning connection's login |
| `provider/auth/status` | request | Returns non-secret authentication status for a provider |
| `provider/auth/logout` | request | Cancels pending login, logs out and invalidates credentials/catalog identity |
| `provider/auth/usage` | request | Returns optional provider usage |
| `provider/auth/updated` | notification | Publishes a revisioned login snapshot to the initiating connection |

Login state contains `loginId`, `providerId`, `revision`, `status`, optional event, optional prompt
and optional authentication status or error. Status is running, waiting, completed, cancelled or failed.
Input contains `loginId`, `promptId` and `value`. Replayed or stale prompt answers fail.
Prompt kinds are text, secret, select and manual_code. Events include auth_url, device_code, progress
and info. A device-code event includes its URL and user code. Clients never echo secret answers.

Login notifications can arrive before the login request returns. Clients subscribe before starting
login, correlate login IDs, and merge only increasing revisions. Reading state recovers missed
notifications. Terminal states are retained for fifteen minutes after creation for reconciliation.
A connection cannot inspect or control another connection's login.

An instance can have only one login in progress. Explicit cancel, connection closure, generation
stopping and a fifteen-minute timeout cancel pending prompts and provider work. Logout waits for
cancelled login to terminate before removing credentials.

Backend descriptors do not contain credentials. Backend IDs and settings are configured through
existing configuration writes. Provider listings add `backendId`; missing backend code does not
remove saved configuration. Existing auth/openai methods retain their current wire behavior.
