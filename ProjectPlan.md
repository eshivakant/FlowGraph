# FlowGraph MVP Implementation Plan

## 1. Overview

FlowGraph is a distributed system intelligence engine that indexes multiple repositories and builds a unified execution graph across RPC calls, messaging systems (e.g., Solace), and service interactions.

It enables developers and architects to understand:

* Message publishing and consumption flows
* RPC call chains
* Cross-service dependencies
* Impact of changes in real time

---

## 2. Core Objective

Build a system that:

* Indexes multiple Git repositories
* Extracts RPC + messaging + dependency flows using Roslyn
* Builds a unified graph of system interactions
* Supports incremental indexing via git diff
* Supports manual and webhook-based reindexing
* Uses SQLite for metadata only
* Uses file-based storage for artifacts
* Uses Neo4j for relationship graph
* Supports optional AI layer via OpenAI-compatible endpoint

---

## 3. High-Level Architecture

```
Git Repositories
      ↓
Index Orchestrator (.NET Worker/API)
      ↓
Git Diff Engine (incremental)
      ↓
Roslyn Analyzer (C#)
      ↓
-----------------------------
|                           |
|                           |
Neo4j Graph DB        File Blob Storage
(Relationships)       (diffs, snapshots)
      ↓
SQLite Metadata DB (state + jobs)
      ↓
API Layer (Search / Impact / Trace)
      ↓
Optional AI Layer (OpenAI-compatible)
```

---

## 4. Design Principles

### 4.1 Separation of Concerns

| Concern       | Storage     |
| ------------- | ----------- |
| Relationships | Neo4j       |
| State / Jobs  | SQLite      |
| Artifacts     | File System |

---

### 4.2 Incremental First

All indexing must be incremental:

```
lastIndexedCommit → git diff → changed files → Roslyn scan → graph update
```

Full scan only when necessary.

---

### 4.3 Deterministic Core

* Graph extraction is deterministic
* AI is optional and never source of truth

---

## 5. System Components

## 5.1 Index Orchestrator

Responsibilities:

* Handle manual reindex requests
* Handle webhook triggers
* Compute git diff ranges
* Coordinate indexing pipeline
* Update SQLite + Neo4j

Interfaces:

* IRepoIndexer
* IIncrementalDiffService
* IIndexJobManager
* IRepoStateStore

---

## 5.2 Git Diff Engine

Input:

* repo + branch

Output:

* list of changed files since last indexed commit

Rules:

* No last commit → full scan
* Missing commit → full scan fallback

---

## 5.3 Roslyn Analyzer (C# MVP)

Extracts:

### RPC Patterns

* HTTP calls
* service-to-service calls
* gRPC calls

### Messaging Patterns

* Publish / Send / Emit
* Subscribe / Consume / Handle
* Solace topics
* message types

### Relationships

* CALLS
* PUBLISHES
* CONSUMES
* HANDLES
* DEPENDS_ON
* USES_TOPIC

---

## 5.4 Graph Writer (Neo4j)

### Nodes

* Service
* Method
* Message
* Topic
* Repo
* Handler

### Edges

* CALLS
* PUBLISHES
* CONSUMES
* HANDLES
* DEPENDS_ON
* USES_TOPIC

---

## 5.5 SQLite Metadata Store

Used only for operational state.

### repos

```
repo_name TEXT PRIMARY KEY
last_indexed_commit TEXT
last_indexed_at TEXT
status TEXT
```

### indexing_jobs

```
id INTEGER PRIMARY KEY
repo_name TEXT
status TEXT
started_at TEXT
completed_at TEXT
changed_files_count INTEGER
```

### config

```
key TEXT PRIMARY KEY
value TEXT
```

---

## 5.6 File Blob Storage (Abstracted)

### Interface

```
IBlobStore
- WriteAsync
- ReadAsync
- ExistsAsync
- DeleteAsync
```

### Implementation (MVP)

* FileSystemBlobStore

Structure:

```
/storage/
  repos/
  diffs/
  snapshots/
  logs/
```

---

## 5.7 API Layer

### Endpoints

#### Reindex repo

```
POST /repos/{repo}/reindex
```

Options:

* incremental (default)
* full

---

#### Search graph

```
GET /graph/search?query=OrderCreated
```

---

#### Impact analysis

```
GET /graph/impact?change=OrderCreated
```

---

#### Trace flow

```
GET /graph/trace?start=CheckoutController
```

---

## 6. Reindex Workflow

### Manual Trigger Flow

```
1. Receive request
2. Load repo state (SQLite)
3. Fetch latest commit
4. Compute git diff
5. If missing → full scan
6. Run Roslyn on changed files
7. Update Neo4j graph
8. Update SQLite state
```

### Webhook Flow

Same as manual but triggered from Git provider events.

---

## 7. Flow Types (UI Concept Layer)

These are visualization modes only:

* RPC Flow
* Event Flow
* Hybrid Flow
* Data Lineage
* Business Workflow
* Fan-out / Fan-in
* Retry / DLQ Flow
* Background Job Flow

---

## 8. Optional AI Layer

### Interface

```
IAiProvider
```

### Implementations

* DisabledAiProvider (default)
* OpenAICompatibleProvider
* AzureOpenAIProvider
* OllamaProvider

### Use Cases

* Natural language queries
* Flow summarization
* Impact explanation

AI MUST NOT compute graph truth.

---

## 9. Failure Handling

* Roslyn failure → skip file
* Neo4j failure → retry queue
* SQLite missing commit → full reindex
* Git diff failure → fallback full scan

---

## 10. MVP Scope

### Must Have

* Repo indexing
* Incremental git diff
* Roslyn extraction (RPC + messaging)
* Neo4j graph writes
* SQLite state tracking
* File blob storage abstraction

### Nice to Have

* Search API
* Basic UI
* Webhook trigger

### Not in MVP

* Historical time travel
* Full AI dependency
* Advanced visualization engine

---

## 11. Success Criteria

MVP is successful if:

* Query message → shows publishers + consumers
* Reindex is incremental and fast
* Can answer:

```
What breaks if OrderCreated changes?
```

in under 2 seconds via graph lookup.

---

## 12. Future Extensions (Not in MVP)

* Multi-language support (Java, TS, Python)
* Runtime Solace ingestion
* CI/CD integration
* PR risk scoring
* Architecture drift detection
* Ownership graph

---

## 13. Summary

FlowGraph is a deterministic system for understanding distributed systems through code + messaging + RPC analysis, with optional AI enhancement.
