# Recurrence detection

A fingerprint hashes normalized system, integration, and error-signature values. Normalization replaces GUIDs, timestamps, labeled request/correlation/transaction/trace IDs, labeled employee/person/worker IDs, and long numeric IDs; it also collapses whitespace and case.

The matcher compares integration names and Jaccard overlap across normalized error-message tokens. A matching fingerprint can increase the score. Candidates above `Investigation:SimilarityThreshold` are ranked and accompanied by reasons. The application labels matches as possible unless a matching fingerprint and high similarity support a confirmed recurrence. This is a conservative heuristic, not semantic proof. The user decides whether to create a new Jira or prepare a comment.

History is local SQLite behind `IIncidentHistoryRepository`. It can later be replaced or supplemented without changing investigation rules.
