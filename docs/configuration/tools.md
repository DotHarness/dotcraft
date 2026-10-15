# Tools

Limits and switches for the built-in tools. What the agent may touch is set in [Permissions](./permissions).

## Files and shell

| Field | Description | Default |
|---|---|---|
| `Tools.File.MaxFileSize` | Max readable file size in bytes | `10485760` |
| `Tools.File.RipgrepPath` | Optional `rg` path; empty tries `DOTCRAFT_RG_PATH`, `PATH`, then fallback | `""` |
| `Tools.File.SearchTimeoutSeconds` | Max `GrepFiles` content-search time before timeout | `30` |
| `Tools.Shell.Timeout` | Seconds the agent waits for a command before stopping it. A command still running after `Tools.Shell.Background.ForegroundBudgetMs` is instead returned to the agent and keeps running until it exits or is stopped, unless background terminals are disabled | `300` |
| `Tools.Shell.MaxOutputLength` | Max shell output length in characters | `10000` |

## Background terminals

| Field | Description | Default |
|---|---|---|
| `Tools.Shell.Background.Enabled` | Enable background terminal sessions | `true` |
| `Tools.Shell.Background.ForegroundBudgetMs` | How long the agent waits for a command before it continues in the background. A message you send while it waits moves it to the background at once | `120000` |
| `Tools.Shell.Background.DefaultYieldTimeMs` | Default wait before a command started in the background returns its session | `10000` |
| `Tools.Shell.Background.MaxYieldTimeMs` | Maximum wait for a background command to return, and for a background-session read or write | `300000` |
| `Tools.Shell.Background.CompletionDelivery` | `wake` tells the agent when a background command it is waiting on finishes, starting a new turn if the thread is idle. `off` leaves the agent to check the command itself | `wake` |
| `Tools.Shell.Background.MaxConsecutiveWakes` | Maximum turns in a row that finished background commands can start on their own before the agent waits for you | `3` |
| `Tools.Shell.Background.MaxSessionsPerThread` | Maximum concurrent background terminals per thread | `8` |
| `Tools.Shell.Background.MaxSessionsPerWorkspace` | Maximum concurrent background terminals in one workspace | `32` |
| `Tools.Shell.Background.IdleTimeoutSeconds` | Reserved; not currently enforced by the background terminal service | `1800` |
| `Tools.Shell.Background.OutputMaxBytes` | Reserved; not currently enforced by the background terminal service | `67108864` |
| `Tools.Shell.Background.OutputRetentionDays` | Retention window for completed or lost terminal metadata and output; running terminals are excluded | `7` |
| `Tools.Shell.Background.StallWatchdogSeconds` | Seconds of silence after a prompt-like line, such as `(y/n)`, before a running command is reported as waiting for input. `0` turns this off | `45` |
| `Tools.Shell.Background.DefaultReadMaxOutputChars` | Default maximum characters returned in a terminal snapshot | `10000` |

## Web

| Field | Description | Default |
|---|---|---|
| `Tools.Web.MaxChars` | Max chars for web fetch | `50000` |
| `Tools.Web.Timeout` | Web request timeout in seconds | `300` |
| `Tools.Web.SearchMaxResults` | Default search result count | `5` |
| `Tools.Web.SearchProvider` | `Bing` / `Exa` | `Exa` |

## Tool results

| Field | Description | Default |
|---|---|---|
| `Tools.ResultLimits.MaxToolResultChars` | Default tool result length in characters before the result spills to disk; `0` removes the limit for tools that use the global default | `50000` |
| `Tools.ResultLimits.SpillPreviewLines` | Head and tail lines kept in the preview when a result spills to disk | `40` |

## Image generation

| Field | Description | Default |
|---|---|---|
| `Tools.ImageGeneration.Enabled` | Lets the agent generate and edit images with supported OpenAI providers | `true` |
| `Tools.ImageGeneration.Provider` | Provider id that generates images. Empty uses the conversation's provider | Empty |
| `Tools.ImageGeneration.Model` | Image model used for generation and edits | `gpt-image-2` |
| `Tools.ImageGeneration.MaxReferenceImages` | Maximum reference images one edit can use, from `1` to `5` | `5` |

Generated images are saved under the Agent data directory at `generated_images/<threadId>/<callId>.png`. When connected to a remote computer, files are saved to the remote workspace’s `.craft/generated_images/<threadId>/<callId>.png` instead. If saving fails, the conversation still displays the generated image and reports the storage failure.

With a supported provider, ask DotCraft to generate an image, or to edit an attached, local, or previously generated one, in a normal conversation. DotCraft calls the OpenAI Images API, saves the PNG, and shows the image inline in clients that render rich content.

Two switches gate the `image_gen.imagegen` tool, and both must be true: the global `Tools.ImageGeneration.Enabled`, and the image provider's own `SupportsImageGeneration`. The image provider must also use an OpenAI protocol. Omitting the provider field leaves ChatGPT OAuth and the official OpenAI API-key endpoint enabled, and other endpoints disabled. Enable another endpoint only once you know it serves `images/generations` and `images/edits`.

The image provider is the conversation's provider unless `Tools.ImageGeneration.Provider` names another one. Set it to keep image generation available while you chat through a provider without an Images API, such as an Anthropic or a custom OpenAI-compatible endpoint:

```json
{
  "Tools": {
    "ImageGeneration": {
      "Provider": "openai"
    }
  }
}
```

If the named provider is missing or cannot generate images, the tool is not offered; DotCraft does not fall back to the conversation's provider.

## Code mode

| Field | Description | Default |
|---|---|---|
| `Tools.CodeMode.Mode` | Scripted tool calls: `only`, `on`, or `off` | `only` |

`Tools.CodeMode.Mode` controls scripted tool calls. The `CodeMode` tool runs one JavaScript program that calls the conversation's other tools and returns only what the model needs, so batches of calls take one model round trip and large intermediate results stay out of context. With `only`, every tool a script can call leaves the model's direct tool list and is reached through `CodeMode`. `on` adds `CodeMode` and keeps every tool directly callable, and `off` removes it. In Desktop, change it under **Settings › General › Tools**.

Scripts run in a worker process started from the DotCraft executable. If the worker cannot start, `on` falls back to direct tools, while `only` fails the turn with `code_mode_unavailable`; use `on` or `off` where the host cannot launch it.
