# Test ve CI stratejisi

## CI sözleşmesi

`.github/workflows/ci.yml`, her push ve pull request için temiz bir Ubuntu runner üzerinde aşağıdaki sıralı sözleşmeyi çalıştırır:

1. Repository checkout edilir.
2. Repository kökündeki `global.json` ile aynı .NET SDK sürümü olan `10.0.103` kurulur.
3. `dotnet restore CodeGuardAI.sln` bağımlılıkları geri yükler.
4. `dotnet build CodeGuardAI.sln --configuration Release --no-restore` restore çıktısını yeniden kullanarak derler.
5. Unit/eval ve API/integration projeleri, derlenmiş çıktıda ayrı `dotnet test --no-build --filter "Category!=Database"` adımlarıyla çalışır. Ayrı adımlar kırılan test sınırını GitHub job özetinde görünür tutar.

Adımların hiçbirinde `continue-on-error` yoktur. Restore, build veya test sıfır dışı exit code döndürürse job ve workflow başarısız olur; sonraki adım çalışmaz. GitHub job cancellation, çalışan `dotnet` sürecini sonlandırır. Job için 15 dakikalık üst sınır vardır.

Workflow yalnız `contents: read` izni ister. Docker, service container, PostgreSQL servisi, repository secret veya Gemini API anahtarı kullanmaz. Normal CI suite'indeki LLM davranışı fake provider/HTTP handler ile deterministik ve ağsız doğrulanır; gerçek Gemini çağrısı yapılmaz.

Bu aşamada test sonucu artifact'i ve NuGet cache'i eklenmemiştir. Suite küçük olduğu için varsayılan konsol çıktısı yeterli teşhis sağlar; ek depolama ve cache invalidation yüzeyi minimum pipeline hedefini gereksiz yere büyütür.

## Yerel offline doğrulama

CI ile aynı doğrulamayı yerelde çalıştırmak için:

```powershell
dotnet restore CodeGuardAI.sln
dotnet build CodeGuardAI.sln --configuration Release --no-restore
dotnet test tests/CodeGuardAI.UnitTests --configuration Release --no-build --filter "Category!=Database"
dotnet test tests/CodeGuardAI.IntegrationTests --configuration Release --no-build --filter "Category!=Database"
```

Bu komut Unit, API/integration, fake LLM ve `Category=Eval` testlerini kapsar. `Category=Database` dışındaki testlerin dış servise veya geliştirici secret'ına ihtiyaç duymaması gerekir.

## PostgreSQL testleri: local-only

Gerçek ilişkisel davranışı kanıtlayan testler `[Trait("Category", "Database")]` ile ayrılır ve hosted CI'da çalıştırılmaz. Bu testler yalnız geliştiricinin açıkça hazırladığı yerel PostgreSQL test veritabanına karşı koşar:

```powershell
$env:CODEGUARD_TEST_DATABASE_CONNECTION_STRING = "Host=localhost;Port=5432;Database=codeguard_test;Username=codeguard_test;Password=<TEST_PASSWORD>;Include Error Detail=false"
dotnet test tests/CodeGuardAI.IntegrationTests --filter "Category=Database"
```

Güvenlik sınırları:

- Veritabanı adı `_test` ile bitmek zorundadır.
- Connection string yoksa database testi gerekçesiyle skip edilir; fake provider'a sessizce düşmez.
- Development veritabanı database test hedefi olarak kullanılmaz.
- Connection string, test çıktısına veya repository dosyalarına yazılmaz.
- Ayrıntılı rol, migration ve temizleme adımları için `docs/setup/postgresql-windows.md` izlenir.

Test tamamlandığında process-level secret temizlenir:

```powershell
Remove-Item Env:CODEGUARD_TEST_DATABASE_CONNECTION_STRING -ErrorAction SilentlyContinue
```

## Neden bu sınır var?

GitHub Actions burada orkestrasyon katmanıdır; kaynak kodunu checkout eder ve `dotnet` CLI exit code'larını kalite kapısı olarak yorumlar. Derleme ve test mantığı workflow içine taşınmaz. `dotnet` CLI ise aynı solution ve test filtrelerini hem yerelde hem CI'da çalıştıran taşınabilir giriş sözleşmesidir.

Container kullanmama kararı CI'ı küçük ve secretsiz tutar, ancak gerçek PostgreSQL migration/FK/index davranışının hosted runner tarafından kanıtlanmadığı anlamına gelir. Bu risk, açık `Category=Database` etiketi ve ayrı local-only doğrulama prosedürüyle görünür tutulur.
