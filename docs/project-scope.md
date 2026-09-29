# CodeGuard AI - Proje Kapsamı

**Durum:** Onaya hazır ilk sürüm

**Faz:** Gün 1 / 28 - Project Definition & Architecture

**Son güncelleme:** 2026-09-29

## 1. Amaç ve değer önerisi

CodeGuard AI, kullanıcının kaydettiği yerel bir kaynak kod deposunu güvenli kurallarla tarayan, seçilmiş ve sınırlandırılmış bağlamı bir LLM sağlayıcısına yapılandırılmış inceleme için gönderen, yalnız doğrulanmış bulguları geçmişiyle saklayan ve bu bulgulardan test senaryoları öneren tek kullanıcılı, local-first bir geliştirici aracıdır.

Ürünün temel değeri, LLM çıktısını serbest metin olmaktan çıkarıp gerçek bir dosya ve satır aralığına dayanan, şeması doğrulanmış, filtrelenebilir ve yeniden görüntülenebilir bir inceleme kaydına dönüştürmektir. Ürün bir sohbet uygulaması değildir; ana yaşam döngüsü `Project -> Scan -> ReviewRun -> Finding -> TestCase` şeklindedir.

## 2. Aktörler ve yetki sınırları

| Aktör | Hedefi | Yapabildikleri | Yapamadıkları |
|---|---|---|---|
| Yerel geliştirici (birincil) | Kendi yerel deposunu güvenli biçimde incelemek | Proje kaydetmek, tarama ve review başlatmak, geçmişi/bulguları görmek, test önerisi üretmek, ayrıca izinli test çalıştırmayı açıkça istemek | Modele keyfi shell komutu vermek, ürün üzerinden production kodu değiştirmek veya production ortama müdahale etmek |
| Teknik değerlendirici (ikincil) | Mimari kararları ve güvenlik kanıtlarını değerlendirmek | Demo akışını, geçmişi, ölçütleri ve dokümante edilmiş trade-off'ları incelemek | Yönetimsel ya da çok kiracılı bir rol üstlenmek |
| LLM sağlayıcısı (dış sistem) | Sınırlandırılmış bağlamdan yapılandırılmış aday çıktı üretmek | Versiyonlu prompt ve şema kapsamında review/test adayı döndürmek | Dosya sistemine, veritabanına, Git'e, shell'e veya production ortama doğrudan erişmek |
| Yerel PostgreSQL (dış sistem) | Doğrulanmış kayıtları ve audit metadata'yı saklamak | Uygulamanın persistence adapter'ı üzerinden veri saklamak | Domain veya workflow kararı vermek |

Tek kullanıcı ve güvenilir yerel operatör varsayılır. Repository içeriği, LLM cevabı ve tool girdileri yine de güvenilmeyen veri kabul edilir.

## 3. Ana demo akışı

1. Kullanıcı bir proje adı ve sunucunun erişebildiği mutlak yerel repository yolu ile `Project` kaydı oluşturur.
2. Sistem yolu normalize eder; erişim ve benzersizlik kontrolleri başarısızsa açık, sınıflandırılmış hata döndürür.
3. Kullanıcı scan başlatır; sistem kök sınırı, allowlist, secret, reparse point ve boyut kurallarını uygular.
4. UI, dahil edilen ve atlanan dosya sayılarını; atlananlar için gerekçeleri gösterir.
5. Kullanıcı review başlatır; sistem bir `ReviewRun` oluşturur ve deterministik bütçeyle satır numaralı `RepositoryContext` üretir.
6. Review Agent, versiyonlu prompt ve yapılandırılmış çıktı sözleşmesiyle `ILLMProvider` üzerinden LLM'i çağırır.
7. Cevap strict parse, zorunlu alan, enum/aralık, gerçek dosya/satır grounding ve de-duplication kontrollerinden geçer.
8. Yalnız geçerli `Finding` kayıtları saklanır; run terminal olarak `Completed` veya typed hata ile `Failed` olur.
9. Kullanıcı review geçmişini açar; bulguları severity, category, file ve line alanlarıyla görür ve filtreler.
10. Kullanıcı tamamlanmış bir review için ayrı bir aksiyonla test önerileri üretir; grounded ve benzersiz `TestCase` kayıtları saklanır.
11. Test önerisi kaynak kodu değiştirmez. Test çalıştırma, ancak kullanıcının ayrıca açıkça istediği allowlisted `dotnet test` operasyonudur.
12. Kullanıcı eski review'ları yeniden görüntüler; AI/tool audit metadata'sı bulunur, secret veya ham kaynak içerik loglanmaz.

Demo, gerçek Gemini erişimi yokken `FakeLLMProvider` ile offline ve deterministik yürütülebilmelidir. Gerçek Gemini çağrısı yalnız ayrı, manuel ve opt-in smoke kontroldür.

## 4. Kullanıcı hikayeleri ve başarı ölçütleri

| Kimlik | Kullanıcı hikayesi | Başarı ölçütü |
|---|---|---|
| US-01 | Geliştirici olarak yerel repository'mi kaydetmek istiyorum; böylece sonraki işlemler doğru kökle sınırlı olsun. | Geçerli ve erişilebilir mutlak yol normalize edilerek tekil kaydedilir; geçersiz, erişilemez veya tekrar eden kök typed 4xx hata üretir. |
| US-02 | Geliştirici olarak hangi dosyaların neden dahil/atlandığını görmek istiyorum; böylece modele gönderilecek yüzeyi denetleyebileyim. | Scan manifesti included/skipped sayılarını ve her skip nedenini verir; kök dışı, reparse, secret ve limit ihlalleri dahil edilmez. |
| US-03 | Geliştirici olarak repository'm için kanıta dayalı review almak istiyorum; böylece bulguyu kaynakta doğrulayabileyim. | Her saklanan Finding manifestteki relative bir dosyaya ve gerçek 1-based satır aralığına dayanır; geçersiz aday saklanmaz. |
| US-04 | Geliştirici olarak review sonucunu filtrelemek ve geçmişe dönmek istiyorum; böylece sonuçları karşılaştırabileyim. | Tamamlanmış review yeniden getirilebilir; findings severity/category/file alanlarıyla görüntülenip filtrelenebilir. |
| US-05 | Geliştirici olarak bulgulardan test senaryosu önermek istiyorum; böylece regresyon kapsamı planlayabileyim. | Yalnız completed review'dan, grounded hedefe sahip, adı/senaryosu/gerekçesi dolu ve tekrarsız TestCase önerileri oluşur. |
| US-06 | Geliştirici olarak önerilen testleri ayrı bir onayla güvenli çalıştırmak istiyorum; böylece keyfi komut riski olmadan sonucu görebileyim. | Raw command kabul edilmez; sabit `dotnet test`, allowlisted hedef, timeout, output cap ve process-tree sonlandırma uygulanır. |
| US-07 | Teknik değerlendirici olarak dış servis olmadan ana davranışı doğrulamak istiyorum; böylece demo kota ve ağdan bağımsız olsun. | Normal test paketi Gemini key, internet, Docker veya PostgreSQL gerektirmeden fake provider ile review/test başarı ve ana güvenlik hata yollarını kanıtlar. |

## 5. In scope

- Tek kullanıcılı, yerel makinede çalışan ASP.NET Core tabanlı web uygulaması ve basit frameworksüz UI.
- Yerel project kaydı, review geçmişi ve local Windows PostgreSQL persistence.
- Güvenli repository scanner, scan manifesti, bütçeli ve satır numaralı context üretimi.
- `ILLMProvider` arkasında Gemini REST adapter'ı; fake provider ile deterministik offline doğrulama.
- Şemaya bağlı review çıktısı, fail-closed alan doğrulaması ve file/line grounding.
- Grounded findings için listeleme ve severity/category/file filtreleri.
- Completed review tabanlı test senaryosu önerileri.
- Açık kullanıcı aksiyonuyla, raw shell kabul etmeyen ve sınırlandırılmış `dotnet test` runner'ı.
- Güvenli tool sözleşmelerinin üzerine eklenen ince MCP adapter'ı (`read_file`, `run_tests`). MCP bir güvenlik sınırı değildir.
- AI model çağrısı ve tool çalıştırması için redacted audit metadata ile structured logging.
- Unit, integration, scanner/tool security, provider contract, structured output ve offline eval testleri.
- Secretsiz ve containersız GitHub Actions restore/build/test doğrulaması.
- Kurulum, mimari, güvenlik, evaluation, sınırlamalar ve demo dokümantasyonu.

## 6. Out of scope ve bilinçli yasaklar

| Kapsam dışı | Karar ve gerekçe |
|---|---|
| Docker, container dosyaları, komutları ve CI service container'ları | Local-first Windows PostgreSQL hedefi ve 28 günlük kapsam korunur. Containerization ancak hedef deployment modeli değişirse gelecekte ayrı karardır. |
| Python, FastAPI veya production solution'da Python tabanlı yardımcı servis | Backend tek teknoloji sınırı olarak C#/.NET kalır. Doküman üretiminde kullanılan harici araçlar ürün runtime'ının parçası sayılmaz. |
| Otomatik source code/test code değiştirme, patch uygulama, commit veya push | İlk sürüm yalnız öneri ve açıkça izinli test çalıştırma yapar; yanlış LLM çıktısının kalıcı değişiklik üretmesi engellenir. |
| Production sisteme, production repository'ye veya deployment ortamına müdahale | Ürün yerel analiz aracıdır; deploy, rollback, incident response ya da production command çalıştırmaz. |
| Keyfi shell/PowerShell/cmd çalıştırma | Runner yalnız doğrulanmış typed girdiden sabit `dotnet test` operasyonu üretir. |
| Çok kullanıcılı kullanım, authentication/authorization ve multi-tenancy | MVP tek güvenilir yerel operatör içindir; remote kullanım ihtiyacı kanıtlanmadan eklenmez. |
| GitHub/PR entegrasyonu ve otomatik review comment'i | OAuth, webhook, rate limit ve dış yan etki kapsamı büyütür; local MVP sonrası değerlendirilir. |
| Microservice, event bus, message broker, RAG/pgvector, birden fazla LLM sağlayıcısı | Modüler monolit ve deterministik context baseline tamamlanmadan operasyonel karmaşıklık eklenmez. |
| MCP'nin sandbox/authorization yerine kullanılması veya advanced MCP tool'ları | Güvenlik alttaki tool implementasyonunda kalır; ilk sürüm iki tool ile sınırlıdır. |

## 7. Public contract ve veri sözleşmeleri

Bu belge route/DTO ayrıntılarını dondurmaz; Gün 2 ve sonraki günlerde fiziksel hale getirilecek davranış sözleşmesini sabitler:

- Dış girdiler API sınırında doğrulanır; domain entity'leri request/response veya LLM DTO'su olarak doğrudan kullanılmaz.
- Kaynaklar `Project`, `ScanManifest`, `ReviewRun`, `Finding`, `TestCase`, `AIModelRun` ve `ToolExecution` kavramlarıyla ifade edilir.
- Başarılı yazma işlemleri açık bir kimlik ve güncel durum döndürür. Okuma sonuçları makineye özgü secret ve absolute finding path'i içermez.
- Hatalar tutarlı, makinece sınıflandırılabilir bir Problem Details/typed error sözleşmesiyle döner; validation, not-found, conflict, unprocessable input, provider, timeout ve cancellation birbirine karıştırılmaz.
- Bir operasyonun kısmen ürettiği doğrulanmamış LLM verisi başarılı response veya kalıcı Finding/TestCase sayılmaz.
- Scan sonucu limit yüzünden atlanan dosyaları hata gibi gizlemez; manifestte nedenleriyle bildirir.

### 7.1 Finding sözleşmesi

| Alan | Anlamı | Kural |
|---|---|---|
| `Id` | Finding kimliği | Sistem üretir; kalıcı kayıtta zorunlu GUID |
| `ReviewRunId` | Kaynak review | Var olan run'a bağlanır |
| `Severity` | Etki düzeyi | Bilinen enum değeri; bilinmeyen değer fail-closed reddedilir |
| `Category` | Bulgu sınıfı | İzinli kategori: bug, security, maintainability, performance, reliability veya code quality |
| `FilePath` | Kanıt dosyası | Repository root'a göre relative; manifestte bulunmalı; absolute olamaz |
| `StartLine` | Kanıt başlangıcı | 1-based ve `>= 1`; gerçek dosya sınırları içinde |
| `EndLine` | Kanıt sonu | `>= StartLine`; gerçek dosya sınırları içinde |
| `Title` | Kısa bulgu adı | Zorunlu ve boş olmayan metin |
| `Reason` | Neden sorun olduğu | Zorunlu ve boş olmayan, kanıtla ilişkili metin |
| `Suggestion` | Güvenli düzeltme yönü | Zorunlu ve boş olmayan öneri; otomatik uygulanmaz |
| `Confidence` | Model güveni | Decimal, `0 <= confidence <= 1`; ürün doğrulamasının yerine geçmez |
| `CreatedAtUtc` | Oluşturma zamanı | Sistem üretir; UTC |

LLM çıktısının JSON olarak parse edilmesi tek başına başarı değildir. Structural validation, enum/range validation, file/line grounding ve de-duplication tamamlanmadan Finding domain nesnesine veya persistence'a geçemez.

## 8. Review ve test başarı ölçütleri

### Review başarısı

Bir review ancak aşağıdaki koşullar birlikte sağlanırsa başarılıdır:

1. Scan ve context bütçeleri deterministik uygulanmıştır; dahil/atlanan içerik açıklanabilir.
2. Provider cevabı beklenen JSON sözleşmesine parse edilmiş ve bütün zorunlu alanlar doğrulanmıştır.
3. Her saklanan finding izinli enum/aralıklara, manifestteki relative path'e ve gerçek 1-based satır aralığına sahiptir.
4. Duplicate adaylar saklanmamış, reddedilen grounded olmayan adaylar metrik olarak sayılmıştır.
5. `ReviewRun` terminal `Completed` durumundadır ve doğrulanmış finding'ler transaction ile saklanmıştır. Sıfır geçerli finding, temiz sonuç olarak açıkça temsil edilebilir.
6. Model, prompt version, duration ve finding/rejected-finding sayıları redacted metadata olarak izlenebilir; key, raw prompt, raw response ve raw source loglanmamıştır.

Başarısız parse, bilinmeyen enum, provider timeout/availability veya tamamlanamayan workflow başarılı review sayılmaz. Run typed error ile terminal `Failed` olur; grounding'i geçmeyen tekil adaylar karantinaya alınır/reddedilir ve Finding olarak saklanmaz.

### Test önerisi başarısı

Bir test önerisi akışı ancak completed review ve seçilmiş grounded findings üzerinden çalışır. Her TestCase için `Name`, `Type`, `Target`, `Scenario` ve `Reason` dolu; target mevcut bağlamda grounded ve aynı run içinde öneri tekrarsız olmalıdır. `SuggestedTestCode` opsiyoneldir ve hiçbir zaman otomatik yazılmaz. Hata, mevcut review/finding kayıtlarını bozmaz; yalnız typed hata ve redacted model-run metadata üretir.

### Test çalıştırma başarısı

Test önerisi ile test çalıştırma ayrı aksiyonlardır. Çalıştırma yalnız allowlisted proje/test hedefinde sabit `dotnet test` komutu, timeout, output cap, cancellation ve process-tree kill davranışıyla yapılır. Exit code, süre ve sınırlandırılmış özet audit edilir. Kullanıcı girdisinden raw command oluşturulması veya shell açılması başarısız güvenlik kontrolüdür.

## 9. Mimari sınır ve bağımlılık yönü

Planlanan modüler monolit bağımlılık yönü:

```text
API -> Application -> Domain
API -> Infrastructure (yalnız composition root'ta wiring)
Infrastructure -> Application + Domain
```

- **Domain:** Entity, enum ve invariant; framework ve I/O bağımlılığı yoktur.
- **Application:** Use case, port/interface, DTO ve bounded workflow; Domain'e bağımlıdır, somut Gemini/Npgsql/filesystem/process/MCP implementasyonlarını bilmez.
- **Infrastructure:** Application portlarını EF Core/PostgreSQL, Gemini, filesystem, process ve MCP ayrıntılarıyla uygular.
- **API:** HTTP contract, validation, Problem Details ve composition root; iş kuralını controller'a taşımaz.

MCP, safe tool'lardan sonra gelen bir adapter'dır. LLM sağlayıcısı `ILLMProvider` arkasındadır. Böylece dış servis değişimi üst seviye workflow'u değiştirmez ve offline fake ile test edilebilir.

## 10. Cancellation ve hata davranışı

- Tüm I/O ve workflow girişleri `CancellationToken` taşır; token scanner, provider, persistence ve process sınırlarına iletilir.
- Kullanıcı/host cancellation'ı retry edilmez ve genel provider hatasına çevrilmez. Başlamamış veya doğrulanmamış veri başarı diye saklanmaz.
- Kalıcı `ReviewRun` başladıktan sonra cancellation oluşursa run terminal `Failed` durumuna ve `ErrorCode=Cancelled` değerine geçirilir; `Running` durumda sahipsiz bırakılmaz. Bu davranış Gün 6/24'te fiziksel hale getirilecek, yeni bir `Cancelled` durumuyla çoğaltılmayacaktır.
- Provider retry yalnız transient 429/502/503 sınıfında, provider adapter'ı içinde ve sınırlandırılmıştır. Validation, grounding, cancellation ve kalıcı client hataları retry edilmez.
- Timeout ayrı typed hata olarak gözlemlenir; test process'i timeout/cancellation halinde process tree ile sonlandırılır.
- Invalid LLM response ve grounded olmayan finding fail-closed davranır. Secret veya raw payload hata/log mesajına eklenmez.
- Aynı proje için eşzamanlı duplicate review isteği deterministik conflict üretir; serbest/recursive agent loop yoktur.

## 11. Ücretsiz ve local-first varsayımları

- Uygulama tek geliştirici makinesinde çalışır; browser UI ve ASP.NET Core host aynı yerel ürünün parçalarıdır.
- Repository yolu server-local path'tir; taranan dosyalar varsayılan olarak yerel kalır, yalnız policy ile seçilmiş bounded context Gemini'ye gönderilebilir.
- Backend C#/.NET; veritabanı local Windows PostgreSQL'dir. Docker/container gerekmez.
- Hedeflenen geliştirme araçları, Git/GitHub olanakları, PostgreSQL ve normal CI akışı ücret gerektirmeyen katmanlarda kullanılabilir.
- Gemini ücretsiz kotası hedeflenir fakat kullanılabilirlik veya kota garanti değildir. Normal testler ve ana demo fake provider ile ücretsiz/offline çalışır.
- Gerçek Gemini anahtarı yalnız environment variable veya User Secrets ile verilir; repository, örnek config, log ve database içinde tutulmaz.
- GitHub Actions ücretsiz kullanım limitleri aşılırsa local `dotnet restore/build/test` doğrulaması kaynak gerçekliğidir; ürün ücretli servise bağımlı kabul edilmez.
- Ücretsiz/local-first, sıfır dış veri paylaşımı anlamına gelmez: gerçek LLM review seçilmiş context'i sağlayıcıya gönderir ve kullanıcı bunu başlatırken görünür biçimde bilmelidir.

## 12. 28 günlük Definition of Done

MVP ancak aşağıdaki maddelerin tamamı gerçek uygulama ve kanıtla sağlandığında “done” kabul edilir:

- Backend tamamen C#/.NET 10 ve ASP.NET Core'dur; production solution'da Python yoktur.
- Repository'de Docker/container dosyası veya komutu, CI'da service container yoktur.
- Local Windows PostgreSQL kurulumu sıfırdan tekrarlanabilir; EF migration güncel ve uygulanmıştır.
- `Project`, `ReviewRun`, `Finding`, `TestCase`, `AIModelRun` ve `ToolExecution` modeli API sözleşmesiyle uyumludur.
- Scanner root confinement, traversal, reparse, secret, extension, file-count ve byte limitlerini testlerle uygular.
- Application somut Gemini'ye değil `ILLMProvider` portuna bağlıdır.
- Normal `dotnet test` internet, Gemini çağrısı/key'i veya PostgreSQL gerektirmeden başarılıdır; database testleri açıkça local opt-in kategorisindedir.
- Structured JSON; zorunlu alan, enum/range, file/line grounding ve de-duplication geçmeden persistence'a girmez.
- Test Agent production/source kodu değiştirmez; runner raw shell/command kabul etmez.
- MCP yalnız çalışan ve test edilmiş safe tool servisleri üzerine ince adapter olarak eklenmiştir.
- Review/test workflow'ları bounded, cancellation-aware ve terminal state üretir; recursive agent loop yoktur.
- UI project -> scan -> review -> finding/history/filter -> test önerisi ana demo akışını yürütür; loading/error/empty durumları görünürdür.
- Audit/log metadata yapılandırılmış ve redacted'dır; secret, raw source, prompt veya model response sızdırmaz.
- CI secretsiz/container'sız restore, build ve offline test çalıştırır; hata pipeline'ı başarısız yapar.
- README, architecture, database, setup, security, testing/eval, demo ve limitations belgeleri güncel uygulamayla uyumludur.
- Beş dakikalık demo ve en az üç gerekçeli eval fixture'ı (hedef beş) ölçülebilir, yeniden üretilebilir sonuç verir.
- Hiçbir tamamlanmamış özellik dokümanda, demoda veya CV maddesinde tamamlanmış gösterilmez.

## 13. Özellikten kanıta izlenebilirlik

| Özellik | Ana başarı kanıtı |
|---|---|
| Project kaydı | API integration testi: valid create, invalid path, duplicate normalized root |
| Güvenli scan | Scanner security testleri: traversal/reparse/secret/limit ve skip reason |
| Bütçeli context | Deterministik budget/line metadata unit testleri |
| Structured review | Fake provider uçtan uca integration + malformed JSON/unknown enum testleri |
| Grounded findings | Unknown file ve out-of-range line'ın persistence öncesi reddedildiği test |
| History ve filtreler | Read-only API projection/paging/filter integration testleri |
| Test önerileri | Completed review, grounded target, required fields ve duplicate kontrol testi |
| Safe test runner | Raw args/metacharacter/outside-root/timeout/output-cap security testleri |
| MCP adapter | Aynı safe tool contract'ını kullandığını kanıtlayan contract testi |
| UI demo | Manuel demo checklist: loading/error/empty dahil ana akış |
| Observability | Redaction ve gerekli structured alanların log/audit testi |
| CI | Temiz runner'da secretsiz, containersız restore/build/test sonucu |

## 14. Risk register - v1

Sahiplik kişi adına değil, 28 günlük plandaki sorumluluk rolüne atanır; tek kişilik projede geliştirici ilgili rolü üstlenir.

| Kimlik | Risk / erken sinyal | Olasılık / Etki | Sahip | Azaltma adımı | Doğrulama / kalan risk |
|---|---|---|---|---|---|
| R-01 | Repository root dışına okuma; `..`, absolute sibling veya reparse girişimi | Orta / Kritik | Scanner & Security sahibi | Canonical root, separator-aware containment, manifest allowlist, reparse reject | Security fixture'ları tüm kaçışları reddeder. Windows filesystem farklarından kalan risk threat model'de izlenir. |
| R-02 | Secret'ın context, log veya Git'e sızması; `.env`, key pattern veya raw payload görülmesi | Orta / Kritik | Security & Observability sahibi | Path/name denylist, bounded reader, User Secrets/env, redacted logs, secret scan | Secret fixture ve log-capture testi; sağlayıcıya gönderilen seçilmiş kaynak için kullanıcı farkındalığı kalan risktir. |
| R-03 | LLM'in uydurma dosya/satırının gerçek finding olarak saklanması | Yüksek / Yüksek | Review Agent sahibi | Strict DTO validation, manifest ve line-count grounding, fail-closed persistence | Unknown file/out-of-range testleri; semantik false positive eval ile izlenir. |
| R-04 | Malformed/uyumsuz provider JSON'u veya model değişiminin akışı bozması | Orta / Yüksek | LLM Provider sahibi | Versiyonlu prompt/schema, strict deserialize, typed errors, fake HTTP contract testleri | Missing/extra/wrong-type/unknown-enum fixture'ları; gerçek sağlayıcı drift'i opt-in smoke ile izlenir. |
| R-05 | Gemini kota, ağ, 429/5xx veya timeout nedeniyle demonun çalışmaması | Yüksek / Orta | Demo & Provider sahibi | FakeLLMProvider varsayılan demo/test yolu; bounded transient retry; açık timeout | Offline demo ve contract testleri; gerçek servis sürekliliği garanti edilmez. |
| R-06 | Prompt injection'ın tool yetkisini veya scope'u genişletmesi | Orta / Kritik | Agent & Tool Security sahibi | Repo içeriğini data olarak sınırla; authorization'ı modelden bağımsız tool policy'de uygula | “ignore instructions” fixture'ı; LLM kalitesi değil policy sonucu belirleyicidir. |
| R-07 | Test runner üzerinden command injection veya sahipsiz process | Orta / Kritik | Tool Runner sahibi | Shell yok, sabit executable/verb, `ArgumentList`, allowlisted target, timeout/output cap/process-tree kill | Metacharacter, outside-root, timeout testleri; untrusted code çalıştırma local makinede kalan risktir ve production desteği yoktur. |
| R-08 | Büyük repository'nin context/maliyet/zaman sınırlarını aşması | Yüksek / Orta | Context Builder sahibi | Max files, per-file bytes, total chars, deterministic priority ve açık skip reasons | Oversized fixture; büyük repo kalitesi sınırlı olabilir, RAG MVP dışıdır. |
| R-09 | Yarım kalan veya duplicate review'ın belirsiz durumda kalması | Orta / Yüksek | Workflow & Persistence sahibi | Tek aktif run politikası, terminal state, cancellation propagation, transaction ve recovery kuralı | Concurrent/cancellation/incomplete-run integration testleri; kesin concurrency yöntemi sonraki mimari günlerinde ADR ile seçilir. |
| R-10 | Scope creep'in 28 günlük MVP'yi geciktirmesi | Yüksek / Yüksek | Product/Scope sahibi | Bu belge, out-of-scope listesi, günlük acceptance gate ve ayrı future backlog | Her gün değişiklik-scope kontrolü; yeni özellik yalnız açık scope revizyonuyla girer. |
| R-11 | Docker yasağı nedeniyle CI'ın gerçek PostgreSQL ilişkisel davranışını kanıtlayamaması | Yüksek / Orta | Testing & CI sahibi | Offline unit/API suite ile local opt-in `Category=Database` testlerini ayır; sınırı README'de açıkla | Local migration/FK/index testi gerekir; temiz CI yalnız relational doğrulamanın tamamını kanıtlamaz. |
| R-12 | Terminoloji veya contract drift'i; “scan”, “review” ve “test” kavramlarının karışması | Orta / Orta | Architecture/Docs sahibi | `docs/glossary.md` canonical terimleri; DTO/API/docs review | Case-insensitive terim ve alan taraması; kod oluşunca contract testleri. |

## 15. Gün 2 giriş sözleşmesi

Bir sonraki gün yalnız bu scope onaylandıktan sonra şu kararları fiziksel solution sınırlarına dönüştürebilir: modüler monolit, `API -> Application -> Domain` ve `Infrastructure -> Application + Domain` bağımlılık yönü, port/adapter ayrımı ve bu belgedeki public davranış. Gün 2; feature implementasyonu, database veya provider entegrasyonu yapmamalıdır.

## 16. Git ve Markdown öğrenme notu

**Git**, yalnız dosya depolama değil, kararları küçük ve geri alınabilir değişiklikler halinde kanıtlama aracıdır. Bu fazdaki trade-off, çok sayıda erken commit yerine scope onayından sonra tutarlı tek bir küçük dokümantasyon değişikliği yapmaktır. Git uygulama katmanlarına bağımlı değildir; repository genelinde değişiklik geçmişini taşır. Başarısızlık davranışı merge/contract çakışmasını gizlemek değil, farkı görünür kılıp bilinçli çözmektir. Gün 1 tesliminde klasör henüz Git repository'si değildi; Gün 2 başlangıcında `main` branch'iyle repository oluşturulmuş ve build/test çıktıları `.gitignore` ile kapsam dışına alınmıştır.

**Markdown**, insan tarafından okunabilir kararları review edilebilir düz metin sözleşmesine dönüştürür. Hafif ve diff dostudur; buna karşılık schema validation veya otomatik davranış garantisi vermez. Bu nedenle scope, tablo, alan kuralları ve ölçütler açık yazılmış; ilerleyen günlerde kod/test kanıtıyla bağlanması için izlenebilirlik tablosu eklenmiştir. Markdown'ın başarısızlık riski terim drift'i ve güncelliğini yitirmedir; canonical sözlük ve günlük acceptance review bunu azaltır.
