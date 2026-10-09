# Demo verileri — tasarım için

Kurgusal otel: **Lavanta Butik Otel, Kaş** (16 oda, 3 oda tipi). Bugün = **9 Ekim 2026**.
Tüm dosyalar gerçek API'nin döndüreceği yapıyla birebir aynıdır; tasarım onaylanınca sahte veri yerine API bağlanır.
Görseller `picsum.photos` yer tutucularıdır, gerçek fotoğraflarla değiştirilmeli. Para birimi TRY.

## Web sitesi (misafir tarafı)

| Ekran | Dosya | Not |
|---|---|---|
| Ana sayfa (hero, arama kutusu, odalar, olanaklar, iletişim) | `hotel.json`, `room-types.json` | `fromPrice` = en ucuz plan gecelik fiyatı |
| Odalar sayfası | `room-types.json` | |
| Arama sonuçları | `availability-search.json` | 3 gece, hafta sonu %20 pahalı; Suit'te "Son 1 oda" durumu |
| Sonuç yok durumu | `availability-search-empty.json` | |
| Rezervasyon formu (özet kutusu) | `availability-search.json` içindeki seçilen `rates[].nights` | Gecelik döküm + toplam |
| Onay sayfası / Rezervasyonum | `booking-confirmation.json` | `canCancel` true ise iptal butonu |

## Yönetim paneli

| Ekran | Dosya | Not |
|---|---|---|
| Giriş yapan kullanıcı (menü altı) | `admin/me.json` | |
| Gösterge paneli | `admin/dashboard.json` | 3 giriş, 2 çıkış, 4 konaklayan, 2 bekleyen, 1 overbooking uyarısı, 30 günlük doluluk, kaynağa göre ciro, kanal durumu (1 kanal hatalı) |
| Rezervasyon listesi | `admin/bookings.json` (sayfa 1/2), tümü: `admin/bookings-all.json` | 35 kayıt; tüm durumlar (Beklemede, Onaylı, Konaklıyor, Çıkış yaptı, İptal, Gelmedi) ve kaynaklar (Web, Booking.com, Expedia, Airbnb, Agoda, Resepsiyon) |
| Rezervasyon detayı | `admin/booking-detail.json` | Booking.com'dan gelen, check-in yapılmış örnek |
| Fiyat & müsaitlik takvimi | `admin/ari-calendar.json` | 21 gün; satışa kapalı günler (`stopSell`), girişe kapalı (CTA), min. konaklama, kısıtlı envanter (`hasOverride`), dolu günler |
| Odalar | `admin/rooms.json` | Bakımda oda, kirli/kontrol edildi durumları, odadaki misafir |
| Oda tipleri & fiyat planları | `admin/room-types.json` | Her tipte 3 plan (Kahvaltılı, İade edilemez, Yarım pansiyon) |
| Kanallar listesi | `admin/channels.json` | Channex (aktif), Airbnb iCal (aktif), Booking.com doğrudan (kapalı, hata mesajlı) |
| Kanal ekle (sağlayıcı kartları) | `admin/channel-providers.json` | |
| Kanal detayı (ayarlar, eşleme, loglar) | `admin/channel-detail.json`, `admin/channel-logs.json` | Eşleme tablosu `admin/room-types.json` ile birleştirilir |
| Kullanıcılar | `admin/users.json` | 1 kilitli kullanıcı |

## Alan değerleri (etiketler)

- **status:** Pending=Beklemede, Confirmed=Onaylı, CheckedIn=Konaklıyor, CheckedOut=Çıkış yaptı, Cancelled=İptal, NoShow=Gelmedi
- **paymentStatus:** Unpaid=Ödenmedi, PartiallyPaid=Kısmi ödeme, Paid=Ödendi, Refunded=İade, PaidToChannel=Kanala ödendi
- **source:** Direct=Web sitesi, Admin=Resepsiyon, BookingCom, Expedia, Airbnb, Agoda
- **mealPlan:** RoomOnly=Sadece oda, BedAndBreakfast=Oda+kahvaltı, HalfBoard=Yarım pansiyon
- **housekeeping:** Clean=Temiz, Dirty=Kirli, Inspected=Kontrol edildi · **room status:** Available, Maintenance=Bakımda, OutOfOrder=Arızalı
