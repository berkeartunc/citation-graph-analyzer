# Citation Graph Analyzer

[English](README.md)

Bilimsel makalelerden oluşan bir JSON dosyasından atıf grafı kuran, bu grafı analiz eden ve etkileşimli olarak çizen bir Windows masaüstü uygulaması (C#, WPF). Graf algoritmaları ve JSON okuyucu, herhangi bir harici kütüphane kullanmadan sıfırdan yazılmıştır.

## Neler yapıyor

- **Makaleleri JSON'dan okur.** Bunun için elle yazılmış küçük bir JSON okuyucu kullanır. Farklı anahtar adlarını kabul eder (örneğin `year` veya `publication_year`) ve kullanmadığı alanları atlar.
- **Yönlü graf kurar:** her makale bir düğümdür ve `A -> B`, A makalesinin B makalesine atıf yaptığı anlamına gelir. Veri setinde olmayan makalelere yapılan atıflar, tekrar eden atıflar ve makalenin kendine yaptığı atıflar yok sayılır. Ayrıca sıralama kenarları makaleleri id sırasına göre birbirine bağlar.
- Bir makalenin **h-index, h-core ve h-median** değerlerini, o makaleye atıf yapan makalelerden hesaplar.
- Her düğümün **betweenness centrality** değerini Brandes algoritmasıyla hesaplar. Hesaplama arka planda çalışır, pencere donmaz.
- **k-core ayrıştırması:** her düğümün en az k komşusu olduğu alt grafı vurgular.
- **Etkileşimli görünüm:** yakınlaştırma, kaydırma, üzerine gelince ayrıntılar, tıklayınca makalenin h-core'unu ekleme ve seçili makaleye atıf yapanlardan büyüyen bir ağ gösteren odak modu (çift tık).

## Veri seti

`data/articles.json`, bilimsel çalışmaların açık kataloğu olan [OpenAlex](https://openalex.org)'ten alınmış 1.000 bilgisayarlı görü makalesi içerir (1971-2025, CC0 lisansı).

| Ölçüt | Değer |
|---|---|
| Makale | 1.000 |
| Atıf kenarı | 1.580 (1.590 atıf, makalenin kendine yaptığı 10 atıf yok sayıldı) |
| En çok atıf alan makale | *Very Deep Convolutional Networks for Large-Scale Image Recognition* (2014), 58 atıf, h-index 5 |
| En yüksek h-index | 6, *Improving quality inspection of food products by computer vision - a review* (2003) |
| En yüksek betweenness | aynı 2014 makalesi |
| En büyük k-core | k = 6, 16 makale |

Sıralama kenarları tüm makaleleri tek bir zincirde birleştirir. Bu yüzden betweenness ve k-core için kullanılan yönsüz graf her zaman bağlıdır. Bu durum, yalnızca atıf kenarlarından oluşan bir grafa göre değerleri yükseltir.

## h-index burada nasıl hesaplanıyor

Bir T makalesi için, T'ye atıf yapan makaleler alınır ve her birinin kaç atıf aldığına göre sıralanır. h, bu makalelerden h tanesinin en az h atıf aldığı en büyük sayıdır. h-core bu h makaledir, h-median ise bu makalelerin atıf sayılarının ortancasıdır.

## Proje yapısı

```
src/
├── CitationGraph.Core/          sınıf kütüphanesi (.NET 8, arayüz yok, her işletim sisteminde çalışır)
│   ├── Article.cs               veri modeli
│   ├── ArticleJsonReader.cs     makaleleri JSON'dan okur
│   ├── MiniJsonReader.cs        elle yazılmış JSON okuyucu
│   ├── Graph.cs                 komşuluk listeleri, atıf sayıları, id ile arama
│   ├── GraphBuilder.cs          makalelerden grafı kurar
│   ├── GraphAnalytics.cs        h-index, betweenness, k-core
│   └── HIndexResult.cs
└── CitationGraphAnalyzer/       WPF uygulaması (Windows)
    ├── MainWindow.xaml(.cs)     kontroller ve istatistik paneli
    └── GraphView.cs             grafı çizen özel kontrol
tests/CitationGraph.Core.Tests/  xUnit testleri
data/articles.json               veri seti
```

Analiz kodu, arayüz kodu içermeyen ayrı bir kütüphanededir. Böylece tek başına test edilebilir ve farklı bir arayüzle yeniden kullanılabilir.

## Başlarken

Gereksinimler: Windows ve .NET 8 SDK (veya Visual Studio 2022).

1. `CitationGraphAnalyzer.sln` dosyasını Visual Studio'da açıp `CitationGraphAnalyzer` projesini çalıştırın ya da şu komutu kullanın:
   ```
   dotnet run --project src/CitationGraphAnalyzer
   ```
2. **JSON Yükle** düğmesine basıp `data/articles.json` dosyasını seçin.
3. Bir makale id'si girin (örneğin `W1686810756`) ve h-core'unu görmek için **Göster / Baştan Çiz** düğmesine basın. Genişletmek için düğümlere tıklayın, odak moduna girmek için çift tıklayın, çıkmak için ESC'ye basın.

Arayüz dili Türkçedir.

## Testler

```
dotnet test tests/CitationGraph.Core.Tests
```

30 xUnit testi graf kurucuyu, h-index'i, betweenness'ı, k-core'u ve JSON okuyucuyu (kaçış karakterleri, yanlış tipler, bozuk girdi) kapsar. Bazı testler gerçek veri seti üzerinde çalışır ve beklenen değerler Python'daki NetworkX kütüphanesiyle ayrıca doğrulanmıştır. Testler yalnızca çekirdek kütüphaneyi kullandığı için Linux ve macOS'ta da çalışır.

## Kullanılan teknolojiler

C# 12, .NET 8, WPF, xUnit
