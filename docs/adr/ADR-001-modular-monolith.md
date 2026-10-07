# ADR-001: Modüler monolit ve fiziksel bağımlılık yönü

- **Durum:** Kabul edildi
- **Tarih:** 2026-09-29
- **Karar sahipleri:** Architecture / Application

## Bağlam

CodeGuard AI; HTTP, workflow, domain kuralları ve dış sistem adapter'larını ayırmalıdır. Tek geliştiricili, 28 günlük local-first MVP için microservice sınırları; deployment, network, gözlemlenebilirlik ve veri tutarlılığı maliyetini ürün değerinden önce getirir. Katmansız tek proje ise Gemini, PostgreSQL, filesystem ve process ayrıntılarının iş kurallarına sızması riskini doğurur.

Public contract ve hata davranışı `docs/project-scope.md` içinde tanımlıdır. Fiziksel proje sınırları bu sözleşmeyi değiştirmez: API dış girdiyi ve güvenli hata temsilini sahiplenir; Application use case, `CancellationToken`, typed result/error ve portları sahiplenir; Domain saf invariant'ları taşır; Infrastructure dış I/O ayrıntılarını uygular.

## Karar

Ana uygulama tek deploy edilen modüler monolit olarak dört production ve iki test projesiyle kurulacaktır. Gün 22'de kanıtlanan yerel stdio process sınırı için aynı Application/Infrastructure graph'ını kullanan ince bir `CodeGuardAI.McpHost` executable'ı eklenmiştir; transport kararı [ADR-003](ADR-003-mcp-host.md) içinde kayıtlıdır:

```text
CodeGuardAI.Api ----------------> CodeGuardAI.Application ------> CodeGuardAI.Domain
       |
       +-----------------------> CodeGuardAI.Infrastructure ----> CodeGuardAI.Application
                                           |
                                           +-------------------> CodeGuardAI.Domain

CodeGuardAI.McpHost -----------> CodeGuardAI.Application
       |
       +-----------------------> CodeGuardAI.Infrastructure

CodeGuardAI.UnitTests ---------> Application + Domain
CodeGuardAI.IntegrationTests --> Api (production graph'a transitif erişim)
```

Production project reference allowlist'i:

| Proje | İzinli doğrudan referanslar | Yasak doğrudan referanslar |
|---|---|---|
| `CodeGuardAI.Domain` | Yok | Application, Infrastructure, API ve test projeleri |
| `CodeGuardAI.Application` | Domain | Infrastructure, API ve test projeleri |
| `CodeGuardAI.Infrastructure` | Application, Domain | API ve test projeleri |
| `CodeGuardAI.Api` | Application, Infrastructure | Test projeleri |
| `CodeGuardAI.McpHost` | Application, Infrastructure | API, Domain'e doğrudan referans ve test projeleri |

API'nin Infrastructure referansı yalnız composition root'ta port-adapter wiring içindir. Controller'lar Infrastructure type'larını use case girdisi/çıktısı olarak kullanamaz. Bu kural bugünün iskeletinde code review ile, sonraki bileşenlerde test ve yapılandırma konvansiyonlarıyla korunur.

## Sonuçlar ve trade-off'lar

- Tek process ve deployment basit kalırken proje referansları sorumluluk sınırını derleme zamanında görünür yapar.
- Infrastructure'ın hem Application hem Domain referansı adapter/mapping için bilinçlidir; Domain'in ters yönde referansı yoktur.
- API'nin Infrastructure referansı kötüye kullanılabilir. Bu kalan risk, composition-root kuralı ve architecture testleriyle izlenecektir.
- Ayrı projeler küçük MVP'de dosya/boilerplate maliyeti yaratır; buna karşılık dış servisleri fake adapter'larla değiştirmek ve bağımlılık ihlalini erken yakalamak kolaylaşır.
- Cross-module ihtiyaç kanıtlanmadan yeni production projesi veya generic repository katmanı eklenmez. MCP host istisnası, stdout'u yalnız stdio protokolüne ayırmak ve SDK bağımlılığını API/Application'dan izole etmek için ADR-003 ile kabul edilmiştir.

## Başarısızlık davranışı ve doğrulama

Yasak bir `ProjectReference` build'i mutlaka bozmayabilir; bu nedenle `ProjectReferenceTests` `.csproj` grafiğini exact allowlist ile karşılaştırır ve test projesine ters production referansını reddeder. Restore/build/test başarısızsa veya graph testi kırmızıysa bu ADR uygulanmış sayılmaz. Cancellation ve typed error ayrıntıları bugün feature koduyla uygulanmaz; sınırların sahipliği yukarıdaki gibi korunur.

## Alternatifler

- **Tek proje:** Daha az başlangıç dosyası, fakat dependency inversion yalnız ekip disiplinine kalır.
- **Microservices:** Bağımsız deployment sağlar, fakat MVP için network ve operasyon maliyeti gereksizdir.
- **Infrastructure referansı olmayan API:** Daha katı görünür, fakat ayrı bir composition-root projesi gerektirerek bugünkü kapsamı büyütür.
