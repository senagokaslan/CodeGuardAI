# Güvenlik modeli

## Kapsam ve güven sınırları

CodeGuard AI local-first bir geliştirme aracıdır. Güvenilen kullanıcı, API'nin çalıştığı makine, kayıtlı local repository ve yerel PostgreSQL aynı yönetim sınırı içindedir. Gemini ayrı bir dış servis; MCP client ise local child-process başlatan ayrı bir istemcidir.

Bu proje authentication, multi-tenant izolasyon, sandbox veya kötü niyetli repository kodu için güvenli execution ortamı sağlamaz. API yalnız localhost'ta kullanılmalı ve internete açılmamalıdır.

## Tehditler ve kontroller

| Risk | Uygulanan kontrol | Kalan sınır |
|---|---|---|
| Path traversal / mutlak yol | Canonical root çözümü, relative-path zorunluluğu, root containment | İşletim sistemi ACL'leri ayrıca geçerlidir |
| Junction/symlink ile root kaçışı | Reparse-point segment reddi | Repository dışındaki OS değişiklikleri izlenmez |
| Bilinen secret dosyalarının prompt'a girmesi | `.env`, credential/secret adları, sertifika/key uzantıları denylist | Source dosyasına gömülü secret semantik olarak garantiyle bulunmaz |
| Sınırsız repository context | File count, per-file byte ve total byte limitleri | Büyük repository'nin yalnız bir kısmı değerlendirilir |
| Arbitrary command | Runner yalnız `dotnet test` ve root altındaki `.csproj` target üretir; shell/args girdisi yoktur | MSBuild/test kodu çalıştırılır; repository güvenilir olmalıdır |
| Sonsuz agent loop | Sabit state machine ve max-step bütçesi | Provider gecikmesi ayrıca timeout/cancellation'a bağlıdır |
| Duplicate concurrent workflow | PostgreSQL filtered unique index ve typed `409` mapping | Stale run yeni request gelene kadar görünür kalabilir |
| Provider transient failure | Yalnız 429/5xx için 0-2 bounded retry | Kota veya kalıcı provider hatası kullanıcıya failure olur |
| Log/protocol secret sızıntısı | Structured redacted loglar, header redaction, raw exception vermeyen contract | Operatör terminal/debug araçlarını güvenli tutmalıdır |
| MCP'nin tool'u bypass etmesi | İnce adapter yalnız Application portunu çağırır | MCP transport kendi başına authentication sağlamaz |

## Secret yönetimi

- `Database:ConnectionString` ve `Gemini:ApiKey` source control'a yazılmaz.
- Development'ta ASP.NET Core User Secrets; process entegrasyonunda environment variables kullanılır.
- `dotnet user-secrets list` çıktısı issue, log, ekran görüntüsü veya dokümana kopyalanmaz.
- API key `x-goog-api-key` header'ı loglarda redact edilir.
- Secret yanlışlıkla commit edilirse yalnız dosyadan silmek yeterli değildir; değer revoke/rotate edilmeli ve Git geçmişi incelenmelidir.
- MCP host'a parent process'in tüm environment'ı yerine yalnız gereken anahtarların allowlist edilmesi önerilir.

## Filesystem ve tarama politikası

Scanner yalnız `.cs`, `.csproj` ve `appsettings.json`, `appsettings.Development.json`, `global.json` adındaki JSON dosyalarını kabul eder. `.git`, `bin`, `obj`, `node_modules` atlanır. Varsayılan limitler 500 dosya, dosya başına 256 KiB ve toplam 2 MiB'dir.

Project root kayıt anında normalize edilir ve unique tutulur. MCP tool input'u root alamaz; `repositoryId` üzerinden aynı kayıtlı root çözülür. Finding yolları da repository-relative olmak zorundadır.

## Test runner

`SafeDotnetTestRunner` shell açmaz. Executable server configuration'dan gelir ve yalnız `dotnet`/`dotnet.exe` kabul edilir; target root altındaki mevcut bir `.csproj` olmalıdır. Varsayılan timeout iki dakika, output cap 32.768 karakterdir. Cancellation process ağacını sonlandırır; başarı, non-zero test sonucu, timeout ve infrastructure failure ayrı audit/error durumlarıdır.

Bu kontroller test projesinin kendisini sandbox'lamaz. `dotnet test` MSBuild target'ları ve test assembly kodu çalıştırabildiğinden yalnız güvenilen repository hedeflenmelidir.

## LLM veri akışı

Allowlist/denylist ve byte limitlerinden geçen repository context'i Gemini'ye gönderilir. Structured schema çıktının biçimini, grounding validator ise file/line referanslarının taranan context'le uyumunu doğrular. Bunlar model çıktısının her zaman doğru veya güvenli olduğu garantisi değildir; finding'ler kullanıcı incelemesi gerektirir.

Prompt içeriği, repository içeriği ve API key loglanmaz. Loglanan metrikler kimlikler, model/prompt sürümü, süre, karakter sayıları, finding sayısı ve stable error type ile sınırlıdır.

## Error, cancellation ve recovery

- HTTP yüzeyi expected failure'ları stable code içeren `ProblemDetails` olarak döndürür.
- MCP expected failure'ları structured error olarak döndürür; raw exception/stack trace döndürmez.
- Cancellation token controller'dan scanner, provider ve tool'a aktarılır.
- İptal edilmiş review, `Failed/Cancelled` terminal durumuna request token'dan bağımsız kaydedilir.
- Process çökmesinden kalan aktif review otomatik replay edilmez; sonraki request timeout'u aşmış kaydı `Failed/RecoveryTimeout` yapar.

## Operasyon checklist'i

- API yalnız local interface/localhost üzerinde kullanılıyor.
- User Secrets ve environment değerleri repository dışında.
- Review edilecek repository güvenilir ve server-local path doğru.
- MCP client yalnız gereken environment anahtarlarını miras alıyor.
- Ekran görüntülerinde terminal, User Secrets, connection string ve API key yok.
- Database test hedefinin adı `_test` ile bitiyor.
- CI secretsiz ve service container olmadan yeşil.
