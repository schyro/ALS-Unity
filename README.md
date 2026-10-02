# ALS-Unity

Unreal Engine için yazılmış [ALS-Community](https://github.com/PanicPetal/ALS-Community) (Advanced Locomotion
System V4'ün C++ topluluk sürümü) **referans alınarak geliştirilen bağımsız bir Unity prototipi**. Hareket,
karakter dönüşü, kamera, mantle ve animasyon mantığı ALS-Community kaynak kodu incelenerek Unity'ye yeniden
yazıldı; Unreal içeriği (mannequin, animasyonlar, blueprint'ler) kullanılmadı. Resmi bir port değildir ve
ALS / Epic Games ile bir bağı yoktur.

Tek oyunculu, oynanabilir bir üçüncü şahıs karakteri ve küçük bir test sahnesi içerir.

## Unity sürümü

- Unity **6000.3.12f1** (Unity 6.3), Universal Render Pipeline
- Input System 1.14 (Active Input Handling: yalnızca yeni Input System)
- Ek paket ya da Asset Store içeriği gerekmez.

## Kurulum

Model ve doku dosyaları Git LFS ile tutulur.

```bash
git lfs install
```

```bash
git clone https://github.com/schyro/ALS-Unity.git
```

Klasörü Unity Hub ile 6000.3.12f1 sürümünde açın. İlk açılışta içe aktarma birkaç dakika sürebilir.

`Packages/manifest.json` içindeki `com.unity.ai.assistant`, `com.unity.ai.inference` ve `com.unity.pipeline`
paketleri yalnızca geliştirme sırasında editör otomasyonu için kullanıldı; prototip bunlara bağlı değildir,
sorun çıkarırlarsa kaldırılabilir.

## Çalıştırma

1. `Assets/ALS/Scenes/ALS_TestScene.unity` sahnesini açın (Build Settings'teki tek sahne budur).
2. Play'e basın. Fare, Game penceresine tıklayınca kilitlenir; `Esc` serbest bırakır.

Sahne, prefab ve Animator Controller koddan üretilir. Bir şeyi değiştirdikten sonra menüden
**ALS > Rebuild Everything** ile hepsi yeniden kurulabilir (`ALS > Build Steps` altında adımlar tek tek de var).

## Kontroller

Kontroller oyun içinde de ekranda gösterilir (`F1` paneli gizler, `F2` Türkçe / İngilizce değiştirir).

| Eylem | Klavye / fare | Gamepad |
|---|---|---|
| Hareket (kameraya göre) | `W A S D` | Sol çubuk |
| Kamera | Fare | Sağ çubuk |
| Zıplama / tırmanma / ragdoll'dan kalkma | `Space` | A (alt tuş) |
| Sprint (basılı tut) | `Sol Shift` | Sol çubuğa basma |
| Yürüme / koşma geçişi | `Sol Ctrl` | D-pad aşağı |
| Çömelme (çift basış: yuvarlanma) | `C` veya `Sol Alt` | B (sağ tuş) |
| Yuvarlanma | `Q` | X (sol tuş) |
| Ragdoll aç / kapat | `X` | Y (üst tuş) |
| Nişan alma (basılı tut) | Sağ fare tuşu | Sol tetik |
| Dönüş modu: hız yönü / bakış yönü | `1` / `2` | D-pad sol / sağ |
| Kamera omzu değiştirme | `V` | Sağ çubuğa basma |
| Başlangıca dönme | `R` | Select |

Tırmanma (mantle): engele doğru yön tuşunu basılı tutarken `Space`. Havadayken yön tuşu basılıysa uygun
kenarlar kendiliğinden yakalanır.

## Özellikler

- **Yürüyüş durumları:** yürüme (1,65 m/s), koşma (3,5 m/s), sprint (6 m/s) ve çömelme. ALS'teki gibi "istenen /
  izin verilen / gerçek" yürüyüş ayrımı; ivme, frenleme, sürtünme ve dönüş hızı ALS'in "Normal" hareket
  eğrilerinden gelir.
- **Dönüş modları:** hız yönü (karakter gittiği yöne akıcı şekilde döner), bakış yönü (karakter kameraya göre
  döner, yan / geri adım atar, dururken kamera 45°'den fazla dönerse yerinde döner) ve nişan alma.
- **Zıplama, düşme, iniş:** havada sınırlı kontrol, apekste düşme animasyonuna geçiş, iniş animasyonu ve
  iniş sarsıntısı. Sert inişte (≥ 7 m/s) yön tuşu basılıysa yuvarlanarak iniş, çok sert inişte (> 10 m/s)
  ragdoll.
- **Yuvarlanma:** `Q` ya da çömelme tuşuna çift basış; yuvarlanırken kapsül küçülür, yön yavaşça
  değiştirilebilir, boşluğa yuvarlanınca ragdoll'a geçer.
- **Mantle:** ALS'in iz sürme mantığı (ileri kapsül izi, aşağı küre izi, yer kontrolü). 0,4–1,25 m alçak
  mantle, 1,25–2,5 m yüksek mantle (zıpla + tırman), havada kenar yakalama (zıplayarak yaklaşık 3,3 m'ye kadar).
- **Kamera:** fareyle kontrol edilen üçüncü şahıs kamera; eksen bağımsız gecikmeli pivot, omuz değiştirme,
  duruma göre (koşu, sprint, çömelme, nişan, ragdoll) yumuşak geçen ayarlar ve duvar çarpışması.
- **Ayak IK'sı:** ayaklar eğime, merdivene ve engebeye oturur, leğen kemiği alçaktaki ayağa göre iner.
- **Ragdoll:** Humanoid iskeletten çalışma anında kurulur; yüzüstü ve sırtüstü için ayrı kalkış, ragdoll
  pozundan animasyona yumuşak geçiş.
- **Animasyon:** hıza göre yürüme–koşu–sprint karışımı, ivmeye göre eğilme (lean), baş kameraya bakar,
  durumlar arası çapraz geçişler koddan yönetilir.
- **Test sahnesi:** düz zemin, 10°–50° rampalar (50° yürünemez), iki merdiven, 0,5–4 m tırmanma blokları,
  ayak IK'sı için engebeli zemin, çömelme tüneli ve düşüş platformları (3 m ve 6,5 m).

## Proje yapısı

```
Assets/ALS/
  Scripts/Runtime   Karakter, animasyon sürücüsü, mantle, ragdoll, kamera, girdi, HUD
  Scripts/Editor    İçe aktarma ayarları ile Animator, prefab ve sahne üreticileri (ALS menüsü)
  Art               CC0 mannequin ve animasyonlar, üretilen materyaller, ızgara dokusu
  Animation         Üretilen Animator Controller
  Prefabs           ALSCharacter prefab'ı
  Scenes            ALS_TestScene
Tools/blender       Animasyon dosyalarını GLB'den FBX'e çeviren Blender betiği
```

## Nasıl doğrulandı

Prototip Unity editöründe Play Mode'da, betiklenmiş girdilerle (karakter API'si ve sentetik klavye / fare
olayları üzerinden) çalıştırılıp durum kayıtları ve ekran görüntüleri incelenerek doğrulandı: yürüyüş
durumları, yön değiştirme, zıplama / iniş, çömelme ve tünel, yuvarlanma, her blok yüksekliğinde mantle,
rampalar, merdivenler, engebeli zeminde ayak IK'sı, 3 m ve 6,5 m düşüşler, iki yönden ragdoll ve kalkış,
bakış yönü / nişan modları ve yerinde dönüş.

Doğrulanamayanlar:

- Hareket hissi ve animasyon akıcılığı gerçek zamanlı olarak bir insan tarafından oynanarak değerlendirilmedi;
  ayarlar ALS değerlerine ve kare kare incelemeye dayanıyor.
- Gerçek gamepad ile denenmedi (bağlamalar tanımlı ama test edilmedi).
- Editör dışı (standalone) derleme alınıp çalıştırılmadı.

## Bilinen eksikler

- Çok oyunculu, yapay zekâ, overlay durumları (silah, kutu vb.), birinci şahıs kamera ve ayak sesi / efekt
  sistemleri yok.
- Animasyonlar ALS'in kendi seti değil, CC0 kütüphanelerden: dönüş (pivot), başlama / durma geçişleri, adım
  uzunluğu uyarlaması (stride warping) ve eklemeli eğilme pozları yok; eğilme ve iniş sarsıntısı prosedürel.
- Yan ve geri adım klipleri yalnızca yürüme temposunda olduğu için bakış yönü modunda yana / geriye hareket
  1,1–1,2 m/s ile sınırlı; nişan alırken koşulmaz. Çömelirken yalnızca ileri yürüme klibi var.
- Yüksek mantle için ayrı bir klip yok: zıplama + 1 m tırmanma klibiyle birleştirilir, eller kenara tam
  oturmayabilir (el IK'sı yok).
- Yüzüstü kalkış, şınav klibi + çömelmeden doğrulma ile birleştirilir.
- Ayak IK'sı basittir (ayak kilitleme yok); merdivende kapsül basamaklara kısa süreli takıldığı için gerçek
  ilerleme hızı biraz düşer (yaklaşık %5).
- Hareket eden platformlar ve fizik nesneleriyle etkileşim ele alınmadı.

## Kaynaklar ve lisans

- Kod: MIT ([LICENSE](LICENSE)). ALS-Community (MIT) kaynak kodundan uyarlanan bölümler ve asıl telif bildirimi
  [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) dosyasındadır.
- Karakter ve animasyonlar: Quaternius *Universal Animation Library 1 / 2* ve Mesh2Motion, hepsi **CC0 1.0**.
  Kaynak adresleri, sürümler ve yapılan dönüştürmeler aynı dosyada listelenir.
- ALS-Community deposundaki Unreal içeriği (mannequin, animasyonlar, eğriler) yalnızca Unreal Engine
  lisansıyla dağıtıldığı için bu projede yer almaz.

## English summary

An independent Unity 6 (URP) third-person locomotion prototype written with ALS-Community as the reference:
walk / run / sprint / crouch, jump / fall / land, velocity and looking-direction rotation modes with turn in
place, mouse-driven camera, roll, mantling, foot IK and ragdoll with get-up. It uses CC0 art only. Open
`Assets/ALS/Scenes/ALS_TestScene.unity` in Unity 6000.3.12f1 and press Play; controls are shown on screen.
