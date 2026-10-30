# Architecture

## Projects and dependency direction

`App` (WPF/MVVM) calls use cases in `Application`. `Application` depends on `Core` and owns ports for file scanning/extraction, history, LLM requests, and artifact output. `Infrastructure` implements those ports and depends on `Application`/`Core`. UI code does not own investigation decisions.

## Investigation stages

1. Scan the selected local directory and classify metadata.
2. Let the user review inclusion and attachment flags and edit the problem description.
3. Extract a bounded evidence set; prioritize diagnostic/error reports, share the remaining text budget across included files, stream logs and retain relevant error context.
4. Identify error lines and build a normalized fingerprint.
5. Query local history and produce scored, explainable candidates.
6. Build a versioned prompt/request, call the selected provider, and validate the structured response.
7. Present results for user review; only save local artifacts or copy text on user action.

Progress is reported by the running pipeline. Cancellation is passed to file I/O, history I/O, and provider calls.

## Storage and external boundaries

SQLite is hidden behind `IIncidentHistoryRepository`. Provider calls are hidden behind `ILlmClient`. No Jira, Teams, or file-upload integrations are included. The only configured network path is an explicit AI investigation request.
