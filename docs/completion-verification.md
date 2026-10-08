# Tamamlanma kriterleri ve teknik ürün akışı

Bu belge final kabul maddelerini çalışan kod ve doğrulama kanıtlarıyla eşler. Dış provider'a bağlı başarılı Gemini demosu ayrı bir operasyonel ön koşuldur; geçerli key/kota yokken sonuç uydurulmaz.

## Tamamlanma kriterleri

| Kriter | Durum | Kanıt |
|---|---|---|
| Production backend C#/.NET 10 ve ASP.NET Core; Python yok | Sağlandı | `CodeGuardAI.sln`, `global.json`, `src/CodeGuardAI.Api` |
| Docker/container dosyası, komutu veya CI service container yok | Sağlandı | `.github/workflows/ci.yml`, repository dosya taraması |
| Local Windows PostgreSQL kurulumu ve güncel EF migration | Sağlandı | `docs/setup/postgresql-windows.md`, iki migration, fresh database doğrulaması |
| Project, ReviewRun, Finding, TestCase, AIModelRun ve ToolExecution modeli API ile uyumlu | Sağlandı | Domain modelleri, `CodeGuardDbContext`, API response contract'ları |
| Scanner secret/path/reparse/size kurallarını uygular | Sağlandı | `RepositoryScannerSecurityTests`, `RepositoryScannerTests`, root-bound scan endpoint testi |
| Application Gemini yerine `ILLMProvider` portuna bağlı | Sağlandı | `ReviewAgent`, `TestAgent`, DI composition root |
| Normal test suite gerçek Gemini çağrısı/key istemez | Sağlandı | Fake provider/HTTP handler testleri ve secretsiz CI |
| JSON/field/file-line doğrulaması persistence öncesi çalışır | Sağlandı | Parser, schema, grounding ve review integration testleri |
| Test Agent source değiştirmez; runner raw shell kabul etmez | Sağlandı | `TestAgentTests`, `TestRunnerSecurityTests` |
| MCP safe tool'lar üzerinde ince adapter'dır | Sağlandı | `CodeGuardAI.McpHost`, MCP contract/smoke testleri |
| Workflow bounded ve terminaldir; agent loop yoktur | Sağlandı | `WorkflowSafety`, concurrency/recovery integration testleri |
| UI project, scan, review, history, filtre ve test önerisi akışını yürütür | Sağlandı | `wwwroot/index.html`, `app.js`, API integration testleri |
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
12. `POST /api/reviews/{reviewId}/test-runs` veya MCP `run_tests`, yalnız kayıtlı root altındaki allowlist edilmiş `.csproj` için `dotnet test` çalıştırır; raw command/working directory kabul etmez.
13. ToolExecution sonucu bounded output, duration ve redacted özetle audit edilir; review history UI ve API üzerinden yeniden görüntülenir.

Geriye uyumluluk için eski `/projects` ve `/reviews` route'ları korunur; canonical demo/API yolları `/api` prefix'ini kullanır.
