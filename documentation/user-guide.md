# User guide

Integration Incident Investigator is a Windows WPF desktop tool for turning local integration evidence into a reviewed investigation, a Teams summary, and Jira drafts. It does not create tickets, upload Jira attachments, or post to Teams.

## 1. Install and start

Requirements:

- Windows with permission to read the incident folder and write outputs there.
- Git and the .NET 10 SDK selected by `global.json` (10.0.401, with patch roll-forward).
- Internet access for package/runtime downloads and AI requests.
- One approved AI provider account. You do not need all three providers.
- Visual Studio with the .NET desktop development workload if you want to run/debug in Visual Studio.

Download .NET from <https://dotnet.microsoft.com/download/dotnet/10.0> and Git from <https://git-scm.com/downloads/win>.

### New installation

Run in PowerShell. Clone only if the destination does not already exist:

```powershell
Set-Location C:\Repo
git clone https://github.com/dawood-myob/IntegrationIncidentInvestigator.git
Set-Location C:\Repo\IntegrationIncidentInvestigator
dotnet --version
dotnet restore .\IntegrationIncidentInvestigator.sln
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
dotnet build .\IntegrationIncidentInvestigator.sln --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
dotnet test .\tests\IntegrationIncidentInvestigator.Tests\IntegrationIncidentInvestigator.Tests.csproj --no-restore
```

Configure one provider using [Configuration and credentials](configuration.md), then start:

```powershell
Set-Location C:\Repo\IntegrationIncidentInvestigator
dotnet run --project .\src\IntegrationIncidentInvestigator.App\IntegrationIncidentInvestigator.App.csproj
```

In Visual Studio, open `IntegrationIncidentInvestigator.sln`, set **IntegrationIncidentInvestigator.App** as the startup project, and press **F5**. `Application` is a business-logic library, not the executable.

### Update an existing checkout

Stop the app first. Inspect local changes; do not discard your own changes to make an update succeed.

```powershell
Set-Location C:\Repo\IntegrationIncidentInvestigator
git status --short
git pull --ff-only origin main
if ($LASTEXITCODE -ne 0) { throw 'Update failed. Resolve local changes or branch divergence before continuing.' }
dotnet build .\IntegrationIncidentInvestigator.sln
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
dotnet run --project .\src\IntegrationIncidentInvestigator.App\IntegrationIncidentInvestigator.App.csproj
```

## 2. Prepare an incident folder

Use a separate folder with a unique folder name for every incident, for example:

```text
C:\IncidentData\INC-2026-001
	problem.txt
	integration.log
	Diagnostic_Audit.html
	input.csv
	post-process-data.xml
	screenshot.png
```

The folder name becomes the incident ID in local history. Reusing the same name can update the existing history entry instead of creating a distinct incident.

Recommended `problem.txt` content:

```text
Integration: INT008 Acumatica Billing Journals Inbound
System: Workday
Observed: 28 October 2026 at 02:05, time zone to be confirmed
Expected: Import the inbound journal file successfully.
Actual: Completed with errors; the diagnostic audit is included.
Impact: State affected records and business impact, or mark unknown.
Already checked: Record checks and their results.
Known cause: State only confirmed facts; otherwise write unknown.
Actions taken: Separate completed actions from proposed actions.
Question: Identify the likely cause and a safe correction/reprocessing plan.
```

Do not include credentials. Minimize personal, payroll, customer, and other confidential data, and obtain approval before sending company evidence to the selected provider. Existing Jira comments are not fetched: put relevant, approved discussion or confirmed resolutions in the problem description yourself.

## 3. Scan and review files

1. Click **Browse...** or enter the incident folder.
2. Click **Scan folder**. Subfolders are scanned, but reparse-point files/folders and known generated outputs are skipped.
3. Enter consistent **System** and **Integration** names to improve recurrence matching.
4. Review the classification, size, relevance, and recommendation reasons in **Incident files**.
5. Set **Use for AI** for evidence you want considered. Configuration/unknown files are excluded by default.
6. Independently set **Attach to Jira** for files you intend to attach manually. Including a file for AI is not the same as approving its attachment.
7. Review/edit the loaded `problem.txt` text. Edits are used for this investigation; the application does not write them back to `problem.txt`.
8. Select an image row for a local preview. Screenshot pixels are not sent to the model. Transcribe important visible errors into the problem description if needed.

Supported text includes TXT, logs, JSON, XML, CSV, error/stack-trace files, and HTML diagnostic audits. HTML is read as source, not rendered. ZIP archives, PDFs, and Office files are not automatically extracted. Manually extract approved text files from an archive into the incident folder before rescanning; including the ZIP itself does not send its contents.

## 4. Choose the AI provider and model

Choose **OpenAICompatible**, **Claude**, or **GitHubCopilot**. Setup, billing distinctions, and exact credential commands are in [configuration.md](configuration.md).

The model field is editable. Suggested IDs are not an account entitlement list, and models change over time. Enter an ID available to your account/provider; stronger models can improve synthesis but may cost more or consume Copilot usage allowances. The same model family can have different IDs across providers.

Selections are session-local. Switching providers restores the last selected model for that provider; closing the app restores defaults from `appsettings.json`. No automatic provider/model fallback occurs.

## 5. Prepare evidence before sending

1. Click **Prepare evidence**. This performs local extraction and local history matching; it does not call the AI provider.
2. Open **Prepared evidence**.
3. Check incident metadata, provider/model, the **Problem description sent**, and **Historical match information sent**.
4. Select each evidence row and inspect its exact text plus the selected character count/reason.
5. Check truncation, unreadable/unsupported files, exhausted budgets, and missing evidence. A scan result does not guarantee all content was extracted.
6. If needed, change selections/problem text and prepare again. Reduce unrelated files before increasing limits.
7. Only when approved, click **Send reviewed evidence to AI**. This sends selected excerpts, problem context, and matching historical information to the chosen provider. It does not send every original file.

Text is bounded by the configured context budget; diagnostic/error reports are prioritized and remaining space is shared across files. Logs use error-pattern lines with surrounding context. This is a character budget, not an exact token limit. Use **Cancel** to stop work; cancellation cannot retract data already sent to a provider.

## 6. Review results and prepare Jira/Teams

In **Investigation results**, review:

- Incident and Teams summaries.
- Root-cause confidence and the supporting file references.
- Observed errors versus suspected causes; AI assessments still need human verification.
- Recommended next actions, including validation, reconciliation, and duplicate prevention.
- Historical matches, similarity, and suggested Jira reference. A recurrence match is not proof of the same cause.

For Jira:

1. Choose **Create New Jira** or **Comment on Existing Jira** in **Jira action**.
2. Enter or correct the existing issue key where appropriate.
3. Review the generated content. New stories include objective, user story, description, observed errors, scope, and acceptance criteria.
4. Return to **Incident files** to confirm **Attach to Jira** selections. The user-confirmed list is authoritative in copied/saved Jira text.
5. Use **Copy Jira Content**, paste it into Jira, edit as needed, and attach approved files manually.
6. Use **Copy Teams Summary**, review it, and paste into Teams manually.

This version has read-only generated result fields. Edit pasted content in Jira/Teams or the saved text files, or improve the problem description and rerun. Rerunning can incur another provider charge.

## 7. Save outputs

Click **Save investigation outputs** to write into the incident source folder:

| File | Contents |
|---|---|
| `investigation.json` | Structured result, evidence, recommendations, recurrence and fingerprint |
| `investigation.txt` | Human-readable investigation |
| `teams-summary.txt` | Copy-ready Teams summary |
| `jira.txt` | New Jira content, when Create New Jira is selected |
| `jira-comment.txt` | Existing Jira update, when Comment on Existing Jira is selected |
| `attachment-recommendations.txt` | User-confirmed attachment selections and reasons |

Only the selected Jira mode's file is written per save. Comment mode requires an existing Jira key before saving. To save both Jira drafts, select each mode and save once, supplying a real existing key for comment mode. Common output files are overwritten on subsequent saves; preserve a copy first if you need version history. An old file for the other Jira mode can remain from an earlier save. Saving in comment mode also updates that incident's local history Jira reference.

Successful AI investigations are stored in local SQLite history even before you click Save. Default database:

```text
%LOCALAPPDATA%\IntegrationIncidentInvestigator\data\incidents.db
```

The database and generated reports can contain sensitive incident details; the application does not encrypt them. Protect them using Windows access controls and your organization's retention policy. Close the app before backing up the database; preserve any associated WAL/SHM files if they remain rather than copying a live database alone. There is no Jira history synchronization or CSV history import/export in this version.

## 8. Troubleshoot

See the [troubleshooting table](configuration.md#troubleshooting). When reporting a problem, include provider, model ID, application error, and whether preparation or AI submission failed. Never share keys, device codes, credential files, or unapproved incident evidence.

## 9. Optional release folder

Create a framework-dependent Windows release (the destination machine still needs the matching .NET Desktop Runtime):

```powershell
Set-Location C:\Repo\IntegrationIncidentInvestigator
dotnet publish .\src\IntegrationIncidentInvestigator.App\IntegrationIncidentInvestigator.App.csproj -c Release -r win-x64 --self-contained false -o .\artifacts\publish
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
& .\artifacts\publish\IntegrationIncidentInvestigator.App.exe
```

Keep the entire publish folder, including `appsettings.json` and the Copilot native runtime files; do not copy just the executable. Configure credentials on each user's Windows account. Do not distribute keys, incident outputs, or the local history database with the application.
