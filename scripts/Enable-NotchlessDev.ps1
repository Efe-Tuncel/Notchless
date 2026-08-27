# Notchless - Dev makine için Smart App Control / Defender dışlama (one-time, free)
# Kullanım: PowerShell'i YÖNETİCİ olarak aç -> .\scripts\Enable-NotchlessDev.ps1
param(
  [string]$Path = "C:\Users\efetu\Desktop\Notchless"
)
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  Write-Host ">> Lutfen PowerShell'i YONETICI olarak ac ve tekrar dene." -ForegroundColor Yellow
  Write-Host "   Baslat -> PowerShell -> Sag tik -> Yonetici olarak calistir" -ForegroundColor Yellow
  exit 1
}
try {
  # Klasör yoksa oluştur
  if (-not (Test-Path $Path)) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
  Add-MpPreference -ExclusionPath $Path -ErrorAction Stop
  Write-Host ">> OK: $Path Defender/WDAC dislama listesine eklendi." -ForegroundColor Green
  Write-Host "   Artik bu klasordeki her yeni build (hash degisse bile) bloklanmaz." -ForegroundColor Green
  Write-Host "   PC'yi yeniden baslatman onerilir." -ForegroundColor Cyan
  Get-MpPreference | Select-Object -ExpandProperty ExclusionPath
} catch {
  Write-Host ">> Hata: $_" -ForegroundColor Red
  Write-Host "   Alternatif: Ayarlar -> Windows Guvenligi -> Uygulama ve tarayici denetimi -> Akilli Uygulama Denetimi -> Kapali -> Restart" -ForegroundColor Yellow
}
