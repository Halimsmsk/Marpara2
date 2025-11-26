# Kraftvaerk Umbraco Block Filter Kullanýmý

Bu proje Kraftvaerk.Umbraco.Blockfilter paketini kullanarak Umbraco Block Grid blok kataloðunu dinamik olarak filtreler.

## ?? Nasýl Çalýþýr

### 1. Paket Kurulumu
Paket zaten projeye eklenmiþ:
```xml
<PackageReference Include="Kraftvaerk.Umbraco.Blockfilter" Version="1.1.4" />
```

### 2. Handler Kaydý
`BlockFilterNotificationHandler` otomatik olarak `BlockGridValidationComposer` tarafýndan kaydedilir.

### 3. Filtreleme Mantýðý

#### ?? Kullanýcý Grubu Bazlý Filtreleme
- **Admin Olmayan Kullanýcýlar**: `staticPage`, `staticSubPage`, `questionsPage` bloklarý gizlenir
- **Temel Kullanýcýlar**: `representationMap` gibi geliþmiþ bloklar gizlenir

#### ?? Content Type Bazlý Filtreleme
- **Ana Sayfa**: Sadece `representationList`, `representationMap`, `campingSlider`, `blogFeatured`
- **Blog Sayfalarý**: Sadece `blogList`, `blogFeatured`
- **Kampanya Sayfalarý**: Sadece `campingList`, `campingSlider`
- **Temsilcilik Sayfalarý**: Sadece `representationList`, `representationMap`
- **Soru Sayfalarý**: Sadece `questionsPage`

#### ?? Münhasýr Blok Filtreleme
- Münhasýr bloklar varsa (`questionsPage`, `blogList`, `staticPage`, vb.) diðer bloklar gizlenir
- Debug bloklarý production ortamýnda otomatik gizlenir

## ?? Konfigürasyon

### Yeni Blok Türü Ekleme
`BlockFilterNotificationHandler.cs` dosyasýnda ilgili listlere yeni blok türlerini ekleyin:

```csharp
// Admin sadece bloklar
var adminOnlyBlocks = new List<string>
{
    "staticPage",
    "staticSubPage",
    "questionsPage",
    "yeniAdminBlok" // ? Yeni eklenen
};
```

### Content Type Kurallarý Ekleme
```csharp
case "yeniContentType":
    var yeniAllowedBlocks = new List<string>
    {
        "blok1",
        "blok2"
    };
    // Filtreleme mantýðý...
    break;
```

## ?? Debug ve Monitoring

Handler tüm filtreleme iþlemlerini loglar:
- Kullanýcý bilgileri
- Content type bilgisi
- Filtrelenen blok sayýlarý
- Uygulanan filtre türleri

### Log Seviyeleri
- `LogInformation`: Genel iþlem bilgileri
- `LogDebug`: Detaylý filtreleme bilgileri

## ??? Geliþtirme Ýpuçlarý

### Mevcut Bloklarý Kontrol Etme
`GetCurrentContentBlocks()` metodu geliþtirilmek üzere býrakýlmýþtýr. Bu metod ile þu anki content'te bulunan bloklar alýnabilir ve ona göre filtre uygulanabilir.

### Custom Filter Kurallarý
`ApplySpecialFilters()` metodunda özel filtre kurallarý eklenebilir:

```csharp
// Örnek: Pazartesi günleri belirli bloklarý gizle
if (DateTime.Now.DayOfWeek == DayOfWeek.Monday)
{
    notification.Model.Blocks = notification.Model.Blocks
        .Where(b => b.Alias != "weekendOnlyBlock")
        .ToList();
}
```

## ?? Ýlgili Dosyalar
- **Handler**: `Notifications/BlockFilterNotificationHandler.cs`
- **Composer**: `Composers/BlockGridValidationComposer.cs`
- **Validation**: `Notifications/BlockGridValidationHandler.cs` (kaydetme sýrasýnda validasyon)

## ?? Daha Fazla Bilgi
- [Kraftvaerk Block Filter GitHub](https://github.com/kraftvaerk/umbraco-blockfilter)
- [Umbraco Notifications Documentation](https://docs.umbraco.com/umbraco-cms/extending/notifications)