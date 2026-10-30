# Configuration and credentials

Start with the [user guide](user-guide.md) for installation and the daily investigation workflow. You need only one provider. Do not copy credentials from Visual Studio/Copilot Chat, commit them, or paste them into chat.

## Choose a provider

| UI provider | Credential and where to obtain it | Subscription/billing |
|---|---|---|
| `OpenAICompatible` | OpenAI project API key from <https://platform.openai.com/api-keys> | API billing is separate from ChatGPT subscriptions. Configure billing/usage limits in the API dashboard. |
| `Claude` | Anthropic API key from <https://platform.claude.com/settings/keys> | Claude/Claude Code subscriptions do not fund this Messages API integration. Add API credits or billing in the Claude Console. |
| `GitHubCopilot` | Personal fine-grained token from <https://github.com/settings/personal-access-tokens/new>, or an interactive CLI OAuth login | The GitHub account must have Copilot access and permitted CLI/model use. Plan allowances and organization policies apply. Not Microsoft 365 Copilot. |

## Set a credential safely in PowerShell

Windows user environment variables persist across logins but are not a secret vault: processes running as your account can read them. Use a protected account/device. Never store a key in `appsettings.json`, a checked-in script, a screenshot, or the incident folder.

Open PowerShell and paste this helper once. It prompts without showing the key or putting the literal key into command history. It sets both the current terminal's process variable and the persistent Windows user variable:

```powershell
function Set-IncidentCredential {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('INCIDENT_AI_API_KEY', 'ANTHROPIC_API_KEY', 'COPILOT_GITHUB_TOKEN')]
        [string]$Name
    )
    $secure = Read-Host "Enter $Name (hidden)" -AsSecureString
    $pointer = [IntPtr]::Zero
    try {
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        $value = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        if ([string]::IsNullOrWhiteSpace($value)) { throw 'Credential cannot be empty.' }
        [Environment]::SetEnvironmentVariable($Name, $value, 'User')
        [Environment]::SetEnvironmentVariable($Name, $value, 'Process')
        Write-Host "$Name configured; value not displayed."
    }
    finally {
        if ($pointer -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
        }
        Remove-Variable value -ErrorAction SilentlyContinue
        $secure.Dispose()
    }
}
```

The plaintext must briefly exist in process memory to authenticate; masked entry does not make environment storage encrypted. If preferred, use Windows **Edit environment variables for your account > User variables > New**, entering the variable name and value locally.

### OpenAI setup

1. Sign in at <https://platform.openai.com/> using an account/project approved for your incident data.
2. Open **API keys**, create a project secret key, and copy it locally when shown. Restrict its permissions to what your project needs, including Chat Completions requests and model listing if you use the optional check below.
3. Set up API billing and usage limits. ChatGPT Plus/Pro is not an API credit balance.
4. In the PowerShell window containing the helper, run:

```powershell
Set-IncidentCredential -Name INCIDENT_AI_API_KEY
Set-Location C:\Repo\IntegrationIncidentInvestigator
dotnet run --project .\src\IntegrationIncidentInvestigator.App\IntegrationIncidentInvestigator.App.csproj
```

5. Select **OpenAICompatible** and an accessible model, initially the configured `gpt-4.1` if available. The default endpoint is `https://api.openai.com/v1/chat/completions`. The adapter requires structured JSON-schema output support; not every OpenAI-compatible server implements it.

The variable is `INCIDENT_AI_API_KEY`, not the OpenAI SDK's usual `OPENAI_API_KEY`, unless you deliberately change `AI:ApiKeyEnvironmentVariable`.

### Claude setup

1. Sign in to <https://platform.claude.com/>. Open **Settings > API keys** at <https://platform.claude.com/settings/keys> and create a key for the approved workspace.
2. Configure API billing/credits and applicable spending limits. A Claude chat or Claude Code subscription alone does not pay for this API path.
3. In the PowerShell window containing the helper, run:

```powershell
Set-IncidentCredential -Name ANTHROPIC_API_KEY
Set-Location C:\Repo\IntegrationIncidentInvestigator
dotnet run --project .\src\IntegrationIncidentInvestigator.App\IntegrationIncidentInvestigator.App.csproj
```

4. Select **Claude** and an exact Messages API model ID that supports structured output. Defaults suggest `claude-sonnet-4-6` and `claude-opus-4-6`; verify availability for your account. The endpoint is `https://api.anthropic.com/v1/messages`.

### GitHub Copilot setup: fine-grained token

This path was verified with the production adapter. No OpenAI or Anthropic key is required for a model offered through your GitHub Copilot account.

1. Sign in to the GitHub account that has Copilot access.
2. Go to <https://github.com/settings/personal-access-tokens/new> (**Settings > Developer settings > Personal access tokens > Fine-grained tokens**).
3. Fill out the form:

| Field | Value |
|---|---|
| Token name | `Integration Incident Investigator` |
| Description | `Copilot authentication for the local incident investigation app` |
| Resource owner | Your personal GitHub account with Copilot access, not an organization |
| Expiration | A short approved period, for example 30 days; renew before expiry |
| Repository access | Keep **Public repositories**; private repository permissions are not required for this tool's investigation requests |
| Account permissions | **Add permissions > Copilot Requests > Read-only** |

4. Do not grant unrelated repository, administration, or write permissions. Click **Generate token** and copy it locally. Classic `ghp_` tokens are not supported.
5. In the PowerShell window containing the helper, run:

```powershell
Set-IncidentCredential -Name COPILOT_GITHUB_TOKEN
Set-Location C:\Repo\IntegrationIncidentInvestigator
dotnet run --project .\src\IntegrationIncidentInvestigator.App\IntegrationIncidentInvestigator.App.csproj
```

6. Select **GitHubCopilot** and an accessible model. `gpt-4.1` succeeded in the verified incident run; suggestions are not guarantees of current access. An unavailable model must be changed explicitly.

If **Copilot Requests** is missing or policy blocks token/CLI use, ask your GitHub administrator rather than granting broader permissions. A personal token does not bypass organization restrictions.

The app reads `COPILOT_GITHUB_TOKEN` from the process first, then the current Windows user environment on each request. Consequently a newly added user token can be picked up without restarting Visual Studio. A stale nonempty process token still wins: restart Visual Studio after rotating/removing it. `GH_TOKEN` and `GITHUB_TOKEN` may also influence the CLI if no explicit app token is selected; do not set these merely for this app. An IDE-injected `COPILOT_SDK_AUTH_TOKEN` is not reused.

For the alternative browser login path, see [Copilot authentication](#copilot-authentication) below. An interactive CLI install is needed for OAuth login, but not just to use a PAT with the bundled SDK runtime.

## Check credential presence without revealing values

```powershell
foreach ($name in @('INCIDENT_AI_API_KEY', 'ANTHROPIC_API_KEY', 'COPILOT_GITHUB_TOKEN')) {
    [pscustomobject]@{
        Name = $name
        ProcessConfigured = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name, 'Process'))
        UserConfigured = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name, 'User'))
    }
}
```

`True` means a value exists, not that billing, permissions, model access, or authentication succeeded. Avoid `Get-ChildItem Env:` or printing the variables when sharing diagnostics.

OpenAI and Claude read process variables. If they were configured in another terminal or through Windows Settings, completely close/reopen Visual Studio before F5, or load your own user variable in the terminal you use to launch the app:

```powershell
$env:INCIDENT_AI_API_KEY = [Environment]::GetEnvironmentVariable('INCIDENT_AI_API_KEY', 'User')
$env:ANTHROPIC_API_KEY = [Environment]::GetEnvironmentVariable('ANTHROPIC_API_KEY', 'User')
```

### Optional model-list checks (no incident content)

These authenticated GET requests contact the selected provider and print only model IDs. They require a configured key and permission to list models; a successful listing does not prove a specific model supports this tool's schema or that inference billing is enabled.

```powershell
# OpenAI
$headers = @{ Authorization = "Bearer $env:INCIDENT_AI_API_KEY" }
try {
    (Invoke-RestMethod -Uri 'https://api.openai.com/v1/models' -Headers $headers -Method Get).data |
        Select-Object -ExpandProperty id | Sort-Object
}
finally { Remove-Variable headers }

# Anthropic Claude
$headers = @{ 'x-api-key' = $env:ANTHROPIC_API_KEY; 'anthropic-version' = '2023-06-01' }
try {
    (Invoke-RestMethod -Uri 'https://api.anthropic.com/v1/models' -Headers $headers -Method Get).data |
        Select-Object -ExpandProperty id
}
finally { Remove-Variable headers }
```

The Claude snippet lists the first API page, not necessarily every available model. The desktop model dropdown is configurable/editable, not automatically populated by these commands. Copilot users can consult the authenticated interactive CLI's `/model` picker for suggestions; plan and SDK model compatibility still apply.

## Rotate or remove a key

Revoke exposed/unused keys in the issuing provider's dashboard, then generate a replacement and rerun `Set-IncidentCredential`. Do not send the replacement to another person or to chat. For removal, change the name below to the specific provider you intend to disable:

```powershell
$name = 'COPILOT_GITHUB_TOKEN'
[Environment]::SetEnvironmentVariable($name, $null, 'User')
[Environment]::SetEnvironmentVariable($name, $null, 'Process')
```

Other running processes can retain old copies until restarted. Removing an environment variable does not revoke the credential at the provider or remove a separately saved CLI OAuth login.

## Settings reference

Edit the repository-root `appsettings.json` for source builds. For a published app, edit the copy beside the executable. Stop/restart the app after editing defaults; rebuilding can replace the build-output copy. JSON Windows paths need escaped backslashes, for example `"C:\\IncidentHistory\\incidents.db"`. Keep secrets out of JSON.

`appsettings.json` contains non-secret defaults:

- `AI:Provider`: `OpenAICompatible` (default), `Claude`, or `GitHubCopilot`. Mock is removed.
- `AI:Endpoint`: full HTTPS Chat Completions URL (HTTP permitted only for loopback development).
- `AI:Model` / `AI:Models`: default OpenAI-compatible model and suggested model IDs. Defaults to `gpt-4.1`; mini models remain an optional lower-cost choice.
- `AI:ApiKeyEnvironmentVariable`: environment variable holding the OpenAI-compatible key; defaults to `INCIDENT_AI_API_KEY`.
- `AI:Claude:Endpoint`: HTTPS Anthropic Messages URL, default `https://api.anthropic.com/v1/messages`.
- `AI:Claude:ApiKeyEnvironmentVariable`: defaults to `ANTHROPIC_API_KEY`.
- `AI:Claude:Model` / `AI:Claude:Models`: default and suggested Claude IDs (Sonnet/Opus). Use a model with JSON structured-output support.
- `AI:Copilot:Model` / `AI:Copilot:Models`: default and suggested GitHub Copilot model IDs. Authenticate using your fine-grained token or the interactive CLI below; this does not use a Microsoft 365 Copilot subscription or an OpenAI key. The bundled SDK `copilot.exe` is a runtime wrapper, NOT an interactive login executable.
- `AI:Copilot:HomeDirectory`: optional non-secret custom Copilot home path; defaults to `%USERPROFILE%\.copilot`. Use the same directory for CLI OAuth sign-in.
- `AI:MaxOutputTokens`: output budget used by the direct OpenAI/Claude adapters, default 8192; valid range 1024-32768. Copilot inference limits are managed by its SDK/provider. Reduce evidence or raise the budget for incomplete direct-provider responses, subject to model limits.
- `AI:TimeoutSeconds`: request timeout, default 180; valid range 30-600.
- `Investigation:MaxFileSizeMB`: maximum size for non-log text extraction. Logs are streamed and bounded by selected context.
- `Investigation:MaxContextCharacters`: selected text budget.
- `Investigation:SimilarityThreshold`: recurrence threshold from 0 to 1.
- `Investigation:AllowImageUpload`: reserved setting; image upload is not implemented in this version.
- `Investigation:MaxImagePreviewMB`: maximum image size loaded for local screenshot preview; image bytes are not sent to the AI provider.
- `Storage:Database`: SQLite path. Relative paths are rooted under the user's Local Application Data application folder.

Copilot's restricted SDK Empty mode requires an explicit local persistence directory even when memory and the shared session store are disabled. The adapter defaults to `%USERPROFILE%\.copilot` and deliberately ignores inherited `COPILOT_HOME`: Visual Studio can inject a private IDE home that differs from a normal terminal's login home. To deliberately use another home, set the non-secret `AI:Copilot:HomeDirectory` setting and use the same path for CLI sign-in. No token is copied into appsettings; SDK-owned config/session files may reside in this directory.

## Copilot authentication

Install the interactive CLI if needed:

```powershell
winget install --id GitHub.Copilot --exact
```

Then open a fresh PowerShell window and verify:

```powershell
copilot --version
copilot login --help
```

Do not use the executable bundled under the app output for login; it supports runtime transport only. If the CLI confirms login but the SDK cannot see it, prefer the fine-grained token path above rather than repeatedly signing in.

The pinned SDK's Empty mode disables the OS keychain and uses its explicit Copilot home. To match that mode during CLI sign-in, open a separate PowerShell window and run:

```powershell
$env:COPILOT_HOME = Join-Path $env:USERPROFILE '.copilot'
$env:COPILOT_DISABLE_KEYTAR = '1'
copilot login --device-code
```

Open the URL printed by the CLI, enter the device code in that browser page, and finish authorization with your Copilot-enabled account. Wait for the terminal to confirm success, then close that PowerShell window and restart the app. Do not share the device code or tokens. If `AI:Copilot:HomeDirectory` is configured, replace the first command's path with that directory. Merely running the command is not a completed login.

This storage mode may save the OAuth credential in files under the selected Copilot home rather than Windows Credential Manager. Protect that directory and never commit or share its files. Alternatively, use a user-owned fine-grained GitHub token with the Copilot Requests permission in `COPILOT_GITHUB_TOKEN` (Windows user environment variables). The adapter reads the process variable first, then the current Windows user variable on every request, so a newly added user token does not require restarting Visual Studio. A process-level token takes precedence until that process is restarted or its variable is removed. Never put tokens in appsettings or chat.

The SDK's `Environment` option replaces the entire child-process environment. The adapter preserves Windows/network settings, disables telemetry, removes inherited native file-logging settings, and explicitly passes the configured token through `GitHubToken`. An IDE-injected `COPILOT_SDK_AUTH_TOKEN` is discarded, not reused. Stripping the environment can cause both missing authentication and native HTTPS failures even when the same token works with a normal HTTP client.

Provider and editable model selectors operate for the current session; changing providers restores the last selected model for that provider. Suggestions are configurable, not downloaded model-entitlement lists; model IDs and permissions vary by account. No fallback is attempted if a model/key is unavailable. Restart the app after editing appsettings. Restart Visual Studio after changing OpenAI/Claude user variables, or any stale process-level Copilot token. Scan/evidence review do not require credentials, but investigation does.

## Troubleshooting

| Symptom | Action |
|---|---|
| SDK version not found | Install the SDK selected by `global.json`; run `dotnet --version` from the repository root, not its parent. |
| Application project cannot be started | Set `IntegrationIncidentInvestigator.App` as startup, not the Application library. |
| Build reports locked executable/DLL | Stop debugging and close your running app, then rebuild. Do not terminate unrelated processes. |
| Missing environment variable | Use the presence-only check above, then launch from the configured terminal or restart Visual Studio. |
| OpenAI HTTP 401 / Claude authentication failure | Check the correct provider's key, expiry, project/workspace and variable name. Never paste the key into an error report. |
| HTTP 403 / access denied | Check account/organization permissions and model entitlement; do not grant unrelated permissions. |
| HTTP 429 | Check rate limits and provider billing/quota. Wait or reduce usage; do not retry in a tight loop. |
| Claude HTTP 400 | Check API credits/billing and exact model/schema support in the Claude Console. Insufficient API credit was a previously observed cause; a Claude Code subscription alone did not resolve it. |
| Copilot still says not signed in | Update/rebuild the app, stop the old executable, and rerun. Verify `COPILOT_GITHUB_TOKEN` is present, fine-grained, unexpired, and has Copilot Requests permission. Check for a stale process token. If using OAuth, complete browser authorization with the same home/Windows account. |
| Copilot native network fetch / HTTPS failure | Use the latest app: older code replaced the entire child environment, breaking Windows network/authentication settings. If it persists, check approved proxy/firewall/certificate configuration with IT. Never disable TLS verification as a workaround. |
| Selected model rejected | Enter an exact model ID supported by that provider/account. Dropdown suggestions are not a live catalog; no fallback is automatic. |
| Invalid/incomplete investigation response | Reduce evidence and retry with a schema-capable model; check output and timeout limits. The app rejects invalid output rather than saving it as success. |
| ZIP/PDF contents missing | These formats have no extractor. Manually extract approved text into the folder, rescan, and prepare evidence again. |
| Error details missing from screenshots | Images are local previews/metadata only. Add the important text to `problem.txt` or the editable problem description. |
| Empty/truncated evidence | Inspect selection reasons; deselect unrelated files, adjust the text/file budget within valid limits, and prepare again. |
| No result files after investigation | Click **Save investigation outputs**. The app writes reports only on explicit save, to the source folder. Check write permissions. Successful investigations are separately saved in local history. |

On an authentication failure before evidence submission, incident excerpts are not sent. Once AI submission begins, a later timeout, cancellation, or response-validation failure does not mean the provider received no evidence. Check provider data-handling policies before submitting company data.

## Official references

- [OpenAI API quickstart](https://developers.openai.com/api/docs/quickstart)
- [Claude API overview](https://platform.claude.com/docs/en/api/overview)
- [GitHub Copilot CLI authentication and supported tokens](https://docs.github.com/en/copilot/how-tos/copilot-cli/set-up-copilot-cli/authenticate-copilot-cli)
- [GitHub Copilot network troubleshooting](https://docs.github.com/en/copilot/how-tos/troubleshoot-copilot/troubleshoot-network-errors)

Never commit credentials or paste them into chat. No credentials are copied from Visual Studio chat. Do not submit company evidence without approval of the provider's data-handling/privacy policy.
