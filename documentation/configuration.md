# Configuration

`appsettings.json` contains non-secret defaults:

- `AI:Provider`: `OpenAICompatible` (default), `Claude`, or `GitHubCopilot`. Mock is removed.
- `AI:Endpoint`: full HTTPS Chat Completions URL (HTTP permitted only for loopback development).
- `AI:Model` / `AI:Models`: default OpenAI-compatible model and suggested model IDs. Defaults to `gpt-4.1`; mini models remain an optional lower-cost choice.
- `AI:ApiKeyEnvironmentVariable`: environment variable holding the OpenAI-compatible key; defaults to `INCIDENT_AI_API_KEY`.
- `AI:Claude:Endpoint`: HTTPS Anthropic Messages URL, default `https://api.anthropic.com/v1/messages`.
- `AI:Claude:ApiKeyEnvironmentVariable`: defaults to `ANTHROPIC_API_KEY`.
- `AI:Claude:Model` / `AI:Claude:Models`: default and suggested Claude IDs (Sonnet/Opus). Use a model with JSON structured-output support.
- `AI:Copilot:Model` / `AI:Copilot:Models`: default and suggested GitHub Copilot model IDs. Sign in using the full interactive Copilot CLI as described below; this does not use a Microsoft 365 Copilot subscription or an OpenAI key. The bundled SDK `copilot.exe` is a runtime wrapper, NOT an interactive login executable.
- `AI:MaxOutputTokens`: output budget, default 8192; valid range 1024-32768. Reduce evidence or raise the budget for incomplete responses, subject to model limits.
- `AI:TimeoutSeconds`: request timeout, default 180; valid range 30-600.
- `Investigation:MaxFileSizeMB`: maximum size for non-log text extraction. Logs are streamed and bounded by selected context.
- `Investigation:MaxContextCharacters`: selected text budget.
- `Investigation:SimilarityThreshold`: recurrence threshold from 0 to 1.
- `Investigation:AllowImageUpload`: reserved setting; image upload is not implemented in this version.
- `Investigation:MaxImagePreviewMB`: maximum image size loaded for local screenshot preview; image bytes are not sent to the AI provider.
- `Storage:Database`: SQLite path. Relative paths are rooted under the user's Local Application Data application folder.

Copilot's restricted SDK Empty mode requires an explicit local persistence directory even when memory and the shared session store are disabled. The adapter defaults to `%USERPROFILE%\.copilot` and deliberately ignores inherited `COPILOT_HOME`: Visual Studio can inject a private IDE home that differs from a normal terminal's login home. To deliberately use another home, set the non-secret `AI:Copilot:HomeDirectory` setting and use the same path for CLI sign-in. No token is copied into appsettings; SDK-owned config/session files may reside in this directory.

## Copilot authentication

Install the interactive CLI if needed with `winget install GitHub.Copilot`, then open a fresh PowerShell window. `copilot --version` must work. Do not use the executable bundled under the app output for login; it supports runtime transport only.

The pinned SDK's Empty mode disables the OS keychain and uses its explicit Copilot home. To match that mode during CLI sign-in, open a separate PowerShell window and run:

```powershell
$env:COPILOT_HOME = Join-Path $env:USERPROFILE '.copilot'
$env:COPILOT_DISABLE_KEYTAR = '1'
copilot login --device-code
```

Open the URL printed by the CLI, enter the device code in that browser page, and finish authorization with your Copilot-enabled account. Wait for the terminal to confirm success, then close that PowerShell window and restart the app. Do not share the device code or tokens. If `AI:Copilot:HomeDirectory` is configured, replace the first command's path with that directory. Merely running the command is not a completed login.

This storage mode may save the OAuth credential in files under the selected Copilot home rather than Windows Credential Manager. Protect that directory and never commit or share its files. Alternatively, use a user-owned fine-grained GitHub token with the Copilot Requests permission in `COPILOT_GITHUB_TOKEN` (Windows user environment variables). The adapter reads the process variable first, then the current Windows user variable on every request, so a newly added user token does not require restarting Visual Studio. A process-level token takes precedence until that process is restarted or its variable is removed. Never put tokens in appsettings or chat.

The SDK's `Environment` option replaces the entire child-process environment. The adapter preserves Windows/network settings, disables telemetry, removes inherited native file-logging settings, and explicitly passes the configured token through `GitHubToken`. An IDE-injected `COPILOT_SDK_AUTH_TOKEN` is discarded, not reused. Stripping the environment can cause both missing authentication and native HTTPS failures even when the same token works with a normal HTTP client.

Provider and editable model selectors operate for the current session; changing providers restores the last selected model for that provider. Suggestions are configurable, not downloaded model-entitlement lists; model IDs and permissions vary by account. No fallback is attempted if a model/key is unavailable. Restart the app after editing appsettings and restart Visual Studio after changing user environment variables. Scan/evidence review do not require credentials, but investigation does.

Never commit credentials or paste them into chat. Configure the relevant key in Windows user environment variables or sign in separately to GitHub Copilot. No credentials are copied from Visual Studio chat. Do not submit company evidence without approval of the provider's data-handling/privacy policy.
