# Configuration and secret setup

CodeGuard AI, ASP.NET Core'un varsayılan configuration pipeline'ını kullanır. Zorunlu secret değerleri repository'deki `appsettings*.json` dosyalarına yazılmaz ve startup sırasında değerleri loglanmaz.

## Zorunlu anahtarlar

| Anahtar | Amaç | Repository'de değer bulunur mu? |
|---|---|---|
| `Database:ConnectionString` | İleride local Windows PostgreSQL adapter'ına verilecek bağlantı | Hayır |
| `Gemini:ApiKey` | İleride Gemini provider adapter'ına verilecek API anahtarı | Hayır |

Options binding Application katmanındaki `DatabaseOptions` ve `GeminiOptions` tiplerine yapılır. API composition root her iki değeri startup sırasında doğrular. Eksik veya yalnız whitespace olan değer host açılmadan `OptionsValidationException` üretir. Mesaj yalnız eksik configuration anahtarının adını içerir; secret değerini veya configuration dump'ını içermez.

## Development - User Secrets

API projesinde `UserSecretsId` tanımlıdır. Aşağıdaki komutlarda yer tutucuları kendi yerel değerlerinle değiştir:

```powershell
dotnet user-secrets set "Database:ConnectionString" "<LOCAL_POSTGRES_CONNECTION_STRING>" --project src/CodeGuardAI.Api
dotnet user-secrets set "Gemini:ApiKey" "<YOUR_GEMINI_API_KEY>" --project src/CodeGuardAI.Api
```

Anahtarların tanımlı olduğunu yerel olarak kontrol etmek için:

```powershell
dotnet user-secrets list --project src/CodeGuardAI.Api
```

Bu komutun çıktısını issue, log, ekran görüntüsü veya dokümana kopyalama. Değeri değiştirmek ya da kaldırmak için:

```powershell
dotnet user-secrets set "Gemini:ApiKey" "<NEW_GEMINI_API_KEY>" --project src/CodeGuardAI.Api
dotnet user-secrets remove "Gemini:ApiKey" --project src/CodeGuardAI.Api
```

## Environment variables

ASP.NET Core nested anahtarlar için çift alt çizgi kullanır. PowerShell oturumu için örnek:

```powershell
$env:Database__ConnectionString = "<LOCAL_POSTGRES_CONNECTION_STRING>"
$env:Gemini__ApiKey = "<YOUR_GEMINI_API_KEY>"
dotnet run --project src/CodeGuardAI.Api
```

Environment variable değerlerini source control'a alınan scriptlere yazma. Production benzeri ortamlarda User Secrets kullanılmaz; süreç/host tarafından sağlanan environment variables veya ileride seçilecek secret store kullanılır.

MCP stdio host yalnız kayıtlı repository kimliğini çözmek ve tool audit kaydı yazmak için database ayarına ihtiyaç duyar; Gemini anahtarı istemez. MCP istemcisi host'u başlatırken `Database__ConnectionString` değerini process environment üzerinden sağlamalıdır:

```powershell
$env:Database__ConnectionString = "<LOCAL_POSTGRES_CONNECTION_STRING>"
dotnet run --project src/CodeGuardAI.McpHost
```

`CodeGuardAI.McpHost` stdout'u MCP protokolüne ayırır; uygulama logları stderr'e gider. MCP client process environment aktarımını destekliyorsa yalnız gereken değerleri allowlist etmek, parent process'in ilgisiz secret'larını miras bırakmaktan daha güvenlidir.

`run_tests` için varsayılan timeout ve output cap aynı strongly typed `TestRunnerOptions` üzerinden yönetilir. MCP input'u bu limitleri override edemez. Gerektiğinde host environment'ında bounded değerler verilebilir:

```powershell
$env:TestRunner__DefaultTimeout = "00:02:00"
$env:TestRunner__MaxOutputCharacters = "32768"
```

## Öncelik

Aynı anahtar birden fazla kaynaktaysa son sağlayıcı kazanır. Bu proje için ilgili düşükten yükseğe sıra:

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. Development ortamında User Secrets
4. Environment variables
5. Command-line arguments

Bu nedenle environment variable, aynı adlı User Secrets veya appsettings değerini override eder. Testler gerçek process secret'larına bağımlı kalmamak için en son in-memory provider ile açık fake değerler kullanır.

## Hata ve güvenlik davranışı

- Eksik zorunlu ayar host'u fail-fast durdurur; `/health` başarılı gösterilmez.
- Hata mesajında yalnız `Database:ConnectionString` veya `Gemini:ApiKey` anahtar adı bulunur; değer bulunmaz.
- Configuration nesnesi, provider listesi veya secret değerleri loglanmaz.
- Startup validation I/O yapmaz ve cancellation davranışının yerine geçmez. İlerideki database/provider I/O çağrıları kendi `CancellationToken` değerini taşımaya devam eder.
- Repository'ye secret girerse User Secrets'a taşımak yeterli değildir; secret revoke/rotate edilmeli ve Git geçmişi ayrıca değerlendirilmelidir.
