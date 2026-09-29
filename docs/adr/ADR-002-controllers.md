# ADR-002: HTTP sınırında controller kullanımı

- **Durum:** Kabul edildi
- **Tarih:** 2026-09-29
- **Karar sahipleri:** API / Architecture

## Bağlam

Planlanan API; Project, Scan, ReviewRun, Finding ve TestCase etrafında büyüyen bir endpoint haritasına sahiptir. Public contract; boundary validation, tutarlı HTTP status/Problem Details, cancellation ve typed error eşlemesini görünür tutmalıdır. ASP.NET Core hem Minimal API hem controller yaklaşımını destekler.

## Karar

HTTP endpoint'leri ASP.NET Core controller sınıflarıyla tanımlanacaktır. Gün 2'de API projesi yalnız `AddControllers` ve `MapControllers` ile controller altyapısını kurmuştur. Gün 3'te ilk dikey dilim olarak typed response kullanan, dış bağımlılık veya secret ayrıntısı döndürmeyen `GET /health` endpoint'i eklenmiştir.

Controller sorumluluğu şunlarla sınırlıdır:

- HTTP request/response modelleri, route ve status code eşlemesi.
- Boundary validation ve güvenli Problem Details üretimi.
- Framework'ün sağladığı request cancellation token'ını Application use case'ine iletmek.
- Application sonucunu HTTP'ye çevirmek; domain/workflow kararı vermemek.

Controller doğrudan DbContext, Gemini client, filesystem, process veya MCP adapter'ı kullanamaz. Infrastructure yalnız API composition root tarafından DI wiring amacıyla görülür.

## Sonuçlar ve trade-off'lar

- Kaynak bazlı büyüyen endpoint'ler sınıf ve action düzeyinde gruplanır; ortak attribute/filter davranışı görünür olur.
- Controller modeli Minimal API'ye göre daha fazla dosya ve framework convention'ı getirir.
- Minimal API küçük health/prototype uçlarında daha kısa olabilirdi; ancak iki HTTP stili kullanmak public contract'ı parçalayacağından MVP'de tek stil seçilir.
- Controller seçimi domain modellerini HTTP modeli yapmaz; DTO/domain ayrımı korunur.

## Cancellation ve hata davranışı

Action metotları I/O veya workflow çağırdığında `CancellationToken` alıp Application'a iletecektir. Cancellation retry edilmez veya provider hatası diye maskelenmez. Validation, not-found, conflict, unprocessable input, provider, timeout ve cancellation tek bir genel 500 cevabında birleştirilmez. Gün 3'te `AddProblemDetails`, exception handler ve status-code pages composition root'a eklenmiştir; feature'a özgü hata eşlemesi ilgili contract gününde genişletilecektir.

## Başarısızlık davranışı ve doğrulama

Controller'ın Infrastructure servisini doğrudan kullanması, business logic taşıması veya cancellation token'ı yutması bu ADR ihlalidir. API assembly'sinin test projesinden keşfedilmesi ve solution'ın warnings-as-errors ile derlenmesi korunur. Gün 3 integration testleri health contract'ını, güvenli 404 Problem Details cevabını ve OpenAPI'nin yalnız Development ortamında sunulmasını doğrular.

## Alternatif

**Minimal API**, daha az ceremony ve küçük endpoint'lerde hızlı başlangıç sağlar. Planlanan endpoint sayısı, öğretici boundary görünürlüğü ve tutarlı controller konvansiyonu ağır bastığı için seçilmemiştir.
