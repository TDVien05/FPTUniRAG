# Epics — FPT UniRAG

> The epic MAP. A thin index, not a context object. Each epic lists its goal, the
> requirements it covers, its ordered stories, and cross-epic dependencies. Story
> detail will live in individual `{epic}.{story}.{slug}.story.md` files once each
> epic is sharded (not yet done — this pass produced the map only, per user choice).
>
> Track: **Quick Flow, adapted for brownfield** — no `prd.md` exists yet. Requirements
> below are cited to `project-documentation.md` (fresh codebase scan, 2026-07-20) and
> `GAP_BUSINESS.md` (local business/functional gap analysis, spot-checked against
> current `main` — see that document's staleness notes) in lieu of a formal PRD.
> Sources: bmad-output/project-documentation.md, GAP_BUSINESS.md

---

## Epic 1: Real Chat Streaming & Scalable Retrieval

**Goal:** Make student chat answers arrive as genuine token-by-token streams and keep
retrieval latency flat as a subject's document library grows, instead of both currently
degrading invisibly as usage scales.

**In scope (cited):**
- Real streaming — `StudentChatService.StreamMessageAsync` awaits the full OpenRouter
  completion before replaying it to the browser in fixed 48-char chunks with an
  artificial 40ms delay; `OpenRouterChatCompletionService` already implements real
  delta/SSE streaming but its output is discarded by the caller.
  [Source: GAP_BUSINESS.md#2.1 "Streaming" is simulated, not real]
- Retrieval scalability — `StudentChunkRetrievalService.RetrieveRelevantChunksAsync`
  loads every chunk+embedding for a subject into app memory and scores cosine
  similarity in a C# loop; no pgvector-native ANN/distance-operator query, no caching,
  no upper bound on subject size.
  [Source: GAP_BUSINESS.md#2.2 Retrieval is brute-force, in-process]
- Citation score integrity — `TryResolveCitationSimilarityAsync` re-derives a citation's
  similarity score by re-scanning stored citation JSON rather than persisting the score
  directly; silently returns 0 on no match instead of surfacing "unknown."
  [Source: GAP_BUSINESS.md#2.3 Citation similarity score requires "self-lookup"]

**Architecture touchpoints:** `FPTUniRAG.BusinessLayer/Rag/Chat/` (`StudentChatService`,
`StudentChunkRetrievalService`, `OpenRouterChatCompletionService`),
`FPTUniRAG.BusinessLayer/Rag/Embeddings/PostgresChunkEmbeddingStore`, `StudentChatHub`.
[Source: project-documentation.md#3 Entry Points and Key Flows, #6 Integration Points]

**Out of scope:** Payment reliability, account lifecycle, and subscription plan
correctness gaps — identified during discovery but explicitly deferred by the user for
a later pass (see Notes).

**Stories (ordered):**

| ID | Slug | Intent | Status |
|------|------|--------|--------|
| 1.1 | real-token-streaming | Wire `OpenRouterChatCompletionService`'s existing delta callback through `StudentChatService`/`StudentChatHub` to the browser; remove the post-hoc chunk-and-delay replay | done |
| 1.2 | pgvector-ann-retrieval | Replace the in-process cosine-similarity loop with a pgvector-native SQL query (`real[]::vector` cast + `<=>` cosine distance) in `ChunkVectorRepository` | done |
| 1.3 | citation-score-source-of-truth | Thread `messageId` through citation-detail lookup so the score comes from an exact, single-message read instead of an ambiguous session-wide scan; nullable score signals "unknown" instead of silent 0 | done |

**Cross-epic dependencies:**
- Blocked by: none
- Blocks: none (independent of Epic 2's ingestion-side changes)

---

## Epic 2: Ingestion Robustness & Test Coverage

**Goal:** Let document processing scale past one-at-a-time and recover from transient
failures on its own, and close the test-coverage gaps in the domains most likely to
regress silently (payments, subjects/subscriptions, ingestion).

**In scope (cited):**
- Concurrency — `DocumentProcessingBackgroundService` drains one shared
  `IDocumentProcessingQueue` strictly sequentially for the entire application; one slow
  document (e.g. large OCR PDF) blocks every other subject's uploads behind it.
  [Source: GAP_BUSINESS.md#4.1 Single-threaded background processing]
- Resilience — `TeacherDocumentWorkflowService.ProcessDocumentAsync` has no automatic
  retry/backoff; transient failures (OpenRouter timeout, network blip) require a
  teacher to notice and manually click retry.
  [Source: GAP_BUSINESS.md#4.2 No automatic retry policy]
- Versioning — `UploadAsync` rejects an upload outright if the target chapter already
  has a document; replacing one requires deleting it (and its chunks/embeddings) first.
  [Source: GAP_BUSINESS.md#4.3 Chapter model is 1:1 with a document]
- Test coverage — no automated tests exist for Stripe payment flows, subject/subscription
  management, or RAG ingestion/chunking/embedding; only Accounts, AdminDashboard,
  Rag/Chat, and TeacherDocuments have coverage today (7 files total).
  [Source: project-documentation.md#2 Test Framework(s); GAP_BUSINESS.md#7 Test coverage
  (count corrected — see project-documentation.md#7 Planning Notes)]

**Architecture touchpoints:** `FPTUniRAG.BusinessLayer/Rag/Ingestion/`
(`DocumentProcessingBackgroundService`, `IDocumentProcessingQueue`,
`TeacherDocumentWorkflowService`), `FPTUniRAG.Tests/`.
[Source: project-documentation.md#3 Background Workers/Queues, #4 Layer Breakdown]

**Out of scope:** Payments and Accounts test coverage beyond what's needed to pin down
current behavior — a full account-lifecycle epic (edit/delete, password reset) was
deferred by the user, so its tests are deferred with it.

**Stories (ordered):**

| ID | Slug | Intent | Status |
|------|------|--------|--------|
| 2.1 | parallel-document-processing | Add bounded concurrency to `IDocumentProcessingQueue` consumption so multiple documents process at once instead of one global sequential loop | backlog |
| 2.2 | ingestion-retry-policy | Add automatic retry/backoff for transient failures in `ProcessDocumentAsync` before falling back to the manual "failed + teacher retries" path | backlog |
| 2.3 | document-replace-in-place | Allow a chapter's existing document to be replaced/versioned without a destructive delete-first step | backlog |
| 2.4 | ingestion-and-payment-test-coverage | Add service-level xUnit coverage for Stripe payment flow and RAG ingestion/chunking/embedding | backlog |

**Cross-epic dependencies:**
- Blocked by: none
- Blocks: none
- **Internal note:** 2.1 (parallel processing) and 2.3 (replace-in-place) both touch
  `TeacherDocumentWorkflowService` — when these are sharded into story context objects,
  declare non-overlapping Owned File/Module Scope or sequence them rather than running
  both in parallel dev sessions.

---

## Epic 3 (Future — not sharded yet): Quiz / Self-Test Feature

**Goal:** Decide the fate of `TestQuestion` and, if pursued, build a minimal
auto-generated practice-question feature on top of already-ingested chapters.

**In scope (cited):**
- `TestQuestion` and its relationships from `Chapter`/`BenchmarkResult` exist in the EF
  model and `create_database.sql`, and are now also referenced from
  `SubjectManagementModels.cs`/`SubjectManagementService.cs`/`DocumentRepository.cs`/
  `SubjectRepository.cs` — but no repository, service, endpoint, or Razor page builds a
  quiz/self-test feature on top of it.
  [Source: GAP_BUSINESS.md#6 Schema/feature drift; project-documentation.md#7 Planning
  Notes (references have grown since the gap doc was written — worth re-checking scope
  before this epic is sharded)]

**Architecture touchpoints:** `FPTUniRAG.DataAccessLayer/Entities/TestQuestion.cs`,
`FPTUniRAG.BusinessLayer/Subjects/`. [Source: project-documentation.md#4 Layer Breakdown]

**Out of scope:** Full quiz UI/grading/analytics — this epic starts at "decide and scope
a minimal version," not a complete feature build.

**Stories (ordered):**

_Not sharded yet — user chose to keep this a placeholder future epic rather than compile
stories in this pass. Recommended first step when picked up: re-scan current
`TestQuestion` references (they've grown since the original gap analysis) to confirm
whether this is genuinely still unbuilt before committing to build vs. remove._

**Cross-epic dependencies:**
- Blocked by: none
- Blocks: none

---

## Deferred (identified, not epic'd this pass)

Two improvement clusters surfaced during discovery but were explicitly **not** selected
for this round — captured here so a future `bmad-epics-and-stories` run doesn't have to
rediscover them:

- **Payments & Money** — no Stripe webhook (subscription activation depends solely on
  browser return, confirmed still true against current `Program.cs`), no admin
  refund/cancel action, declared plan feature flags (`HasAdvancedModels`,
  `HasPrioritySupport`, `HasHistoryExport`) not actually settable from
  `CreateAsync`/`UpdateAsync`, no downgrade/proration concept.
  [Source: GAP_BUSINESS.md#1, #5]
- **Accounts & Access** — no self-service password reset despite the DB columns
  existing, no admin edit/delete account, no teacher un-assignment from a subject,
  teacher-creation email can be sent before the DB write is confirmed.
  [Source: GAP_BUSINESS.md#3]

---

## Delivery Tracking (count-based)

No story points, velocity, or burndown. Track by COUNT only:

- Total stories (sharded epics only): 7
- Done: 3
- Remaining: 4
- Completion rate: 43%
- Placeholder (Epic 3, not yet sharded): 1 epic, story count TBD

## Notes

- This epic map substitutes for a formal `prd.md`/`architecture.md` pair — it was built
  directly from `project-documentation.md` (fresh scan) and `GAP_BUSINESS.md` (gap
  analysis, spot-checked). If this project later runs `bmad-tech-spec` or `bmad-prd`,
  reconcile against this map rather than starting over.
- Per the BMAD workflow contract, this is the epic MAP only — no story context objects
  have been compiled yet, and none are `ready-for-dev`. Confirm this map (add, drop, or
  reorder epics/stories) before running a story-compilation pass.
- Recommended sequencing once compiled: Epic 1 (chat/retrieval) and Epic 2 (ingestion/
  tests) can run in parallel — they touch disjoint modules. Epic 3 should wait until its
  scope question (build vs. remove `TestQuestion`) is resolved.
