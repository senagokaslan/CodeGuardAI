# API contract validation boundary

DataAnnotations yalnız HTTP girdisinin biçim kurallarını tanımlar:

- zorunlu alan;
- güvenli minimum/maksimum metin uzunluğu;
- basit biçim ve aralık kontrolleri.

Repository yolunun normalize edilmesi, erişilebilirliği, benzersizliği, root güvenliği ve diğer iş kuralları API modelinde uygulanmaz. Bunlar Application use case ve Domain invariant sınırlarına aittir. Request DTO güvenilmeyen ve eksik gelebilen girdiyi temsil ettiği için property'leri nullable kalabilir; response DTO yalnız doğrulanmış dış temsili taşır. Domain entity hiçbir zaman request veya response modeli olarak kullanılmaz.

DataAnnotations hataları exception ile normal akışa çevrilmez. `[ApiController]` model validation, `ApiProblemDetailsFactory` üzerinden deterministik RFC 9457 response üretir.

Request cancellation bir validation, not-found veya conflict sonucu değildir. I/O kullanan controller action'ları request `CancellationToken` değerini Application'a iletir; cancellation normal `Result` hatasına çevrilmez, retry edilmez ve bu boundary'de maskelenmez.
