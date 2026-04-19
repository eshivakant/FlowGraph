# Roslyn Ingestion Guide (FlowGraph)

## 1. Purpose

This document defines how agents must use Roslyn to ingest .NET repositories and extract a deterministic execution graph representing:

* RPC calls (synchronous execution)
* Messaging flows (pub/sub, Solace, queues, topics)
* Dependency injection relationships
* REST API entry points
* Cross-service interactions

The output of this process is used to build a Neo4j graph.

**Important principle:**
This ingestion process must be fully deterministic and NOT rely on LLMs.

---

## 2. Core Principle

Roslyn is used as a **static analysis engine**, not a heuristic tool.

Agents MUST:

* Use semantic model (`SemanticModel`) wherever possible
* Resolve symbols (`ISymbol`, `IMethodSymbol`, `INamedTypeSymbol`)
* Avoid string-only parsing except for fallback patterns

---

## 3. Input Assumptions

Agents operate on:

* One Git repository at a time
* A specific commit range (incremental mode preferred)
* C# solution (.sln) loaded via MSBuildWorkspace

---

## 4. Incremental Ingestion Strategy

### 4.1 Required Flow

1. Load last indexed commit from SQLite
2. Compute git diff (lastCommit → HEAD)
3. Extract changed files only
4. Re-analyze only impacted syntax trees
5. Update graph incrementally

### 4.2 Full Reindex Fallback

Trigger full scan if:

* No previous commit exists
* Commit is missing (force push / rewrite)
* Corrupted state detected

---

## 5. What MUST Be Extracted

## 5.1 RPC (Synchronous Calls)

Detect method invocations:

### Rules:

* Use `InvocationExpressionSyntax`
* Resolve via `SemanticModel.GetSymbolInfo()`

### Extract:

* Caller method
* Callee method
* Containing types

### Output Edge:

```
CALLS
```

---

## 5.2 Dependency Injection Graph

Detect constructor injection:

### Rules:

* Identify constructors (`IMethodSymbol.MethodKind == Constructor`)
* Extract parameters

### Output Edge:

```
DEPENDS_ON
```

---

## 5.3 REST API Entry Points

Detect ASP.NET controllers:

### Rules:

* Classes inheriting ControllerBase
* Attributes:

  * [HttpGet]
  * [HttpPost]
  * [HttpPut]
  * [HttpDelete]

### Extract:

* HTTP method
* Route
* Entry method

### Output Edge:

```
TRIGGERS
```

(from endpoint → controller method)

---

## 5.4 Messaging (CRITICAL)

### 5.4.1 Publishing Detection

Detect patterns:

* Publish<T>()
* Send()
* Emit()
* Produce()
* Bus.Publish()

### Extract:

* Message type (generic T if available)
* Topic (string literal or constant)
* Source method

### Output Edges:

```
PUBLISHES
USES_TOPIC
```

---

### 5.4.2 Consumption Detection

Detect patterns:

* Subscribe<T>()
* Consume<T>()
* Handle<T>()
* Message handlers

### Extract:

* Message type
* Handler method
* Service owning handler

### Output Edges:

```
CONSUMES
HANDLES
```

---

## 6. Topic Resolution Rules

### Priority order:

1. String literal in method call
2. Constant field
3. Config-bound constant (best-effort resolution)
4. Unknown → store as `UNRESOLVED_TOPIC`

---

## 7. Message Type Resolution Rules

### Preferred:

* Generic type argument (Publish<OrderCreated>)

### Fallback:

* Method parameter type
* Payload DTO type

---

## 8. Symbol Resolution Rules

Agents MUST:

* Always resolve `ISymbol` via SemanticModel
* Prefer `IMethodSymbol` over syntax string matching
* Use `ToDisplayString()` only for graph identifiers

---

## 9. Graph Entity Model

## Nodes

* Service
* Method
* Class
* Message
* Topic
* Controller
* Repository

## Edges

* CALLS
* PUBLISHES
* CONSUMES
* HANDLES
* DEPENDS_ON
* USES_TOPIC
* TRIGGERS

---

## 10. Output Rules

Each extracted relationship MUST be normalized into:

```
(source, relation, target)
```

Example:

```
OrderService.PublishOrderCreated -> PUBLISHES -> OrderCreated
```

---

## 11. File-Level Processing Rules

For each changed file:

1. Load syntax tree
2. Get semantic model
3. Identify all:

   * classes
   * methods
   * invocations
   * attributes
4. Extract relationships
5. Emit graph updates

---

## 12. Performance Rules

Agents MUST:

* Avoid full solution traversal unless required
* Cache semantic models per compilation
* Process only changed syntax trees in incremental mode

---

## 13. Non-Goals (IMPORTANT)

Agents MUST NOT:

* Infer business meaning
* Use LLMs for extraction
* Guess missing symbols
* Perform runtime execution analysis

---

## 14. Optional Enhancements (NOT REQUIRED)

If available, agents MAY:

* Tag services by namespace
* Group messages by domain
* Classify flows as RPC vs EVENT vs HYBRID (heuristic only)

---

## 15. Success Criteria

Ingestion is successful if:

* Every message publish has a corresponding graph edge
* Every consumer is linked to message type
* RPC call chains are traversable end-to-end
* Reindex only processes changed files in incremental mode

---

## 16. Summary

Roslyn ingestion is a deterministic extraction pipeline that transforms C# code into a structured execution graph representing:

* Calls
* Events
* Messages
* Dependencies

This graph is the foundation of FlowGraph’s system intelligence layer.
