# CodeGuard AI - Sözlük

**Amaç:** Ürün, API, domain ve dokümantasyonda kullanılan canonical terimleri sabitlemek. Kod oluşturulduğunda type ve contract adları İngilizce canonical terimleri izler; Türkçe açıklamalar anlamı değiştirmez.

## 1. Canonical ürün terimleri

| Terim | Tanım | Kullanım notu / karıştırılmaması gereken |
|---|---|---|
| **CodeGuard AI** | Yerel repository'yi güvenli kurallarla tarayıp grounded code review ve test senaryosu önerileri üreten local-first developer tool. | Chatbot, otomatik düzeltici veya production agent değildir. |
| **Local-first** | Ana uygulama, repository erişimi, veritabanı ve normal doğrulama akışının geliştiricinin yerel makinesinde olması. | “Hiçbir veri dışarı çıkmaz” demek değildir; gerçek LLM çağrısında yalnız seçilmiş bounded context sağlayıcıya gider. |
| **Project** | Adı, kullanıcı tarafından verilen repository yolu ve normalize edilmiş root'u taşıyan kayıt. | Git hosting projesi veya .NET project'i ile karıştırılmamalı. “Ürün projesi” bağlamı açık yazılır. |
| **Repository** | İncelenecek, Project tarafından işaret edilen yerel kaynak kod dizini. | İlk sürümde remote GitHub repository/PR anlamına gelmez. |
| **Normalized Root** | Repository yolunun filesystem karşılaştırması için canonical hale getirilmiş kök biçimi. | Finding içinde saklanmaz; Finding path'i relative'dir. |
| **Scan** | Repository'deki aday dosyaları policy ile keşfetme ve sınıflandırma işlemi. | LLM analizi değildir; dosya içeriğine ilişkin güvenlik ve limit kararı verir. |
| **Scan Manifest** (`ScanManifest`) | Scan sonunda included ve skipped dosyaları, ilgili metadata ve skip reason'ları taşıyan typed sonuç. | Ham directory listing değildir; sonraki erişimler için allowlist kaynağıdır. |
| **Skip Reason** | Bir dosyanın neden context dışında kaldığını açıklayan canonical neden. | Sessizce atlama yerine gözlemlenebilir sonuçtur; örnek: secret policy, reparse point, extension veya size limit. |
| **Repository Context** (`RepositoryContext`) | Scan manifestinden deterministik bütçeyle seçilen, relative path ve 1-based satır metadata'sı taşıyan LLM girdisi. | Tüm repository değildir; raw filesystem erişimi sağlamaz. |
| **Context Budget** | Maksimum dosya sayısı, dosya başı byte ve toplam karakter gibi merkezi sınırlar. | Token/maliyet tahminiyle ilişkili olsa da deterministic enforcement karakter/byte temelli olabilir. |
| **Review** | Scan/context sonrasında kod hakkında yapılandırılmış bulgu üretme use case'i. | UI eylemidir; kalıcı çalışma kaydı `ReviewRun`'dır. Scan ile eş anlamlı değildir. |
| **Review Run** (`ReviewRun`) | Tek bir review workflow yürütmesinin durumu, zamanı, model/prompt metadata'sı, sonucu ve typed hata bilgisini taşıyan kalıcı kayıt. | “Review” eyleminden farklı olarak kimlikli ve terminal durumu olan instance'tır. |
| **Finding** | Doğrulanmış bir review bulgusu: severity, category, relative file, gerçek satır aralığı, title, reason, suggestion ve confidence taşır. | LLM'in ham adayı değildir; grounding tamamlanmadan Finding sayılmaz. |
| **LLM Finding Candidate** (`LLMFinding`) | Provider cevabından parse edilen ancak henüz domain doğrulaması ve grounding'i geçmemiş güvenilmeyen aday. | Persistence'a doğrudan yazılmaz. |
| **Grounding** | Adayın relative file path'inin manifestte ve 1-based satır aralığının gerçek dosyada olduğunu doğrulama. | Model confidence skoru grounding yerine geçmez. |
| **Severity** | Finding'in öngörülen etki/öncelik düzeyi. | Category değildir; izinli enum seti sonraki contract gününde tek yerde tanımlanır. |
| **Category** | Finding'in problem sınıfı: bug, security, maintainability, performance, reliability veya code quality. | Severity ile karıştırılmaz; exact enum yazımı tek contract'ta sabitlenir. |
| **Confidence** | LLM'in adaya ilişkin 0 ile 1 arasındaki güven değeri. | Doğruluk kanıtı, severity veya grounding değildir. |
| **Test Suggestion** | Completed review ve grounded finding'lerden türetilen test fikri. | Test run değildir ve dosyaya otomatik yazılmaz. Kalıcı karşılığı `TestCase`'dir. |
| **Test Case** (`TestCase`) | Adı, tipi, hedefi, senaryosu, gerekçesi ve opsiyonel öneri kodunu taşıyan kalıcı test önerisi. | Çalıştırılmış test sonucu ya da production source değişikliği değildir. |
| **Test Run** | Kullanıcının test önerisinden ayrı ve açık aksiyonuyla başlatılan, allowlisted hedefteki sınırlandırılmış `dotnet test` çalıştırması. | LLM'in kendiliğinden yapabildiği eylem değildir; raw shell kabul etmez. |
| **AI Model Run** (`AIModelRun`) | Tek bir provider çağrısının purpose, provider/model, prompt version, duration, boyut, durum ve redacted error metadata kaydı. | Raw prompt, source, model response veya secret saklamaz. |
| **Tool Execution** (`ToolExecution`) | Güvenli bir tool çağrısının adı, durumu, süresi, redacted input/output özeti ve hata türünü taşıyan audit kaydı. | Log event'i ile aynı şey değildir; raw command veya secret içermez. |
| **History** | Önceki ReviewRun ve bağlı doğrulanmış sonuçların yeniden görüntülenebilmesi. | Git history değildir. |

## 2. Mimari ve entegrasyon terimleri

| Terim | Tanım | Sınır |
|---|---|---|
| **Modüler Monolit** | Tek deployment/runtime sınırı içinde sorumlulukları modüller ve proje referanslarıyla ayıran mimari. | Microservice veya katmansız monolit değildir. |
| **Domain** | Entity, enum, value object ve iş invariant'larını taşıyan en iç katman. | Framework, database, HTTP, filesystem veya LLM ayrıntısını bilmez. |
| **Application** | Use case, port/interface, DTO ve bounded workflow politikalarını taşıyan katman. | Domain'e bağımlıdır; Infrastructure implementasyonlarını bilmez. |
| **Infrastructure** | PostgreSQL/EF Core, Gemini HTTP, filesystem, process ve MCP gibi dış ayrıntıları Application portlarına uyarlayan katman. | İş akışının sahibi değildir. |
| **API** | HTTP contract, boundary validation, status/Problem Details ve composition root katmanı. | Controller içinde domain/workflow kuralı barındırmaz. |
| **Dependency Direction** | `API -> Application -> Domain` ve `Infrastructure -> Application + Domain` referans yönü. API composition root somut implementasyonları birleştirir. | Infrastructure'ın Domain'e bağımlılığı yalnız domain type'larını uygulamak içindir; Domain dışa bağımlı olmaz. |
| **Port** | Application'ın ihtiyaç duyduğu, dış ayrıntıdan bağımsız interface sözleşmesi; örnek `ILLMProvider`. | Somut HTTP/database/process implementasyonu değildir. |
| **Adapter** | Bir portu belirli dış teknolojiyle uygulayan Infrastructure bileşeni; örnek `GeminiProvider`. | Üst seviye policy tanımlamaz. |
| **Composition Root** | API başlangıcında port ve adapter'ların DI container'a bağlandığı tek wiring noktası. | Service locator veya business logic alanı değildir. |
| **`ILLMProvider`** | Application'ın typed LLM isteği/cevabı için kullandığı provider-independent port. | Gemini ayrıntısını Application'a sızdırmaz. |
| **`GeminiProvider`** | `ILLMProvider` portunun Gemini REST/HTTP ayrıntılarını kapsayan Infrastructure adapter'ı. | Core workflow veya grounding sahibi değildir. |
| **Review Agent** | Versioned prompt, input/output contract ve validation davranışı olan Application servisi. | Bağımsız süreç, serbest otonom agent, OS kullanıcısı veya shell değildir. |
| **Test Agent** | Completed review'dan grounded TestCase önerisi üreten Application servisi. | Production code yazmaz; test run'ı kendiliğinden başlatmaz. |
| **Orchestrator / Workflow** | Scan, context, provider, validation ve persistence adımlarını sınırlı sırayla yürüten Application servisi. | Agentların birbirini çağırdığı recursive/serbest ağ değildir. |
| **Tool** | Typed input/output, authorization, limit ve hata sözleşmesi olan güvenli uygulama fonksiyonu. | Arbitrary shell veya model yetkisi değildir. |
| **MCP Adapter** | Çalışan safe tool'ları Model Context Protocol ile dış istemcilere sunan ince Infrastructure adapter'ı. | Sandbox, authorization veya tool implementasyonu değildir. |
| **Structured Output** | Provider'dan belirlenmiş JSON şekline uyması istenen typed çıktı yaklaşımı. | Geçerli JSON tek başına domain doğruluğu değildir. |
| **Fail-closed** | Girdi/çıktı doğrulanamadığında veriyi kabul etmek yerine typed hata/rejection üretme. | Sessiz düzeltme veya kısmi veriyi başarılı sayma değildir. |
| **Typed Error** | Validation, not-found, conflict, provider, timeout, cancellation gibi sınıfı makinece ayırt edilebilen hata. | Kullanıcıya stack trace, secret veya raw payload döndürmez. |
| **Problem Details** | API hata response'larının tutarlı HTTP problem biçimi. | Domain error'ın kendisi değildir; API'deki güvenli dış temsilidir. |
| **Cancellation** | Kullanıcı/host isteği durdurduğunda token'ın I/O ve workflow boyunca iletilmesi ve operasyonun güvenli terminal sonuçla kapanması. | Timeout ile aynı değildir ve retry edilmez. |
| **Timeout** | Bir dış çağrı veya process'in tanımlı sürede bitmemesi üzerine uygulanan sınır. | Kullanıcı cancellation'ından ayrı typed hata nedenidir. |
| **Bounded** | Adım, dosya, byte, karakter, finding, retry, süre veya output sayısının açık üst sınıra sahip olması. | “Makul büyüklük” gibi ölçüsüz bir ifade değildir. |
| **Redaction** | Secret veya hassas raw içeriğin log/audit alanından çıkarılması ya da güvenli özetle değiştirilmesi. | Şifreleme ile eş anlamlı değildir. |

## 3. Doğrulama terimleri

| Terim | Tanım | Başarı kanıtı |
|---|---|---|
| **Unit Test** | I/O olmadan domain invariant, mapper, budget, grounding ve workflow kararını doğrulayan hızlı test. | Deterministik ve dış servis bağımsızdır. |
| **Integration Test** | Test host, DI, middleware ve fake LLM ile gerçek HTTP pipeline davranışını doğrulayan test. | API contract ve wiring birlikte çalışır. |
| **Database Integration Test** | Gerçek local PostgreSQL üzerinde mapping, migration, FK/index ve query davranışını doğrulayan opt-in test. | `Category=Database`; normal offline suite'in parçası değildir. |
| **Provider Contract Test** | Fake HTTP handler ile request, timeout, retry ve JSON parse davranışını doğrulayan test. | Gerçek Gemini çağrısı yapmaz. |
| **Security Test** | Traversal, reparse, secret, prompt/command injection, oversized input, timeout ve output cap gibi ana riskin reddedildiğini kanıtlayan test. | Yalnız happy path testi değildir. |
| **Eval** | Bilinen kusurlu ve temiz küçük fixture'larla LLM davranışını semantik ölçütlere göre değerlendirme. | Exact prose yerine expected category/file/valid-line/nonempty-suggestion ve false-positive kontrolü kullanır. |
| **FakeLLMProvider** | Önceden belirlenmiş typed cevap döndüren deterministik test double. | Mock çağrı zinciri veya gerçek provider değildir. |
| **Manual Smoke Test** | Gerçek Gemini ile küçük fixture üzerinde, açık opt-in ve kota bilinciyle yapılan kontrol. | Normal `dotnet test` veya offline demo için zorunlu değildir. |
| **Definition of Done (DoD)** | 28 günlük MVP'nin tamamlanmış sayılması için birlikte sağlanması gereken ürün, güvenlik, test, CI ve dokümantasyon kriterleri. | Günlük acceptance criteria'nın yerine geçmez; hepsinin üst seviye kapanış sözleşmesidir. |
| **Acceptance Criteria** | Belirli gün veya özelliğin tamamlanma kapısı. | Sağlanmadan sonraki günün kapsamına geçilmez. |

## 4. Durum ve hata dili

- `Completed`, yalnız workflow'un gerekli doğrulama ve persistence adımları bitince kullanılır.
- `Failed`, terminal ve sınıflandırılmış hata sonucudur; geçersiz provider çıktısı başarılı/partial finding olarak gösterilmez.
- Cancellation için ayrı bir `Cancelled` domain durumu kullanılmaz; kalıcı bir run terminal `Failed` durumuna ve `ErrorCode=Cancelled` değerine geçirilir. HTTP isteğinin iptal temsili bu domain kararını değiştirmez.
- `Partial`, MVP review başarısı için kullanılmaz. Grounding'i geçmeyen adaylar reddedilir; kabul edilen finding'ler başarılı run kurallarının tamamını karşılar.
- “Test” bağlama göre mutlaka **test önerisi**, **TestCase**, **test run** veya **otomatik test** olarak nitelendirilir.
- “Analiz” tek başına canonical use case adı değildir; dosya keşfi için **scan**, LLM code review için **review** kullanılır.

## 5. Yazım ve adlandırma kuralları

- Kod türleri PascalCase (`ReviewRun`, `ScanManifest`); kavramsal metinde ilk kullanımda code formuyla birlikte yazılır.
- Repository içindeki kaynak kanıtı için `FilePath` daima root-relative ve normalize edilmiş separator politikasına uygun olur; absolute path yalnız Project sınırında tutulur.
- Satır numarası her zaman 1-based'dir; aralık iki ucu da kapsar.
- Zaman alanları UTC'dir ve `Utc` son eki taşır.
- “Secret” API key, password, connection string, private key ve denylist ile belirlenen hassas dosyaları kapsar.
- “Production” kullanıcının gerçek çalışma/deployment ortamıdır; CodeGuard AI MVP'sinin yerel demo ortamı production müdahalesi yetkisi vermez.
- “Ücretsiz” üçüncü taraf kota/limitlerinin değişmeyeceği garantisi değildir; ücretli servis olmadan normal test ve fake demo yapılabilmesi anlamına gelir.
