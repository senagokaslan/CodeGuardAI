# Screenshot ve GIF planı

Portföy için hedef 3 ürün ekranı + 1 CI kanıtıdır:

| Dosya | İçerik | Kanıtladığı şey | Hazırlık |
|---|---|---|---|
| `01-dashboard-projects.png` | Project create/list ve server-local path notu | Frameworksüz UI, loading/empty/error contract, accessible labels | Sample project kayıtlı |
| `02-review-results.png` | Scan summary, finding kartları, severity/category filtreleri | Bounded scan ve yapılandırılmış review sonucu | Başarılı sample review |
| `03-test-suggestions.png` | Finding seçimi ve suggestion kartları | Test generation'ın ayrı explicit aksiyon olması | En az bir finding seçili |
| `04-ci-green.png` | GitHub Actions run özeti ve commit | Secretsiz restore-build-offline-test kapısı | `main` workflow başarılı |

Alternatif kısa GIF, yalnız `01 -> 02 -> 03` akışını 20-30 saniyede göstermelidir. GIF ve PNG birlikte tutulacaksa README'de tek format seçilir; aynı içeriğin iki kopyası gösterilmez.

## Görsel güvenlik kontrolü

- Terminal/User Secrets/pgAdmin parola alanı görünmüyor.
- Gemini API key ve PostgreSQL connection string görünmüyor.
- Kişisel home path'i kırpılmış veya sample path ile değiştirilmiş.
- Gerçek üçüncü taraf repository içeriği yerine `sample-repository` kullanılmış.
- UI sonucu gerçekten çalıştırılmış akıştan geliyor; statik mock completed gibi sunulmuyor.
- CI görüntüsü yeşil olmayan veya farklı commit'e ait run göstermiyor.

Görseller çekilene kadar README bu plan belgesine bağlanır; boş/uydurulmuş ekran görüntüsü eklenmez.
