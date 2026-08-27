# Notchless — Windows için Dinamik Ada

[![Release Build](https://github.com/Efe-Tuncel/Notchless/actions/workflows/release.yml/badge.svg)](https://github.com/Efe-Tuncel/Notchless/actions/workflows/release.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/Efe-Tuncel/Notchless/blob/master/LICENSE)
[![Latest Release](https://img.shields.io/github/v/release/Efe-Tuncel/Notchless)](https://github.com/Efe-Tuncel/Notchless/releases/latest)

WPF + .NET 8 ile Windows'ta macOS Dynamic Island benzeri kapsül. Hover'da genişler, tıklayınca Kontrol Merkezi.

## ⬇️ İndir

**Latest Release:** [Notchless-v1.0.0-win-x64.zip](https://github.com/Efe-Tuncel/Notchless/releases/latest) + `Notchless.exe` (tag'e otomatik eklenir)

> SignPath Foundation onayı sonrası her release **DigiCert köklü Authenticode** ile imzalı gelecek — Smart App Control / SmartScreen direkt geçecek.

## ✨ Özellikler

- Üst ortada kapsül, hover'da genişler
- Tıkla → Kontrol Merkezi (Wi-Fi, Bluetooth, Ses vb.)
- Hafif, tek exe, installer yok

## 🚀 Kurulum

1. [Releases](https://github.com/Efe-Tuncel/Notchless/releases/latest)'ten `Notchless-v*.zip` veya `Notchless.exe`'yi indir
2. Zip ise çıkar, `Notchless.exe`'yi çalıştır
3. Smart App Control uyarısı alırsan: `Daha fazla bilgi → Yine de çalıştır` (SignPath sonrası bu uyarı kalkacak)

## Smart App Control / SmartScreen — Free Çözüm

Windows 11 `Smart App Control = On` iken `self-signed` + yeni hash olan her exe (`0x800711C7`) kesilir. Bu **kod hatası değil**, free dağıtım için 2 yol var:

### 1) Geliştirici (senin PC) — 10 sn, bir kere

`PowerShell'i Yönetici olarak aç` →

```powershell
.\scripts\Enable-NotchlessDev.ps1
# veya manuel:
Add-MpPreference -ExclusionPath "C:\Users\efetu\Desktop\Notchless"
```

Restart sonrası bu klasörde **hiçbir build bir daha bloklanmaz** (hash değişse bile). Script: `scripts/Enable-NotchlessDev.ps1:1`

Alternatif: `Ayarlar → Windows Güvenliği → Uygulama ve tarayıcı denetimi → Akıllı Uygulama Denetimi → Kapalı`

### 2) Son kullanıcı — Free, para yok

**a) Kalıcı free imza (önerilen):** Repo `public` olduğu için **SignPath Foundation** (`about.signpath.io/product/open-source`) üzerinden **ücretsiz Authenticode** alıyoruz. Onay `1-3 gün`, sonrası her `git tag` → GitHub Actions `2-3 dk`da otomatik imzalar, `Smart App Control` ve `SmartScreen` direkt geçer (gerçek `DigiCert` kökü).

**b) Anında free (SignPath beklenirken):** Her majör release'i `https://www.microsoft.com/en-us/wdsi/filesubmission` → `Clean file` olarak yolla → `24-48 saatte` o hash `reputable` olur. `timers.json`/`theme.txt` (`%LocalAppData%\Notchless\`) dışarıda olduğu için küçük güncellemelerde **rebuild/hash yok**, tekrar submit gerekmez.

### Kendin imzalamak istersen (geçici)

```powershell
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=Notchless Self-Sign" -CertStoreLocation Cert:\CurrentUser\My
$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe"
& $signtool sign /fd SHA256 /sha1 $cert.Thumbprint /t http://timestamp.digicert.com .\bin\Release\net8.0-windows10.0.22621.0\Notchless.exe
```

Bu sadece senin makinende geçerli, dağıtım için SignPath/WDSI kullan.

## 🛠️ Derleme

```powershell
dotnet publish Notchless.csproj -c Release -r win-x64 --self-contained false -o publish
.\publish\Notchless.exe
```

Gereksinim: .NET 8 SDK, Windows 10 1809+

## 📄 Lisans

MIT — `LICENSE` dosyasına bak. SignPath Foundation için OSI onaylı.
