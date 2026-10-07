# Workflow safety

Review ve test akışları, modelin serbestçe yeni adım üretmesine izin veren agent loop'ları değildir. Application katmanı sabit ve sınırlı bir akış yürütür; Infrastructure yalnız kalıcı eşzamanlılık garantilerini ve dış provider politikasını sağlar.

## Sabit akış ve adım limitleri

Review akışı şu state machine'i izler:

`Created -> PendingPersisted -> Running -> RepositoryScanned -> ContextBuilt -> AgentCompleted -> Terminal`

Başarısızlık, cancellation veya validation sonrası izin verilen tek kısa yol mevcut non-terminal durumdan `Terminal` durumuna geçmektir. Bir review en fazla 6 state geçişi yapabilir. Geçersiz sıra `InvalidOperationException`, bütçe aşımı ise `WorkflowStepLimitExceededException` üretir. Bütçe aşımı oluşturulmuş run'ı `Failed/StepLimitExceeded` terminal durumunda kalıcılaştırır.

Test suggestion akışı en fazla 7, kontrollü test run akışı en fazla 3 sabit adımdan oluşur. Bu limitler Application katmanında tanımlıdır; model veya MCP input'u adım ekleyemez ve agent başka agent çağıramaz.

## Idempotency ve concurrency

Aynı project için yalnız bir `Pending` veya `Running` review bulunabilir. PostgreSQL'deki filtreli unique index `ux_review_runs_active_project` bu kuralın yarış koşullarında da tek doğruluk kaynağıdır. Yeni istek index yarışını kaybederse deterministik `409 review.duplicate_active` döner.

Aynı review için yalnız bir başarılı test generation sonucu bulunabilir. `ux_ai_model_runs_successful_test_generation` index'i `(review_run_id, purpose)` alanlarını yalnız `TestGeneration/Succeeded` kayıtlarında unique tutar. İki istek ön kontrolden birlikte geçse bile atomik persist yarışını yalnız biri kazanır; diğeri `409 tests.already_generated` alır. Başarısız generation kayıtları yeni bir kontrollü denemeyi engellemez.

## Retry sınırı

Orchestrator provider çağrısını bir kez yapar ve retry döngüsü içermez. Retry yalnız Infrastructure'daki `GeminiHttpClient` politikasındadır: yalnız HTTP 429 ve 5xx transient kabul edilir, retry sayısı 0-2 aralığına clamp edilir ve gecikme bounded'dır. Validation, parse, authentication ve diğer kalıcı hatalar retry edilmez. Böylece workflow adım bütçesi ile transport retry bütçesi birbirine karışmaz.

## Logging ve cancellation

Review orchestration log scope'u `CorrelationId`, `ReviewRunId` ve `ProjectId` alanlarını taşır. Test generation/run scope'u `CorrelationId` ve `ReviewRunId` taşır. Correlation kimliği ayrı bir workflow kimliği üretmek yerine mevcut review run kimliğidir; bu seçim log, audit ve persistence kayıtlarının aynı anahtarla aranmasını sağlar.

Cancellation token scanner, context builder, agent, provider ve tool çağrılarına aktarılır. Başlamış bir review iptal edilirse cancellation tekrar caller'a iletilmeden önce run `Failed/Cancelled` terminal durumunda kalıcılaştırılır. Terminal persist için request token yerine `CancellationToken.None` kullanılır; aksi halde iptal edilmiş token güvenlik açısından gerekli durum kaydını da iptal edebilirdi.

## Yarım kalan run recovery

Process kapanması gibi nedenlerle `Pending` veya `Running` kalan review'lar background loop ile kendiliğinden devam ettirilmez. Yeni review oluşturma isteği geldiğinde, aynı transaction içinde `WorkflowSafety:StaleReviewTimeout` süresinden eski aktif kayıtlar `Failed/RecoveryTimeout` yapılır ve `CompletedAtUtc` atanır; ardından yeni `Pending` kayıt eklenir. Süresi dolmamış aktif kayıt varsa istek conflict alır.

Bu recovery request-triggered'dır: yeni istek gelene kadar stale kayıt veritabanında görülebilir. İş yeniden oynatılmadığı için model/tool side effect'i tekrarlanmaz; kullanıcı yeni bir run ile açıkça devam eder. Varsayılan timeout 15 dakikadır ve startup validation 1 dakika ile 24 saat arasını kabul eder.

## Katman sınırı ve trade-off

C# state machine ve step budget Application'da bulunur; böylece HTTP, MCP ve persistence ayrıntılarına bağımlı olmadan aynı orchestration sözleşmesini korur. EF concurrency index ve unique-violation mapping Infrastructure'dadır; process-local lock kullanmak çok instance'lı ortamda doğru olmayacağı için son karar veritabanına bırakılır. Structured logging orchestration bağlamını görünür kılar fakat business kararını log sistemine taşımaz.
