# Configuration Map

An index, not a field reference. For any field name, type, default, sensitive flag, or reload tier run `dotcraft config schema --json`; for current values run `dotcraft config show --json`. Prose lives at `https://www.dotcraft.net/developing/configuration`.

## Two files, one merged config

| File | Owns |
|---|---|
| `~/.craft/config.json` | Personal defaults for every workspace: `Providers` credentials and endpoints, personal tool and UI preferences |
| `<workspace>/.craft/config.json` | This project's settings: `ProviderId`, `ProviderPreferences`, project MCP servers, project skill and tool policy |

Workspace values win. Neither file has to exist.

## Merge semantics

- Objects merge recursively, key by key.
- Arrays and scalars replace wholesale. `McpServers`, `LspServers`, and `EnabledTools` are arrays, so a workspace value replaces the entire personal list rather than appending to it.
- `ProviderPreferences` replaces per provider id: a workspace entry for one provider leaves the other providers' personal entries intact.
- Keys are matched case-insensitively.
- `$VAR` replaces the whole string with the environment variable; `${VAR}` substitutes inline and may appear several times in one string. Both expand when the config is loaded. An unset variable keeps the placeholder text unchanged.
- An unknown property inside an `McpServers` or `LspServers` entry fails the whole config load, not just that entry. Check spelling against `dotcraft config schema --json` before saving.

## File edits need a restart

The running host does not watch these files. `AppConfig` is a snapshot taken at startup. A file edit takes effect at the next AppServer restart, and the Desktop settings pages do not pick it up in the meantime. Say "restart to apply" after every file edit, and never describe a file edit as already live.

The `hot` reload tier reported by `dotcraft config schema` describes changes made through Desktop Settings or the AppServer RPC surface. It does not apply to a hand-edited file. When the user wants a setting live now, point them at the matching Desktop Settings panel instead of editing the file.

## Sections

Root fields such as `ProviderId`, `Providers`, `ProviderPreferences`, `McpServers`, and `EnabledTools` sit at the top level of the file; every other setting lives in a named section object under its own key. `dotcraft config schema` lists the sections this build has, and `dotcraft config schema --section <key>` opens one.

## Providers

Three protocols exist. There are no others.

| `Protocol` | Default endpoint | Use for |
|---|---|---|
| `openai-chat-completions` | `https://api.openai.com/v1` | The default. Also Ollama, DeepSeek, OpenRouter, Azure, and any other OpenAI-compatible endpoint, by setting `EndPoint` |
| `openai-responses` | `https://api.openai.com/v1` | OpenAI Responses API providers |
| `anthropic` | `https://api.anthropic.com` | Anthropic |

An empty `EndPoint` uses the protocol default. `AuthMethod` is `apiKey` (default, reads `ApiKey`) or `chatgptOAuth`, which is only meaningful for the OpenAI protocols and is set up by `dotcraft auth openai login` or Desktop Settings > Models. Do not hand-write `chatgptOAuth` credentials; `ChatGptAccountId` and `ChatGptPlanType` are written by the login flow.

## Never write a literal key

Write an environment reference and tell the user which variable to set.

```json
{
  "Providers": {
    "anthropic": {
      "DisplayName": "Anthropic",
      "Protocol": "anthropic",
      "AuthMethod": "apiKey",
      "ApiKey": "${ANTHROPIC_API_KEY}"
    }
  }
}
```

To inspect configuration, prefer `dotcraft config show --json`, which masks sensitive fields as `***`. Reading `~/.craft/config.json` with `ReadFile` pulls whatever secrets it holds into the transcript.

## Worked example: switch this workspace to a different provider

`~/.craft/config.json` — the credential and endpoint, personal and shared across workspaces:

```json
{
  "Providers": {
    "anthropic": { "Protocol": "anthropic", "ApiKey": "${ANTHROPIC_API_KEY}" }
  }
}
```

`<workspace>/.craft/config.json` — the selection, scoped to this project:

```json
{
  "ProviderId": "anthropic",
  "ProviderPreferences": {
    "anthropic": { "Model": "<model id>" }
  }
}
```

Take the model id from Desktop Settings > Models or `dotcraft config show --json`; do not guess one. `ProviderPreferences` also carries `Reasoning`, `Speed`, and `ContextWindow` for that provider — read their shapes from `dotcraft config schema --section ProviderPreferences`.

Then tell the user: set `ANTHROPIC_API_KEY`, and restart to apply. Changing the provider in Desktop Settings > Models instead takes effect without a restart, and it only moves new threads — existing threads keep the model snapshot taken when they were created.
