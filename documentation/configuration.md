# Configuration

`appsettings.json` contains non-secret defaults:

- `AI:Provider`: `OpenAICompatible` (default), `Claude`, or `GitHubCopilot`. Mock is removed.
- `AI:Endpoint`: full HTTPS Chat Completions URL (HTTP permitted only for loopback development).
- `AI:Model` / `AI:Models`: default OpenAI-compatible model and suggested model IDs. Defaults to `gpt-4.1`; mini models remain an optional lower-cost choice.
- `AI:ApiKeyEnvironmentVariable`: environment variable holding the OpenAI-compatible key; defaults to `INCIDENT_AI_API_KEY`.
- `AI:Claude:Endpoint`: HTTPS Anthropic Messages URL, default `https://api.anthropic.com/v1/messages`.
- `AI:Claude:ApiKeyEnvironmentVariable`: defaults to `ANTHROPIC_API_KEY`.
- `AI:Claude:Model` / `AI:Claude:Models`: default and suggested Claude IDs (Sonnet/Opus). Use a model with JSON structured-output support.
- `AI:Copilot:Model` / `AI:Copilot:Models`: default and suggested GitHub Copilot model IDs. Sign in with `copilot login`; this does not use a Microsoft 365 Copilot subscription or an OpenAI key. If needed, locate the bundled `copilot.exe` under `bin\Debug\net10.0-windows\runtimes\win-x64\native` (RID differs on ARM64).
- `AI:MaxOutputTokens`: output budget, default 8192; valid range 1024-32768. Reduce evidence or raise the budget for incomplete responses, subject to model limits.
- `AI:TimeoutSeconds`: request timeout, default 180; valid range 30-600.
- `Investigation:MaxFileSizeMB`: maximum size for non-log text extraction. Logs are streamed and bounded by selected context.
- `Investigation:MaxContextCharacters`: selected text budget.
- `Investigation:SimilarityThreshold`: recurrence threshold from 0 to 1.
- `Investigation:AllowImageUpload`: reserved setting; image upload is not implemented in this version.
- `Investigation:MaxImagePreviewMB`: maximum image size loaded for local screenshot preview; image bytes are not sent to the AI provider.
- `Storage:Database`: SQLite path. Relative paths are rooted under the user's Local Application Data application folder.

Copilot's restricted SDK Empty mode requires an explicit local persistence directory even when memory and the shared session store are disabled. The adapter uses the same home as CLI sign-in: `COPILOT_HOME` when set, otherwise `%USERPROFILE%\.copilot`. Sign in with the same Windows account and environment used to launch the app. No token is copied into appsettings; SDK-owned config/session files may reside in this directory.

Provider and editable model selectors operate for the current session; changing providers restores the last selected model for that provider. Suggestions are configurable, not downloaded model-entitlement lists; model IDs and permissions vary by account. No fallback is attempted if a model/key is unavailable. Restart the app after editing appsettings and restart Visual Studio after changing user environment variables. Scan/evidence review do not require credentials, but investigation does.

Never commit credentials or paste them into chat. Configure the relevant key in Windows user environment variables or sign in separately to GitHub Copilot. No credentials are copied from Visual Studio chat. Do not submit company evidence without approval of the provider's data-handling/privacy policy.
