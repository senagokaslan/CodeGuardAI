# Mimari

Bu belge, production project reference'ları, EF mapping'leri, workflow'lar ve MCP adapter'larıyla karşılaştırılmış güncel mimariyi gösterir. Karar gerekçeleri [ADR-001](adr/ADR-001-modular-monolith.md), [ADR-003](adr/ADR-003-mcp-host.md) ve Docs Agent için [ADR-004](adr/ADR-004-docs-agent.md) içinde tutulur.

## Katmanlar ve bağımlılık yönü

```mermaid
flowchart TB
    Browser[Frameworksüz browser UI] --> Api[CodeGuardAI.Api]
    McpClient[MCP client] -->|stdio| McpHost[CodeGuardAI.McpHost]

    Api --> Application[CodeGuardAI.Application]
    Api --> Infrastructure[CodeGuardAI.Infrastructure]
    McpHost --> Application
    McpHost --> Infrastructure
    Infrastructure --> Application
    Infrastructure --> Domain[CodeGuardAI.Domain]
    Application --> Domain

    Infrastructure --> PostgreSQL[(PostgreSQL)]
    Infrastructure --> Repository[Local repository]
    Infrastructure --> Dotnet[dotnet test process]
    Infrastructure --> Gemini[Gemini HTTPS API]

    UnitTests[Unit tests] -.-> Application
    UnitTests -.-> Domain
    UnitTests -.-> Infrastructure
    IntegrationTests[Integration tests] -.-> Api
```

- Domain dış sisteme veya üst katmana referans vermez.
- Application use case, port, state machine ve typed result'ları taşır; Infrastructure veya MCP SDK'yı bilmez.
- Infrastructure EF Core, Npgsql, filesystem, process ve Gemini adapter'larını uygular.
- API HTTP contract ve composition root'tur. MCP Host ayrı executable ve yalnız protokol adapter'ıdır.
- `ModelContextProtocol` NuGet bağımlılığı yalnız MCP Host projesindedir.

## ER diyagramı

```mermaid
erDiagram
    PROJECT ||--o{ REVIEW_RUN : owns
    REVIEW_RUN ||--o{ FINDING : produces
    REVIEW_RUN ||--o{ TEST_CASE : suggests
    REVIEW_RUN ||--o{ AI_MODEL_RUN : records
    REVIEW_RUN o|--o{ TOOL_EXECUTION : correlates
    REVIEW_RUN ||--o| DOCUMENTATION_REPORT : documents
    AI_MODEL_RUN ||--o| DOCUMENTATION_REPORT : generated

    PROJECT {
        uuid id PK
        string name
        string repository_path
        string normalized_root_path UK
        timestamptz created_at_utc
        timestamptz updated_at_utc
    }
    REVIEW_RUN {
        uuid id PK
        uuid project_id FK
        string status
        string model_name
        string prompt_version
        timestamptz started_at_utc
        timestamptz completed_at_utc
        string error_code
        jsonb scan_summary_json
    }
    FINDING {
        uuid id PK
        uuid review_run_id FK
        string severity
        string category
        string file_path
        int start_line
        int end_line
        decimal confidence
    }
    TEST_CASE {
        uuid id PK
        uuid review_run_id FK
        string type
        string name
        string target
        text suggested_test_code
    }
    AI_MODEL_RUN {
        uuid id PK
        uuid review_run_id FK
        string purpose
        string provider
        string model_name
        int duration_ms
        int input_chars
        int output_chars
        string status
    }
    TOOL_EXECUTION {
        uuid id PK
        uuid review_run_id FK
        string tool_name
        string status
        int duration_ms
        string input_summary
        string output_summary
    }
    DOCUMENTATION_REPORT {
        uuid id PK
        uuid review_run_id FK,UK
        uuid model_run_id FK
        string title
        text markdown_content
        timestamptz created_at_utc
    }
```

Önemli kalıcı garantiler:

- `projects.normalized_root_path` unique'tir.
- Aynı project için `Pending`/`Running` review'larda filtreli unique index vardır.
- Aynı review için yalnız bir başarılı `TestGeneration` model run'ı olabilir.
- `documentation_reports.review_run_id` unique'tir; aynı review için yalnız bir başarılı Markdown raporu olabilir.
- Documentation report hem `ReviewRun` hem onu üreten `AIModelRun(Purpose=Documentation)` kaydına bağlıdır.
- Finding confidence/line aralıkları ile metrik süreleri check constraint'lerle korunur.
- Project veya ReviewRun silinmesi ilişkili kayıtları cascade eder.

## Review ve AI akışı

```mermaid
sequenceDiagram
    actor User
    participant API as ReviewsController
    participant WF as ReviewOrchestrator
    participant DB as EF workflow store
    participant Scan as RepositoryScanner
    participant Context as RepositoryContextBuilder
    participant Agent as ReviewAgent
    participant LLM as GeminiProvider

    User->>API: POST /api/reviews
    API->>WF: CreateAsync(command, cancellationToken)
    WF->>DB: TryCreatePendingAsync
    Note over DB: duplicate/stale-run policy transaction içinde
    WF->>DB: Running durumunu kaydet
    WF->>Scan: bounded scan
    Scan-->>WF: manifest + skipped reasons
    WF->>Context: bounded prompt context
    WF->>Agent: ReviewAsync
    Agent->>LLM: structured request
    Note over LLM: yalnız 429/5xx için bounded retry
    LLM-->>Agent: structured JSON
    Agent-->>WF: parsed + grounded findings
    WF->>DB: terminal run + findings + model metrics
    WF-->>API: typed success/error
    API-->>User: ReviewResponse veya ProblemDetails
```

Review adımları sabittir; model yeni workflow adımı üretemez ve agent başka agent çağırmaz. Cancellation scanner/provider/tool zincirine taşınır; başlamış review iptalinde terminal `Failed/Cancelled` kaydı request token'dan bağımsız persist edilir. Test önerisi aynı review için ayrı kullanıcı aksiyonudur ve repository'yi değiştirmez.

## Review → Test → Docs akışı

Üç agent birbirini çağırmaz. Review zorunlu başlangıçtır; Test ve Docs ayrı, explicit HTTP istekleridir. Test önerisi yoksa Docs yine rapor üretebilir. Docs Agent yalnız completed review'ın PostgreSQL'de saklanan finding'lerini ve varsa test önerilerini okur.

```mermaid
flowchart LR
    User[Explicit user action] -->|POST documentation| Api[Documentation API]
    Api --> DocsWorkflow[IDocsOrchestrator]
    DocsWorkflow --> Store[IDocsWorkflowStore]
    Store --> Review[(Completed ReviewRun)]
    Store --> Findings[(Grounded Findings)]
    Store --> Tests[(Optional TestCases)]
    DocsWorkflow --> DocsAgent[IDocsAgent]
    DocsAgent --> Provider[ILLMProvider]
    Provider --> Structured[Structured JSON]
    Structured --> Grounding[Finding/Test ID grounding]
    Grounding --> Renderer[Deterministic Markdown renderer]
    Renderer --> Report[(PostgreSQL documentation report)]
```

Çalışan contract:

- `POST /api/reviews/{reviewId}/documentation` bir completed review için tek rapor üretir.
- `GET /api/reviews/{reviewId}/documentation` persisted Markdown raporunu görüntüler.
- İstemci repository root, source content, output path veya raw prompt göndermez.
- Markdown repository'ye yazılmaz; yalnız PostgreSQL ve API response içinde tutulur.
- Provider'ın serbest metni doğrudan saklanmaz. Structured alanlar persisted finding/test ID'lerine ground edilir; severity, category, file/line, reason, suggestion ve test hedefleri canonical kayıtlardan render edilir.
- Bir review için tek başarılı rapor PostgreSQL unique constraint ile korunur; failed deneme retry'ı engellemez.
- Docs workflow bounded ve cancellation-aware olur; Review/Test workflow'larının durumunu değiştirmez.

Akış `LoadSource → ValidateCompletedReview → CheckIdempotency → RunAgent → RenderMarkdown → PersistTerminal` adımlarıyla sınırlıdır. Başarılı `DocumentationReport` ve başarılı `AIModelRun` aynı transaction'da yazılır. Provider/parser/grounding failure yalnız failed model run oluşturur; kısmi rapor oluşturmaz. PostgreSQL unique constraint eşzamanlı iki isteğin son kararını verir ve kaybeden istek `409 documentation.already_generated` alır.

```mermaid
sequenceDiagram
    actor User
    participant API as DocumentationController
    participant WF as DocsOrchestrator
    participant DB as EfDocsWorkflowStore
    participant Agent as DocsAgent
    participant LLM as GeminiProvider

    User->>API: POST /api/reviews/{reviewId}/documentation
    API->>WF: CreateAsync(command, cancellationToken)
    WF->>DB: completed ReviewRun + Findings + optional TestCases
    WF->>Agent: persisted canonical input
    Agent->>LLM: docs-v1 + strict JSON schema
    LLM-->>Agent: IDs + bounded prose fields
    Agent-->>WF: parsed and ID-grounded report model
    WF->>WF: deterministic Markdown render
    WF->>DB: AIModelRun + DocumentationReport transaction
    WF-->>API: DocumentationReadModel
    API-->>User: 201 + Location
    User->>API: GET /api/reviews/{reviewId}/documentation
    API->>DB: persisted report
    API-->>User: 200 + Markdown content
```

## MCP tool zinciri

```mermaid
flowchart LR
    Client[MCP client] -->|stdio JSON-RPC| Host[CodeGuardAI.McpHost]
    Host --> ReadAdapter[read_file adapter]
    Host --> TestAdapter[run_tests adapter]
    ReadAdapter --> Projects[IProjectQueries]
    TestAdapter --> Projects
    ReadAdapter --> ReadPort[IFileReadTool]
    TestAdapter --> TestPort[ITestRunnerTool]
    ReadPort --> Envelope[Authorization + audit envelope]
    TestPort --> Envelope
    Envelope --> Resolver[SafePathResolver]
    Resolver --> Files[Registered repository root]
    TestPort --> Runner[SafeDotnetTestRunner]
    Runner --> Process[dotnet test allowlisted .csproj]
    Envelope --> Audit[(tool_executions)]
```

MCP input'u root path, shell command, working directory, executable, timeout veya output cap belirleyemez. Root server-side Project kaydından gelir; limitler shared options'tan gelir. Testlerin non-zero sonucu `tests_failed`, tool altyapı/policy hatası `tool_error` olarak ayrılır. Beklenmeyen exception ve stack trace protokole çıkmaz; cancellation yeniden fırlatılır.

## Public contract ve failure davranışı

- API validation/typed application error'larını `ProblemDetails` ve stable error code'a map eder.
- Review terminal durumları `Completed` veya `Failed` olarak persist edilir.
- PostgreSQL concurrency constraint yarışın son kararını verir; process-local lock kullanılmaz.
- Gemini transport retry'sı orchestrator adım bütçesinden ayrıdır.
- Log scope `CorrelationId`, `ReviewRunId` ve `ProjectId` taşır; prompt, secret ve repository içeriği taşımaz.
- CI dış servis kullanmadan Application/API sözleşmesini doğrular; gerçek PostgreSQL davranışı local-only testtir.
- Docs API invalid request'i `400`, eksik review/raporu `404`, incomplete veya duplicate isteği `409`, beklenmeyen üretim hatasını redacted `500` olarak map eder.
