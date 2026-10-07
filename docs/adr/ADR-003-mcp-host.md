# ADR-003: MCP host ve stdio transport

- **Durum:** Kabul edildi
- **Tarih:** 2026-10-07
- **Karar sahipleri:** Architecture / Infrastructure

## Bağlam

CodeGuard AI'nin `read_file` ve `run_tests` yetenekleri; repository root doğrulaması, path/target sınırları, timeout, output cap, yetkilendirme ve audit davranışlarını zaten `IFileReadTool` / `SafeFileReadTool` ile `ITestRunnerTool` / `SafeDotnetTestRunner` içinde uygular. MCP bu güvenliği sağlayan yeni bir katman değil, mevcut yetenekleri standart protokol üzerinden sunan bir adapter olmalıdır.

MVP local-first'tür ve aynı anda tek yerel MCP istemcisi hedeflenir. Transport seçimi stdio ile ASP.NET Core Streamable HTTP arasındadır. HTTP; port, host-header/DNS-rebinding savunması, authentication/authorization ve deployment kararlarını bugünün kapsamına ekler. Stdio ise MCP istemcisinin child process olarak başlattığı tek oturumlu yerel host modeline uyar.

## Karar

Ayrı bir `CodeGuardAI.McpHost` executable projesi ve resmi `ModelContextProtocol` C# SDK'sının stdio server transport'u kullanılacaktır.

```text
MCP client --stdio--> CodeGuardAI.McpHost --> IProjectQueries
                                          --> IFileReadTool --> SafeFileReadTool
                                          --> ITestRunnerTool --> SafeDotnetTestRunner
```

- SDK paketi yalnız `CodeGuardAI.McpHost` içinde bulunur. Application ve Domain MCP'yi bilmez.
- `read_file` input'u `repositoryId` ve `relativePath` ile sınırlıdır. İstemci/model mutlak repository root belirleyemez; root kayıtlı Project'ten server tarafında yüklenir.
- Adapter filesystem API'si kullanmaz ve güvenlik kuralı kopyalamaz. Okuma yalnız `IFileReadTool.ExecuteAsync` üzerinden yapılır.
- `run_tests` input'u yalnız `repositoryId` ve repository-relative `.csproj` target'tır. Command, ek argument, working directory, executable ve timeout protokol girdisi değildir.
- Test runner timeout'u ve output cap'i MCP handler'da uygulanmaz; aynı `TestRunnerOptions` ile `SafeDotnetTestRunner` ve tool execution envelope tarafından uygulanır.
- Test process'inin non-zero exit code ile tamamlanması `tests_failed`, process/target/timeout/audit sorunu `tool_error` olarak ayrı temsil edilir. Test failure bir tool infrastructure exception'ı gibi maskelenmez.
- Tool cancellation token'ı Project sorgusuna ve ilgili `IFileReadTool` / `ITestRunnerTool` portuna aynen iletilir.
- Beklenen tool hataları mevcut error code ile, bulunamayan repository ve geçersiz istekler MCP'ye özgü sabit error code ile yapılandırılmış cevap olur. Beklenmeyen exception mesajı veya stack trace protokol cevabına yazılmaz.
- Stdio protokolü stdout'u kullandığı için host logging provider'ları açıkça yeniden kurulur ve `Trace` dahil tüm log seviyeleri stderr'e yönlendirilir; stdout yalnız MCP mesajlarına ayrılır.

## Sonuçlar ve trade-off'lar

- Local kurulum tek process ve tek istemci için küçük kalır; ayrı HTTP endpoint'i ve ağ saldırı yüzeyi oluşmaz.
- Her istemci kendi server process'ini başlatır; paylaşımlı veya uzak kullanım desteklenmez.
- MCP host, persistence üzerinden kayıtlı repository kimliğini çözdüğü için geçerli `Database:ConnectionString` configuration ister.
- İleride uzak/çok istemcili ihtiyaç kanıtlanırsa `ModelContextProtocol.AspNetCore` ve Streamable HTTP ayrı bir ADR ile değerlendirilecektir; authentication, authorization, allowed-hosts ve CORS bu kararın zorunlu parçaları olacaktır.

## Başarısızlık davranışı ve doğrulama

MCP smoke testleri gerçek SDK server/client transport'u üzerinden `read_file` ve `run_tests` tool'larının keşfedildiğini ve typed structured response döndürdüğünü kanıtlar. Adapter testleri path traversal/secret dosya ve arbitrary-command/test-target vakalarını gerçek safe tool servisleriyle çalıştırır; timeout, output cap ve audit kaydını doğrular. Architecture testi Application projesinde MCP package/reference bulunmadığını doğrular.

## Alternatifler

- **Mevcut ASP.NET Core API içinde Streamable HTTP:** Uzak erişim ve process paylaşımı sağlar; MVP'de gereksiz authentication ve ağ güvenliği kapsamı açar.
- **MCP handler içinde doğrudan filesystem okuma:** Daha az wiring gibi görünür; iki ayrı güvenlik politikası oluşturduğu ve audit/authorization zarfını atlattığı için reddedildi.
- **Infrastructure projesine SDK bağımlılığı ekleme:** Adapter'ı mevcut projede tutar; fakat bütün Infrastructure consumer'larına protokol bağımlılığı taşır. Ayrı host fiziksel sınırı daha görünür kılar.
