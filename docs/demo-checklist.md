# Gün 25 demo checklist

## Başlatma

1. `Database:ConnectionString` ve `Gemini:ApiKey` değerlerini User Secrets veya environment variable ile sağla.
2. `dotnet run --project src/CodeGuardAI.Api` komutunu çalıştır.
3. Terminalde gösterilen local HTTP(S) adresini browser'da aç. Ana sayfa demo UI'yi, `/swagger` API referansını gösterir.

## Ana akış

- Projects alanı önce loading, hata halinde okunabilir API mesajı, veri yoksa empty state gösteriyor.
- Yeni project eklerken repository path'in browser bilgisayarında değil API'nin çalıştığı makinede çözüldüğünü doğrula.
- Project seçildiğinde review formu aktif oluyor; model ve max finding alanları label ile erişilebilir.
- Run review sırasında progress ve Cancel request aksiyonu görünüyor.
- Tamamlanan review included files, skipped entries, included bytes ve finding sayısını gösteriyor.
- Severity ve category filtreleri birlikte çalışıyor; eşleşme yoksa ayrı empty state görünüyor.
- Finding checkbox seçilmeden Generate tests pasif kalıyor.
- Generate tests yalnız kullanıcı butona bastığında çağrılıyor; test çalıştırmıyor ve repository dosyası değiştirmiyor.
- API `ProblemDetails` cevaplarında HTTP status, açıklama ve structured error code kullanıcıya okunabilir biçimde gösteriliyor.

## Erişilebilirlik ve güvenlik

- Klavye ile skip link, project seçimi, bütün form alanları, filtreler ve aksiyonlara ulaşılabiliyor.
- Focus göstergesi görünür; status mesajları `aria-live` ile duyuruluyor.
- Dar viewport'ta layout tek kolona düşüyor.
- Browser UI API key, connection string veya başka secret göstermiyor.
- API'den gelen içerik `textContent` ile render ediliyor; HTML olarak enjekte edilmiyor.
