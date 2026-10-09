# Hotel — Rezervasyon sitesi, yönetim paneli ve kanal (OTA) entegrasyonu

| Katman | Teknoloji |
|---|---|
| Backend | .NET 10, ASP.NET Core Minimal API, EF Core 10, PostgreSQL, ASP.NET Identity (bearer token) |
| Frontend | React 19, TypeScript, Vite, React Router, TanStack Query |
| Kanallar | Booking.com (OTA XML), Expedia (EQC), Channex (kanal yöneticisi, 50+ OTA), iCal (Airbnb, Vrbo…) |

## Hızlı başlangıç (geliştirme)

```bash
# 1) PostgreSQL
docker compose up -d db

# 2) API  → http://localhost:5080  (ilk açılışta migration + örnek veri + admin kullanıcı)
cd backend && dotnet run --project src/Hotel.Api

# 3) Frontend → http://localhost:5173  (/api istekleri 5080'e proxy'lenir)
cd frontend && npm install && npm run dev
```

Geliştirme admin girişi: `admin@hotel.local` / `Admin12345!` (`appsettings.Development.json`).
Yönetim paneli: http://localhost:5173/admin · OpenAPI: http://localhost:5080/openapi/v1.json

Testler: `cd backend && dotnet test` · Tip kontrolü/lint: `cd frontend && npm run typecheck && npm run lint`

Üretim (tek imaj, API React build'ini de sunar):

```bash
ADMIN_PASSWORD='GüçlüBirŞifre123' PUBLIC_URL=https://otel.example.com docker compose up -d --build
```

## Mimari

```
backend/src
  Hotel.Domain          Entity'ler, iş kuralları (Booking durum geçişleri, StayPeriod)
  Hotel.Application     Servisler: Availability, Booking, ARI (fiyat/müsaitlik), ChannelSync, Dashboard
                        IChannelConnector soyutlaması (kanaldan bağımsız modeller)
  Hotel.Infrastructure  EF Core/PostgreSQL, Identity, kanal connector'ları, arka plan worker'ı
  Hotel.Api             Endpoint'ler (public / auth / admin / iCal), hata yönetimi, rate limiting
frontend/src
  pages/public          Ana sayfa, odalar, arama, rezervasyon, onay, "rezervasyonum"
  pages/admin           Dashboard, rezervasyonlar, fiyat & müsaitlik takvimi, odalar, oda tipleri,
                        kanallar (ayar, eşleme, test, log), kullanıcılar
```

### Önemli tasarım kararları
- **Overbooking koruması:** Envanteri değiştiren her işlem bir transaction içinde oda tipi başına
  `pg_advisory_xact_lock` alır. PostgreSQL üzerinde 8 odaya 14 paralel istek → tam 8 rezervasyon, 6×409.
- **Kanaldan gelen rezervasyon asla reddedilmez** (OTA'da zaten satılmış). Envanter yoksa
  `IsOverbooked` ile işaretlenir ve dashboard'da uyarı çıkar.
- **Transactional outbox:** Rezervasyon/fiyat/oda değişikliği aynı transaction'da `AriOutbox`'a yazılır;
  `ChannelSyncWorker` 15 sn'de bir değişen aralığı tüm kanallara gönderir (ardışık aynı günler aralığa sıkıştırılır).
- **Rezervasyon çekme** varsayılan 2 dk'da bir. Eşlenmemiş odaya gelen rezervasyon *onaylanmaz (ack edilmez)*,
  böylece eşleme düzeltilince kanal tekrar gönderir. Aynı rezervasyonun iki kez gelmesi çift kayıt oluşturmaz.
- Kanal şifreleri ASP.NET Data Protection ile şifreli saklanır, UI'a hiç geri dönmez.
  **Üretimde `DataProtection:KeysPath` kalıcı bir volume olmalı** (aksi halde kayıtlı şifreler okunamaz).
- Kanal bazında komisyon % (net gelir raporu) ve fiyat farkı % (o kanala gönderilen fiyatlara eklenir).

## Kanal entegrasyonu — gerçekçi yol haritası

| Kanal | Durum | Canlıya almak için |
|---|---|---|
| **Channex** (önerilen) | Hazır: ARI gönderimi, booking revision feed + ack | Channex hesabı → property oluştur → Booking.com/Expedia/Airbnb/Agoda'yı Channex panelinden bağla → API key'i girip oda/plan ID'lerini eşle. Sandbox: `https://staging.channex.io/api/v1` |
| **iCal** | Hazır, çift yönlü | Airbnb/Vrbo ilanının iCal URL'sini eşlemeye gir; panelde gösterilen dışa aktarma URL'sini ilana ekle. Sadece tarih bloklar, fiyat/misafir bilgisi taşımaz. |
| **Booking.com doğrudan** | OTA XML connector yazıldı (HotelAvailNotif, RateAmountNotif, ResNotif, ResModifyNotif) | Booking.com **Connectivity Partner** başvurusu + sertifikasyon gerekir; tek otel için genelde verilmez. Sertifikasyon sırasında mesajlar Booking.com dokümanına göre doğrulanmalı. |
| **Expedia doğrudan** | EQC connector yazıldı (AR, BR, BC) | Expedia connectivity onayı + EQC kimlik bilgileri gerekir. |

Tek bir otel için öneri: **Channex (veya HotelRunner/SiteMinder gibi bir kanal yöneticisi) + iCal**.
Yeni bir kanal yöneticisi eklemek = `IChannelConnector` implementasyonu + DI kaydı; iş kuralları değişmez.

## Claude Design ile tasarım — teslim notları
Arayüz bilerek sade bırakıldı; tüm görsel karar iki dosyada:
- `frontend/src/styles/tokens.css` — renk, tipografi, boşluk, radius, gölge (açık/koyu tema)
- `frontend/src/styles/base.css` — layout ve bileşen sınıfları (`.card`, `.btn--primary`, `.ari`, `.admin__nav`…)

UI primitifleri `frontend/src/components/ui/index.tsx` içinde (Button, Card, Field, Badge, Modal…), sayfalar
sadece bunları kullanır. Tasarımı bu dosyalara uygulamak sayfa mantığına dokunmadan tüm siteyi değiştirir.
Metinler `src/lib/labels.ts` ve sayfalarda (Türkçe); çoklu dil gerekirse i18n katmanı eklenebilir.

## Sonraki adımlar (henüz yok)
- Online ödeme (iyzico / PayTR / Stripe) — şu an "otelde ödeme"
- E-posta/SMS bildirimleri (`INotificationService` şu an sadece log yazıyor)
- Oda fotoğrafı yükleme (şu an URL), çoklu dil, KVKK metinleri
- Çok instance'lı dağıtımda worker'ın tek instance'ta çalışması (`ChannelSync:Enabled`) veya Hangfire/Quartz
