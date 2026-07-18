# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

FPT UniRAG is an academic AI assistant for FPT University built on ASP.NET Core 10 Razor Pages, backed by PostgreSQL, OpenRouter (chat + embeddings), Stripe Sandbox, and SignalR. Students chat with course material via RAG, teachers upload/process documents, and admins manage subjects, accounts, plans, quotas, and embedding config.

## Commands

```bash
# Build the whole solution
dotnet build FPTUniRAG.slnx

# Run the web app natively (requires Docker Postgres running, see below)
dotnet run --project FPTUniRAG/FPTUniRAG.csproj --launch-profile https

# Run all tests
dotnet test FPTUniRAG.Tests/FPTUniRAG.Tests.csproj

# Run a single test
dotnet test FPTUniRAG.Tests/FPTUniRAG.Tests.csproj --filter "FullyQualifiedName~StudentChatProgressTests"

# Full Docker stack (app + Postgres), rebuilds images
docker compose up --build

# Background start / stop / status / logs
docker compose up -d
docker compose down
docker compose ps
docker compose logs -f
```

There is no separate lint command; rely on `dotnet build` warnings (`Nullable` is enabled in all three projects).

### Database

`create_database.sql` is the single source of truth for the PostgreSQL schema. Docker only runs it automatically when the Postgres volume is first created. To apply schema changes to an existing local DB:

```powershell
Get-Content create_database.sql -Raw | docker exec -i fptunirag-postgres psql -U postgres -d prn222
```

Default local services: web app at `http://localhost:5056`, Postgres at `localhost:54329` (container `fptunirag-postgres`, db `prn222`).

Default seeded accounts (local dev only, overridable via `.env`): `admin@fpt.edu.vn` / `Admin@123`, `student@fpt.edu.vn` / `Student@123`.

## Architecture

Three-project layered solution with a strict dependency direction:

```
FPTUniRAG (Presentation: Razor Pages, minimal API endpoints, SignalR hubs, Program.cs)
    -> FPTUniRAG.BusinessLayer (services, RAG pipeline, payments, notifications)
        -> FPTUniRAG.DataAccessLayer (AppDbContext, entities, repositories)
```

- **Presentation (`FPTUniRAG/`)**: Razor Pages under `Pages/` (role-authorized via policies configured in `Program.cs`, e.g. `AdminOnly`, `TeacherOrAdmin`, `StudentOrAdmin`), minimal API groups under `Endpoints/` (`AdminAccountApiEndpoints`, `TeacherSubjectApiEndpoints`, `StudentChatApiEndpoints`, `MomoPaymentEndpoints`), and SignalR hubs under `Hubs/`. All DI registration, middleware, and page authorization conventions live in `Program.cs` — check there first when tracing how a route or service is wired up.
- **Business layer (`FPTUniRAG.BusinessLayer/`)**: organized by domain — `Accounts/` (auth, cookie events, email, seeding, import), `AdminDashboard/`, `Subjects/` (+ `Realtime/` SignalR notifiers), `Subscriptions/` (+ `Realtime/`), `Payments/Stripe/` and `Payments/Momo/`, and `Rag/` (the core RAG pipeline, see below). `Common/OperationResult.cs` is a shared result type used across services.
- **Data access layer (`FPTUniRAG.DataAccessLayer/`)**: `Context/AppDbContext.cs`, POCO `Entities/`, and repositories under `Repositories/<Domain>/` following an `I<Name>Repository` / `<Name>Repository` interface pattern, injected as `Scoped` services in `Program.cs`.

### RAG pipeline (`FPTUniRAG.BusinessLayer/Rag/`)

- `Ingestion/`: `TeacherDocumentWorkflowService` orchestrates upload -> `DocumentTextExtractor` (with optional `TesseractOcrService` OCR) -> chunking -> embedding. Processing is asynchronous: documents are queued via `IDocumentProcessingQueue` (singleton, in-memory channel) and consumed by the `DocumentProcessingBackgroundService` hosted service, which also recovers pending jobs on startup.
- `Chunking/`: `IFixedChunkingService` and `ISemanticChunkingService` implement the two chunking strategies selectable per subject.
- `Embeddings/`: `OpenRouterEmbeddingService` calls OpenRouter for vectors; `PostgresChunkEmbeddingStore` persists/queries them in Postgres; `EmbeddingConfigurationService`/`EmbeddingBenchmarkService` manage/evaluate embedding model choice (admin-facing).
- `Chat/`: student-facing chat flow — `StudentChunkRetrievalService` retrieves relevant chunks for a subject, `OpenRouterChatCompletionService` calls OpenRouter chat completion, `StudentChatService` coordinates the session/response and streams results (consumed via `StudentChatApiEndpoints` and `StudentChatHub`).

Uploaded teacher files are stored under `App_Data/teacher-uploads/<subject>/` (`StorageRoot` config); supported types are `.docx`, `.txt`, `.md`, `.pdf`.

### Auth and access control

Custom cookie authentication (`AccountCookieAuthenticationEvents`) with three roles: `admin`, `teacher`, `student`. Page-level authorization is wired via `AuthorizePage`/`AuthorizeFolder` conventions in `Program.cs`, not `[Authorize]` attributes. A global middleware in `Program.cs` forces any account with the `MustChangePassword` claim to `/ChangePassword` before reaching other protected pages/APIs.

### Realtime (SignalR)

Three hubs, each paired with a notifier interface in the business layer: `TeacherHeaderSubjectHub` (`ITeacherHeaderSubjectNotifier`) for teacher/subject assignment changes, `SubscriptionPlanHub` (`ISubscriptionPlanNotifier`) for plan/quota changes, and `StudentChatHub` for chat events.

### Payments

Stripe Sandbox is the active integration (`Payments/Stripe/`, `IStripePaymentService`), wired into DI and used for subscription checkout. `Payments/Momo/` exists alongside it with a full service and endpoint file (`MomoPaymentEndpoints.cs`) but is **not registered in `Program.cs`** (no DI registration, hub, or endpoint mapping call) — treat it as in-progress/inactive unless you're the one wiring it up.

### Quotas

Students without an active paid plan get a database-backed monthly free-token quota (`StudentFreeQuotaSetting`, managed via `IFreeTokenQuotaService`, admin-configurable at `/FreeQuotaSettings`), defaulting to 2,000 tokens/month if unset. Paid students use the token limit on their active `SubscriptionPlan`.

## Tests

`FPTUniRAG.Tests` uses xUnit. Tests reference both `FPTUniRAG.BusinessLayer` and `FPTUniRAG.DataAccessLayer` directly (no dedicated test-only abstractions layer). Test files are organized by domain mirroring the business layer, e.g. `Accounts/AccountManagementServiceTests.cs`, `Rag/Chat/StudentChatProgressTests.cs`.

## Configuration

- `appsettings.Development.json` overrides `appsettings.json` when `ASPNETCORE_ENVIRONMENT=Development`.
- Secrets/config flow through `.env` (see `.env.example`) into Docker Compose; never commit a real `.env`, `appsettings.json` with secrets, or the `.keys/` Data Protection key directory.
- Key config sections bound via `IOptions<T>`: `Smtp` -> `SmtpOptions`, `RagIngestion` -> `RagIngestionOptions` (OpenRouter API key/models/base URL, Tesseract toggle), `Stripe` -> `StripeOptions`.
