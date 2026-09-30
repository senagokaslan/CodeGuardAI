# Windows PostgreSQL ve migration kurulumu

Bu rehber CodeGuard AI için yerel development ve test veritabanlarını Docker kullanmadan hazırlar. Komutlar repository kökünden PowerShell ile çalıştırılır. Parola ve connection string değerlerini repository dosyalarına, terminal çıktısına veya Git geçmişine ekleme.

## 1. Ön koşullar

1. PostgreSQL'in desteklenen Windows sürümünü resmi installer ile kur. Kurulumda `Command Line Tools` bileşenini seç.
2. Yeni bir PowerShell aç ve PostgreSQL servisinin çalıştığını doğrula:

```powershell
Get-Service -Name "*postgres*"
psql --version
```

`psql` bulunamazsa kurulu sürümün `bin` dizinini yalnız kendi makinenin `PATH` değerine ekle. Örnek dizin: `C:\Program Files\PostgreSQL\<VERSION>\bin`.

3. Repository-local EF aracını yükle ve sürümü doğrula:

```powershell
dotnet tool restore
dotnet ef --version
```

Manifest EF CLI `10.0.4` sürümünü sabitler. Bu sayede geliştiriciler aynı migration tooling sürümünü kullanır.

## 2. Least-privilege local roller ve veritabanları

Bootstrap sırasında yalnız bu bölüm için kurulumda oluşturulan PostgreSQL yönetici rolünü kullan:

```powershell
psql -U postgres -d postgres
```

`psql` içinde development ve test için ayrı login/database oluştur. `\password` parolayı interaktif aldığı için shell history veya bu belgeye yazmaz:

```sql
CREATE ROLE codeguard_dev LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
\password codeguard_dev
CREATE DATABASE codeguard_dev OWNER codeguard_dev;

CREATE ROLE codeguard_test LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
\password codeguard_test
CREATE DATABASE codeguard_test OWNER codeguard_test;

\q
```

Bu roller cluster yönetemez ve başka rol oluşturamaz. Kendi veritabanlarının sahibi olmaları, EF migration'ın tablo/index/FK oluşturabilmesi için gereken yerel yetki sınırıdır. Uygulamayı `postgres` superuser ile çalıştırma.

## 3. Development connection string

Development değerini User Secrets'a yaz. Yer tutucuyu yerel parolanla değiştir; gerçek değeri commit etme:

```powershell
dotnet user-secrets set "Database:ConnectionString" "Host=localhost;Port=5432;Database=codeguard_dev;Username=codeguard_dev;Password=<DEV_PASSWORD>;Include Error Detail=false" --project src/CodeGuardAI.Api
```

API startup validation nedeniyle development ortamında `Gemini:ApiKey` de tanımlı olmalıdır. Ayrıntılar için `docs/setup/configuration.md` belgesini uygula. `dotnet user-secrets list` çıktısını log, issue veya dokümana kopyalama.

## 4. Migration listeleme, uygulama ve doğrulama

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet ef migrations list --project src/CodeGuardAI.Infrastructure --startup-project src/CodeGuardAI.Api
dotnet ef database update --project src/CodeGuardAI.Infrastructure --startup-project src/CodeGuardAI.Api
```

Başarılı apply sonrasında development rolüyle bağlan:

```powershell
psql -U codeguard_dev -d codeguard_dev -h localhost
```

Altı uygulama tablosunu doğrula:

```sql
SELECT tablename
FROM pg_catalog.pg_tables
WHERE schemaname = 'public'
  AND tablename IN ('projects', 'review_runs', 'findings', 'test_cases', 'ai_model_runs', 'tool_executions')
ORDER BY tablename;
```

Sonuç altı satır olmalıdır. Unique index ve enumların string kolonlarda tutulduğunu doğrula:

```sql
SELECT indexname, indexdef
FROM pg_catalog.pg_indexes
WHERE schemaname = 'public'
  AND indexname = 'ux_projects_normalized_root_path';

SELECT table_name, column_name, data_type, character_maximum_length
FROM information_schema.columns
WHERE table_schema = 'public'
  AND column_name IN ('status', 'severity', 'category', 'purpose', 'type')
ORDER BY table_name, column_name;
```

`ux_projects_normalized_root_path` unique olmalı; enum kolonları `character varying` olarak görünmelidir.

## 5. SQL scriptini üretme ve inceleme

Scripti repository dışında geçici dizine üret:

```powershell
$scriptPath = Join-Path $env:TEMP "codeguard-initial-create.sql"
dotnet ef migrations script --idempotent --project src/CodeGuardAI.Infrastructure --startup-project src/CodeGuardAI.Api --output $scriptPath
Get-Content -LiteralPath $scriptPath
```

Script incelemesinde şunlar bulunmalıdır:

- Altı `CREATE TABLE` ve EF migration history tablosu
- Project → ReviewRun ve ReviewRun → dört çocuk için toplam beş `ON DELETE CASCADE`
- `ux_projects_normalized_root_path` unique indexi
- Enum alanları için `character varying(32|64)` kolonları
- `confidence` için `numeric(5,4)` ve `scan_summary_json` için `jsonb`

İnceleme bittikten sonra geçici script silinebilir:

```powershell
Remove-Item -LiteralPath $scriptPath
```

## 6. Rollback ve yeniden apply doğrulaması

Bu komutlar local development verilerini siler. Yalnız `codeguard_dev` hedefini doğruladıktan sonra çalıştır:

```powershell
dotnet ef database update 0 --project src/CodeGuardAI.Infrastructure --startup-project src/CodeGuardAI.Api
dotnet ef database update --project src/CodeGuardAI.Infrastructure --startup-project src/CodeGuardAI.Api
```

İlk komut `InitialCreate` içindeki tabloları geri alır; ikinci komut boş veritabanını yeniden güncel şemaya getirir. Her komuttan sonra migration listesi ve altı tablo sorgusu yeniden çalıştırılmalıdır.

## 7. Ayrı test database ve temizleme

Test connection string'i User Secrets'taki development değerinden ayrı tut ve yalnız geçerli PowerShell oturumuna ver:

```powershell
$env:CODEGUARD_TEST_DATABASE_CONNECTION_STRING = "Host=localhost;Port=5432;Database=codeguard_test;Username=codeguard_test;Password=<TEST_PASSWORD>;Include Error Detail=false"
$env:Database__ConnectionString = $env:CODEGUARD_TEST_DATABASE_CONNECTION_STRING
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet ef database update --project src/CodeGuardAI.Infrastructure --startup-project src/CodeGuardAI.Api --connection $env:CODEGUARD_TEST_DATABASE_CONNECTION_STRING
```

Deterministik integration test başlangıcı için test database sahibiyle yalnız `public` şemasını sıfırla; development database'e karşı çalıştırma:

```powershell
psql -h localhost -U codeguard_test -d codeguard_test -v ON_ERROR_STOP=1 -c "DROP SCHEMA public CASCADE; CREATE SCHEMA public;"
dotnet ef database update --project src/CodeGuardAI.Infrastructure --startup-project src/CodeGuardAI.Api --connection $env:CODEGUARD_TEST_DATABASE_CONNECTION_STRING
```

İş bitince process-level secret'ları temizle:

```powershell
Remove-Item Env:CODEGUARD_TEST_DATABASE_CONNECTION_STRING -ErrorAction SilentlyContinue
Remove-Item Env:Database__ConnectionString -ErrorAction SilentlyContinue
Remove-Item Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
```

## Hata ve cancellation davranışı

- EF CLI sıfır dışı exit code döndürürse adımı başarısız kabul et; bir sonraki migration'a geçme.
- PostgreSQL migration'ı transaction içinde uygular. Komut iptal edilir veya bağlantı kesilirse `migrations list` ve tablo sorgularıyla gerçek durumu doğrulamadan yeniden deneme yapma.
- `Ctrl+C` komutu iptal eder; iptal sonrası connection string veya ayrıntılı provider hatasını issue/log içine kopyalama.
- `Include Error Detail=false` kullan; `EnableSensitiveDataLogging` açma.
- Uygulama kapanması veya cancellation, migration rollback komutu değildir. Şema geri alma yalnız hedef migration açıkça seçilerek yapılır.
