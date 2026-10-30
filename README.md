# Integration Incident Investigator

A Windows WPF application for reviewing local integration-incident evidence, creating an evidence-based investigation with a configurable AI provider, checking recurrence against local history, and preparing copy-ready Teams/Jira content. The first version does not connect to Jira or Teams and never creates tickets or sends messages.

## Start here

1. Follow the [complete user guide](documentation/user-guide.md) to install, run, and investigate an incident.
2. Configure **one** provider using [credentials and configuration](documentation/configuration.md): where to obtain OpenAI/Claude keys or a Copilot token, exact permissions, masked PowerShell commands, billing, and troubleshooting.
3. Scan a folder, review file selections and prepared excerpts, then explicitly send the approved evidence to AI.
4. Review the result, choose new Jira versus existing Jira comment, and copy/save the outputs. Posting and attaching remain manual.

### Documentation

- [User guide: installation, daily workflow, outputs and publishing](documentation/user-guide.md)
- [Provider keys, authentication commands, configuration and troubleshooting](documentation/configuration.md)
- [Architecture](documentation/architecture.md)
- [AI investigation design](documentation/ai-investigation.md)
- [Recurrence detection](documentation/recurrence-detection.md)

## Architecture

The solution uses inward-facing Clean Architecture dependencies:

- `Core`: incident/evidence models, file classification, fingerprints, and recurrence matching.
- `Application`: investigation orchestration, provider/history/file/output interfaces, prompt construction, and structured response validation.
- `Infrastructure`: bounded file extraction, local SQLite history, OpenAI-compatible Chat Completions, Anthropic Claude Messages, and the official GitHub Copilot SDK.
- `App`: WPF desktop UI and dependency-injection composition.
- `Tests`: offline xUnit tests; no real AI endpoint is used by tests.

## Build and run

Requirements: Windows, .NET 10 SDK, and Windows Desktop targeting support. The included `global.json` selects .NET SDK `10.0.401` (latest patch roll-forward).

```powershell
dotnet restore .\IntegrationIncidentInvestigator.sln
dotnet test .\IntegrationIncidentInvestigator.sln
dotnet run --project .\src\IntegrationIncidentInvestigator.App\IntegrationIncidentInvestigator.App.csproj
```

Select an incident directory, scan and review the files, enter optional system/integration names, edit the problem description if needed, then prepare and review the exact excerpts before explicitly sending them. Choose OpenAICompatible, Claude, or GitHubCopilot and a model in the UI. The model selector is editable so you can enter your provider's exact supported model ID; suggestions are not account-entitlement guarantees. Provider/model selections are session-local and retained separately when switching providers. Mock is no longer available in the application; deterministic fakes live only in tests. Scanning/evidence review still work offline. Review results and selected attachments before copying or saving artifacts. Images are previewed locally up to `Investigation:MaxImagePreviewMB`; this version does not upload image bytes.

## AI provider configuration

`appsettings.json` contains non-secret settings and defaults to `OpenAICompatible` with `gpt-4.1` for stronger analysis than the previous mini-model default.

| Provider | Setup | Model selection |
|---|---|---|
| OpenAICompatible | Set `INCIDENT_AI_API_KEY`; configure `AI:Endpoint` to an HTTPS Chat Completions URL. | `AI:Model` / `AI:Models`; supports strict JSON-schema output. |
| Claude | Set `ANTHROPIC_API_KEY` from the Anthropic API console; Claude chat subscriptions are separate. | `AI:Claude:Model` / `AI:Claude:Models`; use a Messages API model with structured-output support. |
| GitHubCopilot | Sign in with `copilot login` using a Copilot-enabled account and an available model. | `AI:Copilot:Model` / `AI:Copilot:Models`; access and usage limits depend on your plan/policy. |

For OpenAI/Claude, launch from the terminal where credentials were set or restart Visual Studio after setting Windows user variables. Copilot also reads the current Windows user `COPILOT_GITHUB_TOKEN` on each request, unless a process-level token takes precedence. Do not place keys in appsettings or source control. No credentials from this Visual Studio chat are extracted.

The official Copilot SDK pins/downloads its runtime during build (so the build output is larger). The bundled `copilot.exe` is a runtime wrapper and does NOT support interactive login. Install the full interactive CLI with `winget install GitHub.Copilot` if it is not already available, then follow the restricted-runtime authentication steps in `documentation/configuration.md`. The app uses `%USERPROFILE%\.copilot` by default rather than an IDE-injected `COPILOT_HOME`; configure `AI:Copilot:HomeDirectory` only for an intentional custom login home. The app checks authentication before submitting incident evidence.

The provider sends only reviewed, bounded excerpts after explicit submission. Provider requests use TLS except for configured OpenAI-compatible loopback development endpoints. No silent fallback to another provider/model occurs. API usage may incur charges. `AI:MaxOutputTokens` and `AI:TimeoutSeconds` bound responses. Model capability improves analysis but does not replace good evidence or human review.

Jira stories use typed sections: title, objective, user story, description, exact observed errors, scope, and numbered acceptance criteria (including safe reprocessing, duplicate prevention and reconciliation where applicable). The app renders these sections consistently for preview/copy/save. Instructions require source-backed specifics and distinguish symptoms, confirmed causes, completed actions and proposed remediation. Put any known cause or follow-up discussion into the reviewed problem description; the app cannot infer unseen Jira comments. Invalid responses do not produce investigation artifacts. AI output is advisory; it cannot create tickets or send messages.

## Recurrence detection

Incident history is stored locally in SQLite under the current user's Local Application Data directory. The recurrence matcher compares the integration name and normalized error text using token overlap and can boost a match for the same normalized fingerprint. It reports its score and reasons; a possible recurrence is not a confirmed cause or an instruction to comment on a Jira. The user chooses the Jira mode. This initial history store does not yet include a CSV importer/exporter.

## Privacy and file processing

- Folder enumeration includes subdirectories. Generated output files are skipped on subsequent scans.
- Configuration/unknown file types are excluded by default; the user can include or exclude discovered files.
- Plain text is capped by the per-file size setting and total context setting. Diagnostic/error reports are prioritized; the remaining text budget is shared across included files so a large payload cannot consume it all. Logs are streamed line by line and select bounded error lines with nearby context; full log files are not sent. HTML/HTM diagnostic audits are included as bounded source text, never rendered or executed.
- Images are shown locally (subject to `Investigation:MaxImagePreviewMB`) and represented only as metadata in the current request. `Investigation:AllowImageUpload` is false and image upload is not implemented.
- Incident content and API keys are not written to application logs. History is local; SQLite is not encrypted by the app, so protect the Windows account/device and database location.
- The user must review the file list and explicitly start AI analysis. No automatic external Jira/Teams actions exist.

## Extending the application

### Add a file extractor

Implement `IFileContentExtractor` (or add a strategy behind `FileContentExtractor`) to return a bounded `EvidenceExcerpt` with the original size, selected character count, and selection reason. Keep reads cancellable, stream large files, and add tests for size limits and malformed content.

### Add an AI provider

Implement `ILlmClient`, register it in `InfrastructureRegistration`, and select it through `AI:Provider`. Keep credentials in environment variables or an OS-managed secret store. Return structured JSON and retain response validation in the Application layer.

## Tests

Run `dotnet test .\IntegrationIncidentInvestigator.sln`. Tests use deterministic local inputs, in-memory/fake services where appropriate, and never call an AI API.
