# ADR-004: Docs Agent teknik rapor sözleşmesi

- **Durum:** Kabul edildi; uygulama bekliyor
- **Tarih:** 2026-10-08
- **Karar sahipleri:** Application / API / Persistence

## Bağlam

CodeGuard AI bugün `ReviewAgent` ile grounded bulgular, `TestAgent` ile açık kullanıcı aksiyonuna bağlı test önerileri üretir. Üçüncü agent'ın görevi, tamamlanmış bir `ReviewRun` için bu kalıcı sonuçları insan tarafından okunabilir teknik Markdown raporuna dönüştürmektir.

Docs Agent yeni bir repository analiz akışı değildir. Scanner'ı tekrar çalıştırması, source dosyalarını yeniden okuması veya repository'ye Markdown yazması gerekmemelidir. Canonical kaynak; PostgreSQL'deki completed review, doğrulanmış finding'ler ve varsa aynı review'a ait test önerileridir. Böylece Docs Agent mevcut Review/Test workflow'larını çağırmaz, agent zinciri veya recursive loop oluşturmaz.

Serbest Markdown cevabını doğrudan saklamak; modelin severity, file/line, reason veya suggestion gibi canonical alanları değiştirmesine ve doğrulanamayan referanslar eklemesine izin verir. Bu nedenle provider cevabı önce strict structured JSON olarak alınmalı, persisted kimliklere göre ground edilmeli ve Markdown Application katmanında deterministik olarak render edilmelidir.

## Karar

Docs Agent, Clean Architecture sınırları içinde ayrı bir Application use case'i olacaktır:

```text
HTTP API --> IDocsOrchestrator --> IDocsAgent --> ILLMProvider
                    |                  |
                    |                  +--> structured JSON
                    |                       --> ID grounding
                    |                       --> deterministic Markdown renderer
                    |
                    +--> IDocsWorkflowStore --> PostgreSQL
```

Bu ADR yalnız mimari karar ve public contract'ı sabitler. Domain modeli, migration, agent, provider metodu, controller ve test implementasyonları sonraki aşamalardadır ve henüz mevcut değildir.

### Kaynak veri sınırı

- Yalnız terminal `Completed` durumundaki bir `ReviewRun` kabul edilir.
- Tüm finding'ler persistence'tan yüklenir ve rapora canonical severity, category, file/line, reason ve suggestion değerleriyle dahil edilir.
- Aynı review'a ait test önerileri varsa rapora dahil edilir; test önerisi bulunmaması rapor üretimini engellemez.
- Repository root, source content, filesystem path veya istemci tarafından sağlanan ek context Docs Agent girdisi değildir.
- Docs Agent scanner, file-read tool, test runner, Review Agent veya Test Agent çağırmaz.

### Structured output ve Markdown

Provider'ın planlanan structured cevabı şu semantik alanlarla sınırlıdır:

- rapor başlığı ve executive summary;
- persisted `findingId` başına açıklama, etki ve remediation notu;
- varsa persisted `testCaseId` başına coverage açıklaması;
- sonuç bölümü.

Unknown/duplicate finding veya test kimliği, eksik zorunlu alan ve limit aşımı fail-closed reddedilir. Model severity, category, file/line, canonical reason, canonical suggestion veya test hedefini yeniden tanımlamaz. Bu değerler yalnız persistence kaydından Markdown renderer'a gelir.

Markdown repository'ye yazılmaz. İlk sürümde rapor bir PostgreSQL kaydıdır ve API response içindeki `markdownContent` alanından görüntülenir.

### Public HTTP contract

Canonical endpoint'ler:

```text
POST /api/reviews/{reviewId}/documentation
GET  /api/reviews/{reviewId}/documentation
```

POST request yalnız bounded provider seçimini taşır:

```json
{
  "model": "gemini-3.5-flash-lite",
  "timeoutSeconds": 120
}
```

Planlanan başarılı response:

```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "reviewId": "00000000-0000-0000-0000-000000000000",
  "modelRunId": "00000000-0000-0000-0000-000000000000",
  "title": "Technical review report",
  "markdownContent": "# Technical review report\n...",
  "createdAtUtc": "2026-10-08T00:00:00Z"
}
```

Davranış:

- İlk başarılı POST `201 Created` ve canonical GET location döndürür.
- GET mevcut raporu `200 OK`, bulunamayan raporu `404` döndürür.
- Review bulunamazsa `404 documentation.review_not_found` döner.
- Review completed değilse `409 documentation.review_not_completed` döner.
- Aynı review için ikinci başarılı üretim veya concurrent duplicate istek `409 documentation.already_generated` döner.
- Invalid model/timeout input'u `400` validation response olur.
- Provider/parser/grounding/persistence failure raw exception olmadan stable error code içeren `ProblemDetails` döndürür.
- Client cancellation provider ve persistence sınırlarına taşınır; kısmi rapor başarı olarak kaydedilmez.

### Persistence ve idempotency sözleşmesi

- Bir review için en fazla bir başarılı documentation report bulunur.
- Başarılı rapor ile başarılı `AIModelRun(Purpose=Documentation)` aynı transaction'da yazılır.
- Başarısız deneme failed AI model run olarak audit edilir; report kaydı oluşturmaz ve tekrar denemeyi engellemez.
- Duplicate yarışının son kararı PostgreSQL unique constraint'tir; process-local lock kullanılmaz.
- Review silinirse ilişkili report cascade edilir.

### Logging ve veri koruma

- Log scope en fazla `CorrelationId`, `ReviewRunId` ve `ProjectId` taşır.
- Prompt, structured provider response, Markdown içeriği, finding/test metni, repository path, source content, API key ve connection string loglanmaz.
- Log/metric alanları model, prompt version, duration, input/output character count, finding/test count, report character count ve stable error code ile sınırlıdır.
- Provider API key mevcut User Secrets/environment ve HTTP header redaction politikasını kullanır.

## Sonuçlar ve trade-off'lar

- Review ve Test Agent değişmeden kalır; Docs Agent onların persisted çıktısını tüketir.
- Source yeniden okunmadığı için rapor yalnız review anında doğrulanmış bilgiye dayanır; sonradan değişen repository durumunu yansıtmaz.
- Deterministik renderer canonical veriyi korur; buna karşılık modelin serbest Markdown biçimlendirme esnekliği azalır.
- Tek başarılı rapor politikası küçük ve deterministiktir; rapor versioning/regeneration ilk sürüm kapsamında değildir.
- Markdown DB'de tutulduğu için repository write/overwrite riski oluşmaz; export/download ayrı bir gelecek kararıdır.

## Kapsam dışı

- Repository'ye `README.md`, rapor veya başka dosya yazma/değiştirme.
- Review Agent veya Test Agent'ı Docs Agent içinden çağırma.
- Otomatik rapor üretimi; POST her zaman explicit kullanıcı aksiyonudur.
- Rapor versioning, update, delete veya regenerate endpoint'i.
- MCP Docs tool'u.
- HTML/PDF/DOCX export.
- Production Swagger politikasını değiştirme.

## Başarısızlık davranışı ve doğrulama kapısı

İmplementasyon tamamlanmış sayılmadan önce unit testler strict parsing, ID grounding, canonical field rendering, output limit ve cancellation davranışını; integration testleri POST/GET contract'ı, completed-review kuralı, optional test önerileri, transactional persistence ve duplicate conflict'i kanıtlamalıdır. Tüm otomatik testler fake `ILLMProvider` kullanmalı ve gerçek Gemini/key gerektirmemelidir. Mevcut Review/Test regression suite'i değişmeden yeşil kalmalıdır.

## Alternatifler

- **Modelin raw Markdown cevabını doğrudan saklamak:** Daha az kod gerektirir; canonical finding/test alanları doğrulanamadığı ve hallucination yüzeyi büyüdüğü için reddedildi.
- **Raporu repository'ye yazmak:** Demo için görünür dosya üretir; path/overwrite/working-tree yetkisi ve geri alma problemi açtığı için reddedildi.
- **Docs üretimini review tamamlanınca otomatik başlatmak:** Tek request'te sonuç verir; explicit kullanıcı kontrolünü, failure ayrımını ve workflow sınırını zayıflattığı için reddedildi.
- **Her istekte yeni report version'ı oluşturmak:** Geçmiş sağlar; MVP idempotency ve concurrency modelini gereksiz büyüttüğü için ertelendi.
