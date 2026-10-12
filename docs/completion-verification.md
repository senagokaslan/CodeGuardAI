# Tamamlanma kriterleri ve teknik ürün akışı

Bu belge final kabul maddelerini çalışan kod ve doğrulama kanıtlarıyla eşler. Dış provider'a bağlı başarılı Gemini demosu ayrı bir operasyonel ön koşuldur; geçerli key/kota yokken sonuç uydurulmaz.

## Tamamlanma kriterleri

| Kriter | Durum | Kanıt |
|---|---|---|
| Production backend C#/.NET 10 ve ASP.NET Core; Python yok | Sağlandı | `CodeGuardAI.sln`, `global.json`, `src/CodeGuardAI.Api` |
| Docker/container dosyası, komutu veya CI service container yok | Sağlandı | `.github/workflows/ci.yml`, repository dosya taraması |
| Local Windows PostgreSQL kurulumu ve güncel EF migration | Kod/prosedür sağlandı; mevcut DB pending | `docs/setup/postgresql-windows.md`; `20261008193806_AddDocumentationReport` migration dosyası mevcut fakat 12 Ekim 2026 listesindeki development DB'ye uygulanmamış |
| Project, ReviewRun, Finding, TestCase, DocumentationReport, AIModelRun ve ToolExecution modeli API ile uyumlu | Sağlandı | Domain modelleri, `CodeGuardDbContext`, API response contract'ları |
| Scanner secret/path/reparse/size kurallarını uygular | Sağlandı | `RepositoryScannerSecurityTests`, `RepositoryScannerTests`, root-bound scan endpoint testi |
| Application Gemini yerine `ILLMProvider` portuna bağlı | Sağlandı | `ReviewAgent`, `TestAgent`, `DocsAgent`, DI composition root |
| Normal test suite gerçek Gemini çağrısı/key istemez | Sağlandı | Fake provider/HTTP handler testleri ve secretsiz CI |
| JSON/field/file-line doğrulaması persistence öncesi çalışır | Sağlandı | Parser, schema, grounding ve review integration testleri |
| Test Agent source değiştirmez; runner raw shell kabul etmez | Sağlandı | `TestAgentTests`, `TestRunnerSecurityTests` |
| MCP safe tool'lar üzerinde ince adapter'dır | Sağlandı | `CodeGuardAI.McpHost`, MCP contract/smoke testleri |
| Workflow bounded ve terminaldir; agent loop yoktur | Sağlandı | `WorkflowSafety`, concurrency/recovery integration testleri |
| UI project, scan, review, history, filtre ve test önerisi akışını yürütür | Sağlandı | `wwwroot/index.html`, `app.js`, API integration testleri |
| Docs Agent completed review'dan tek, kalıcı Markdown raporu üretir | Sağlandı | Docs application/workflow/provider katmanları, documentation API ve offline flow/concurrency testleri |
| CI restore/build/test çalıştırır; container ve Gemini secret istemez | Sağlandı | GitHub Actions `CI` workflow'u |
| README, diagrams, setup, security, eval ve limitations güncel | Sağlandı | README dokümantasyon haritası |

## Final teknik demo flow

1. `dotnet run --project src/CodeGuardAI.Api --launch-profile http` ASP.NET Core host'u açar; static UI aynı origin'den servis edilir.
2. `POST /api/projects` server-local path'i normalize edip doğrular ve Project kaydını PostgreSQL'e yazar.
3. `POST /api/projects/{projectId}/scan` request'ten root kabul etmez; kayıtlı Project root'unda bounded scanner'ı çalıştırıp included/skipped manifest ve nedenlerini döndürür.
4. `POST /api/reviews` ReviewRun oluşturur; orchestrator sabit state machine ve context budget uygular.
5. Review Agent versioned prompt/schema ile `ILLMProvider` portunu çağırır.
6. `GeminiProvider` typed `HttpClient` üzerinden Gemini REST API'ye gider; güvenli metadata AIModelRun'a yazılır.
7. Yanıt strict deserialize, required field, enum/range ve file/line grounding kontrollerinden geçer.
8. Geçerli Finding kayıtları transaction ile saklanır ve ReviewRun terminal `Completed` olur; hata/cancellation terminal `Failed` olur.
9. UI findings'i severity/category ile filtreler; file/line/reason/suggestion/confidence alanlarını gösterir. `GET /api/projects/{projectId}/reviews` eski run'ları yeniden açar.
10. `POST /api/reviews/{reviewId}/tests`, yalnız açık `Generate tests` aksiyonuyla completed review ve seçilmiş grounded finding context'ini Test Agent'a verir.
11. TestCase önerileri saklanır; production/source dosyaları değiştirilmez.
12. `POST /api/reviews/{reviewId}/documentation`, completed ReviewRun, persisted Finding ve varsa TestCase kayıtlarını Docs Agent'a verir; repository root/source yeniden yüklenmez.
13. Gemini documentation cevabı strict schema ile parse edilir; unknown/duplicate ID reddedilir ve canonical severity/category/file/line/reason/suggestion/test target alanları deterministik Markdown'a render edilir.
14. Başarılı `DocumentationReport` ile `AIModelRun(Purpose=Documentation)` aynı transaction'da PostgreSQL'e yazılır; ikinci/eşzamanlı üretim `409` olur. `GET /api/reviews/{reviewId}/documentation` persisted Markdown'ı döndürür.
15. `POST /api/reviews/{reviewId}/test-runs` veya MCP `run_tests`, yalnız kayıtlı root altındaki allowlist edilmiş `.csproj` için `dotnet test` çalıştırır; raw command/working directory kabul etmez.
16. ToolExecution sonucu bounded output, duration ve redacted özetle audit edilir; review history UI ve API üzerinden yeniden görüntülenir.

Geriye uyumluluk için eski `/projects` ve `/reviews` route'ları korunur; canonical demo/API yolları `/api` prefix'ini kullanır.

## Bilinen doğrulama ayrımı

- Normal Release suite tamamen offline çalışır; fake provider ve fake HTTP handler kullanır, Gemini key istemez.
- `Category=Database` suite yalnız adı `_test` ile biten açıkça sağlanmış PostgreSQL bağlantısında çalışır. Bağlantı yoksa skip edilir ve “PostgreSQL doğrulandı” olarak yorumlanmaz.
- Docs Agent'ın canlı Gemini davranışı otomatik kapanış suite'inin parçası değildir. Swagger ile manuel adımlar `docs/demo-script.md` içinde yer alır.
- Static UI mevcut project/scan/review/history/filter/test önerisi demo akışını yürütür; Docs ilk sürümde Swagger/HTTP API üzerinden kullanılır.

## 12 Ekim 2026 Release kapanış doğrulaması

| Kontrol | Sonuç |
|---|---|
| `dotnet restore CodeGuardAI.sln` | Başarılı; tüm projeler güncel |
| Release build | Başarılı; 0 uyarı, 0 hata |
| Unit suite (`Category!=Database`) | 263 başarılı, 0 başarısız, 0 atlanan |
| API/integration suite (`Category!=Database`) | 43 başarılı, 0 başarısız, 0 atlanan |
| Database suite (`Category=Database`) | 0 başarılı, 0 başarısız, 5 atlanan; güvenli `_test` bağlantı değişkeni tanımlı değil |
| Docs odaklı regresyon | 71 unit + 13 integration başarılı; 4 database testi aynı nedenle atlandı |
| Review/Test Agent regresyonu | 21 unit + 3 integration başarılı; 0 başarısız |
| EF migration listesi | `InitialCreate`, `WorkflowSafetyConcurrency`, `AddDocumentationReport`; sonuncusu development DB için pending |
| Development OpenAPI | Review POST, Test POST, Documentation POST ve Documentation GET path'leri mevcut |
| Swagger UI / health | `/swagger/index.html` ve `/health` HTTP 200 |
| Secret scan | Tracked dosyalarda yüksek olasılıklı Gemini key veya hassas dosya adı bulunmadı |
| Gerçek Gemini | Çalıştırılmadı; bütün otomatik doğrulama offline fake provider/handler kullandı |

Release adayı kod/test açısından yeşildir. Canlı Docs Agent demosundan önce mevcut local development veritabanına pending migration uygulanmalı ve Swagger'daki manuel Gemini adımları geçerli key/kota ile yürütülmelidir.
