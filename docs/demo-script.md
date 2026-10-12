# Beş dakikalık demo script'i

## Ön hazırlık

- PostgreSQL servisi çalışıyor ve migration'lar güncel.
- API User Secrets içinde `Database:ConnectionString` ve `Gemini:ApiKey` tanımlı.
- `dotnet run --project src/CodeGuardAI.Api --launch-profile http` çalışıyor.
- `sample-repository` klasörünün tam Windows yolu hazır.
- Browser'ın bir sekmesi `http://localhost:5253`, ikinci sekmesi `http://localhost:5253/swagger` adresinde; terminal ve secret ekranları kapalı.

Demo öncesi hızlı kontrol:

```powershell
dotnet test CodeGuardAI.sln --configuration Release --no-build --filter "Category!=Database"
```

## 0:00-0:40 — Problem ve sınır

“Bu uygulama local C# repository'sini Gemini ile inceliyor; fakat modele filesystem veya shell yetkisi vermiyor. Review sabit adımlı workflow, dosya okuma ve test çalıştırma ise güvenli adapter'lar üzerinden ilerliyor.”

Ana sayfada repository path'in server-local olduğunu ve secret gösterilmediğini belirt.

## 0:40-1:10 — Project kaydı

1. Project name alanına `CodeGuard Sample` yaz.
2. Repository path alanına `sample-repository` klasörünün tam yolunu yapıştır.
3. Create project'e bas ve listede seçili hale geldiğini göster.

Beklenen: duplicate normalized path ikinci kez oluşturulursa okunabilir `409` mesajı gelir.

## 1:10-1:35 — Root-bound scan

1. `Scan repository` butonuna bas.
2. Included/skipped/byte özetini ve skip reason dağılımını göster.
3. Scan isteğinin browser'dan path almadığını; seçili Project'in normalize edilmiş server-local root'unu kullandığını belirt.

## 1:35-2:25 — Review Agent

1. Demo öncesi erişilebilirliği doğrulanmış model adını ve max finding alanlarını göster. 12 Ekim 2026 canlı doğrulamasında `gemini-3.8-flash` kullanıldı; provider kataloğu değişebilir.
2. Run review'e bas.
3. Progress/cancel durumunu göster.
4. Tamamlandığında included files, skipped entries, included bytes ve finding sayısını açıkla.

Beklenen: bulgular repository-relative file ve geçerli line aralığı taşır; parser/grounding dışındaki provider metni kabul edilmez.

Provider geçici olarak erişilemiyorsa hata state'ini göster; sonucu uydurma veya completed gibi sunma.

## 2:25-3:00 — History, filtreler ve failure contract

1. Review history'den önceki bir run'ı seçip persisted sonucun yeniden açıldığını göster.
2. Severity ve category filtrelerini değiştir.
3. Eşleşmeyen kombinasyonda empty state'i göster.
4. Bir finding kartında file/line, reason, suggestion ve confidence alanlarını göster.

API hatalarının raw exception yerine HTTP status, açıklama ve stable structured code olarak gösterildiğini belirt.

## 3:00-3:35 — Explicit Test Agent

1. Bir veya daha fazla finding seç.
2. `Generate tests` butonunun ancak seçimden sonra aktif olduğunu göster.
3. Butona bas ve öneri kartlarını göster.

“Bu aksiyon test kodunu repository'ye yazmıyor ve test çalıştırmıyor; yalnız kullanıcı talebiyle öneri oluşturuyor.”

## 3:35-4:30 — Docs Agent ve Swagger

1. Swagger sekmesinde `POST /api/reviews/{reviewId}/documentation` operation'ını aç.
2. Completed review kimliğini route'a, aşağıdaki bounded body'yi request'e gir:

```json
{
  "model": "gemini-3.8-flash",
  "timeoutSeconds": 120
}
```

3. Execute ile ilk üretimin `201 Created`, `Location: /api/reviews/{reviewId}/documentation` ve `markdownContent` döndürdüğünü göster.
4. Aynı path'in GET operation'ını çalıştırarak raporun PostgreSQL'den tekrar okunduğunu göster.
5. POST'u ikinci kez çalıştırırsan `409 documentation.already_generated` beklendiğini açıkla; canlı demoda duplicate çağrı yapmak opsiyoneldir.

“Docs Agent repository'yi yeniden okumuyor. Completed ReviewRun, grounded Finding kayıtları ve varsa TestCase önerileriyle strict JSON üretip canonical alanları server-side Markdown renderer'dan geçiriyor.”

Test önerisi oluşturulmamış bir completed review kullanıldığında rapor yine üretilir ve test önerisi bölümünde persisted kayıt olmadığı açıkça yazılır.

## 4:30-5:00 — MCP, güvenlik ve kanıt

- MCP host'un stdio üzerinden yalnız `read_file` ve `run_tests` sunduğunu söyle.
- Root path/shell command/timeout değerlerinin model girdisi olmadığını belirt.
- CI badge ve test sayılarını göster: offline suite dış servis ve secret olmadan çalışır; PostgreSQL testi local-only'dir.
- Limitations bölümünden local auth yokluğu ve trusted-repository varsayımını açıkça söyle.

Canlı Gemini çağrısı başarısızsa status ve stable `ProblemDetails.code` değerini göster; response uydurma, API key'i Swagger body/header'a yazma veya ekran görüntüsüne alma. Key yalnız User Secrets üzerinden server-side yüklenir.

## Görsel çekim sırası

1. `01-dashboard-projects.png`: project listesi ve server-local path açıklaması.
2. `02-review-results.png`: scan özeti, finding kartları ve severity/category filtreleri.
3. `03-test-suggestions.png`: seçilmiş finding ve explicit test önerileri.
4. `04-docs-report-swagger.png`: documentation `201` response ve Markdown alanı; kimlikler dışında hassas veri yok.
5. `05-ci-green.png`: commit SHA'sı görünen yeşil GitHub Actions run'ı.

Kırpımda browser adresi/proje adı kalabilir; terminal, local kullanıcı adı, tam kişisel path, User Secrets, API key ve connection string görünmemelidir. Görseller `screenshots/` altında tutulur; güncel olmayan görüntü README'den kaldırılır.
