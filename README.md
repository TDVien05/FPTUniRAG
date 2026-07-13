<div align="center">

# 🎓 FPT UniRAG

### Academic AI Assistant for Course Materials

<p>
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 10" />
  <img src="https://img.shields.io/badge/ASP.NET%20Core-Razor%20Pages-512BD4?logo=dotnet&logoColor=white" alt="ASP.NET Core Razor Pages" />
  <img src="https://img.shields.io/badge/PostgreSQL-17-4169E1?logo=postgresql&logoColor=white" alt="PostgreSQL 17" />
  <img src="https://img.shields.io/badge/Docker-Ready-2496ED?logo=docker&logoColor=white" alt="Docker" />
  <img src="https://img.shields.io/badge/SignalR-Realtime-512BD4?logo=signalr&logoColor=white" alt="SignalR" />
  <img src="https://img.shields.io/badge/Stripe-Sandbox-635BFF?logo=stripe&logoColor=white" alt="Stripe Sandbox" />
</p>

<p>
  <strong>Chat with course materials · Manage subjects · Process academic documents</strong>
</p>

</div>

<div align="center">

<a href="#-highlights">Highlights</a> ·
<a href="#-architecture">Architecture</a> ·
<a href="#first-time-setup">Setup</a> ·
<a href="#-important-routes">Routes</a>

</div>

FPT UniRAG is an academic AI assistant for FPT University. Students chat with course material, teachers upload and process documents, and administrators manage subjects, accounts, subscription plans, quotas, and embedding configuration.

The application is an ASP.NET Core Razor Pages solution backed by PostgreSQL, OpenRouter, Stripe, and SignalR.

## ✨ Highlights

- 🤖 RAG-powered student chat with course citations
- 📚 Teacher document upload, extraction, chunking, and embedding workflow
- 👨‍💼 Admin management for accounts, subjects, plans, quotas, and analytics
- ⚡ Realtime subject, plan, quota, and chat updates with SignalR
- 💳 Stripe Sandbox subscription checkout
- 🔐 Role-based access for students, teachers, and administrators

## 🧰 Tech stack

| Layer | Technology |
|---|---|
| Web application | ASP.NET Core 10 Razor Pages |
| Business logic | .NET class library |
| Data access | Entity Framework Core 10 + Npgsql |
| Database | PostgreSQL 17 |
| AI providers | OpenRouter embeddings and chat completion |
| Realtime | ASP.NET Core SignalR |
| Payments | Stripe Sandbox |
| Infrastructure | Docker Compose |
| OCR | Tesseract OCR (optional) |

## 🏗️ Architecture

```mermaid
flowchart LR
    Student[🎓 Student Browser] --> Web[ASP.NET Core Razor Pages]
    Teacher[👨‍🏫 Teacher Browser] --> Web
    Admin[🛠️ Admin Browser] --> Web

    Web <--> Realtime[SignalR Hubs]
    Web --> Business[Business Layer]
    Business --> Data[Data Access Layer]
    Data --> PostgreSQL[(PostgreSQL)]
    Business --> OpenRouter[OpenRouter API]
    Business --> Stripe[Stripe Sandbox]
```

## Project context

### Main users

- **Student**: selects subjects, chats with course content, views subscription plans, and purchases a plan through Stripe Sandbox.
- **Teacher**: works with subjects assigned as header teacher, uploads documents, and monitors document processing.
- **Admin**: manages users, subjects, teacher assignments, subscription plans, free student quota, embedding configuration, and analytics.

### Main capabilities

- Custom cookie authentication with `admin`, `teacher`, and `student` roles.
- RAG chat pipeline:
  1. Student chooses a subject.
  2. Relevant chunks are retrieved from embedding records stored in PostgreSQL.
  3. OpenRouter generates the answer with course citations.
  4. SignalR streams chat events to the browser.
- Teacher document workflow for upload, extraction, chunking, embedding, and background processing.
- Admin CRUD for subjects and subscription plans.
- Stripe Sandbox product/price synchronization and checkout.
- Database-backed monthly free-token quota for students without an active paid plan.
- SignalR realtime updates for:
  - teacher subject assignments and subject changes;
  - subscription plan create, update, activate/deactivate, and delete;
  - free student quota changes;
  - student chat streaming.

## Repository structure

```text
FPTUniRAG/
├── FPTUniRAG/                  # ASP.NET Core app, Razor Pages, hubs, services
├── FPTUniRAG.BusinessLayer/    # Account, subject, and business contracts/services
├── FPTUniRAG.DataAccessLayer/  # EF Core DbContext, entities, SQL scripts
├── create_database.sql         # PostgreSQL bootstrap schema
├── docker-compose.yml          # PostgreSQL container
└── FPTUniRAG.slnx              # Solution file
```

## Requirements

- Windows, macOS, or Linux
- .NET SDK 10.0
- Docker Desktop and Docker Compose
- Git
- Optional for document OCR: Tesseract OCR
- API credentials for OpenRouter and Stripe Sandbox

## First-time setup

### 1. Clone and enter the repository

```bash
git clone <repository-url>
cd FPTUniRAG
```

### 2. Start PostgreSQL

```bash
docker compose up -d
docker compose ps
```

The default services are:

| Service | Address |
|---|---|
| PostgreSQL | `localhost:54329` |
The PostgreSQL container mounts `create_database.sql` on first initialization. The script is executed only when the PostgreSQL volume is created for the first time.

### 3. Create local appsettings

`FPTUniRAG/appsettings.json` is intentionally ignored by Git because it contains local credentials. Create it from the project configuration used by your team, then set at least:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=54329;Database=prn222;Username=postgres;Password=postgres"
  },
  "AdminCredentials": {
    "Email": "admin@fpt.edu.vn",
    "Password": "Admin@123",
    "DisplayName": "Admin User"
  },
  "StudentCredentials": {
    "Email": "student@fpt.edu.vn",
    "Password": "Student@123",
    "DisplayName": "Default Student",
    "StudentCode": "STU000001"
  },
  "RagIngestion": {
    "OpenRouter": {
      "BaseUrl": "https://openrouter.ai/api/v1",
      "ApiKey": "<openrouter-api-key>",
      "EmbeddingModel": "google/gemini-embedding-001",
      "ChatModel": "qwen/qwen3.6-flash",
      "EmbeddingDimensions": 1536,
      "MaxCompletionTokens": 800,
      "Temperature": 0.2
    }
  },
  "Stripe": {
    "BaseUrl": "https://api.stripe.com/v1",
    "SecretKey": "<stripe-test-secret-key>",
    "Currency": "vnd",
    "PublicBaseUrl": "https://localhost:7268",
    "SuccessPath": "/payments/stripe/return",
    "CancelPath": "/StudentPlans"
  }
}
```

Configure SMTP if the application needs to email teacher/student credentials:

```json
"Smtp": {
  "Host": "smtp.gmail.com",
  "Port": 587,
  "Username": "<smtp-username>",
  "Password": "<smtp-password>",
  "FromEmail": "<from-email>",
  "FromName": "FPT UniRAG",
  "EnableSsl": true,
  "TimeoutMilliseconds": 10000,
  "Security": "StartTls"
}
```

Do not commit real OpenRouter, Stripe, SMTP, or database credentials.

### 4. Apply the database schema

`create_database.sql` is the single source of truth for the current PostgreSQL schema. The Docker bootstrap runs it automatically when the PostgreSQL volume is created for the first time.

For an existing local database, apply the consolidated schema manually:

```bash
docker exec -i fptunirag-postgres psql -U postgres -d prn222 < create_database.sql
```

The schema includes subject chunking settings, embedding settings, document embedding runs, processing progress, Stripe subscription IDs, and the student free-quota setting.

### 5. Restore, build, and run

```bash
dotnet restore FPTUniRAG.slnx
dotnet build FPTUniRAG.slnx
dotnet run --project FPTUniRAG/FPTUniRAG.csproj --launch-profile https
```

Configured development URLs:

- HTTPS: `https://localhost:7268`
- HTTP: `http://localhost:5056`

The HTTPS development certificate may require trusting the .NET certificate:

```bash
dotnet dev-certs https --trust
```

## Default accounts

On startup, the hosted initialization service creates or updates these local development accounts:

| Role | Email | Password |
|---|---|---|
| Admin | `admin@fpt.edu.vn` | `Admin@123` |
| Student | `student@fpt.edu.vn` | `Student@123` |

Change these values in local `appsettings.json` before sharing or deploying the application.

## Important routes

| Route | Access | Purpose |
|---|---|---|
| `/` | Public | Login page |
| `/AdminDashboard` | Admin | Admin dashboard |
| `/Accounts` | Admin | Manage student and teacher accounts |
| `/Subjects` | Admin | Subject CRUD and assignments |
| `/SubscriptionPlans` | Admin | Manage paid plans |
| `/FreeQuotaSettings` | Admin | Configure free monthly student tokens |
| `/EmbeddingSettings` | Admin | Select active embedding model |
| `/TeacherHome` | Teacher | Header subject dashboard |
| `/TeacherUpload` | Teacher | Upload course documents |
| `/StudentDashboard` | Student | AI chat workspace |
| `/StudentPlans` | Student | View and purchase plans |

## Configuration notes

- The free student token limit defaults to `2,000` tokens per month if the quota table is unavailable or has no valid value.
- Paid students use the monthly token limit stored on their active subscription plan.
- `StorageRoot` defaults to `App_Data/teacher-uploads` for private teacher uploads.
- Supported upload types are `.docx`, `.txt`, `.md`, and `.pdf`.
- Tesseract OCR is controlled by `RagIngestion:Tesseract`.
- `appsettings.Development.json` overrides values from `appsettings.json` when the environment is `Development`.

## Troubleshooting

### Startup fails while creating the default admin/student

Check that PostgreSQL is running and that the connection string points to port `54329`:

```bash
docker compose ps
docker exec -it fptunirag-postgres psql -U postgres -d prn222 -c "select 1;"
```

If the error mentions a missing column or table, apply the pending SQL scripts in date order.

### PostgreSQL schema changes do not appear

Docker init scripts run only for a new volume. Apply the required script manually, or recreate the local database volume when it is safe to discard local data.

### Student does not see realtime changes

Confirm that:

1. The student page is connected to `/hubs/subscription-plans`.
2. The browser is authenticated as a student.
3. The admin operation completed successfully.
4. The application process was rebuilt/restarted after code changes.

### Stripe checkout fails

Verify `Stripe:SecretKey` is a Stripe Sandbox key beginning with `sk_test_`, and that `Stripe:PublicBaseUrl` matches the HTTPS development URL.

### OpenRouter requests fail

Verify `RagIngestion:OpenRouter:ApiKey`, model names, embedding dimensions, and network access.

## Development commands

```bash
# Build
dotnet build FPTUniRAG.slnx

# Run the web app
dotnet run --project FPTUniRAG/FPTUniRAG.csproj --launch-profile https

# Start infrastructure
docker compose up -d

# Stop infrastructure
docker compose down

# View infrastructure status
docker compose ps

# View PostgreSQL logs
docker logs fptunirag-postgres
```

## Security notes

- Never commit local `appsettings.json` or secret keys.
- Use environment variables, user secrets, or a production secret manager for deployment.
- The default passwords are for local development only.
- Do not expose the PostgreSQL port publicly without authentication and network controls.
