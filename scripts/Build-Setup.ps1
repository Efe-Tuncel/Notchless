param([string]$Version = "1.3.5")
# Setup exe oluştur — dotnet publish + Inno Setup (ISCC varsa)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
Write-Host "Publishing Notchless $Version ..."
dotnet publish Notchless.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish -p:Version=$Version -p:AssemblyVersion="$Version.0" -p:FileVersion="$Version.0" -p:InformationalVersion="v$Version" | Out-Host

# ISS version güncelle
$iss = Join-Path $root "installer\notchless.iss"
if (Test-Path $iss) {
    (Get-Content $iss) -replace '#define MyAppVersion ".*"', "#define MyAppVersion ""$Version""" | Set-Content $iss -Encoding UTF8
    Write-Host "ISS version set to $Version"
}

# ISCC var mı?
$iscc = Get-Command ISCC -ErrorAction SilentlyContinue
if (-not $iscc) { $iscc = Get-Command "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" -ErrorAction SilentlyContinue }
if ($iscc) {
    Write-Host "Building setup with Inno Setup..."
    & $iscc.Source $iss | Out-Host
    Write-Host "Setup built: dist\"
    Get-ChildItem dist -Filter "*.exe" | Format-Table Name,Length
} else {
    Write-Host "ISCC bulunamadı — setup atlandı, publish klasörü hazır. CI'da Inno Setup kurulu olmalı."
    Write-Host "Manuel: https://jrsoftware.org/isinfo.php -> ISCC.exe kur, tekrar çalıştır"
}
# Not: %LocalAppData%\Notchless asla silinmez, installer [UninstallDelete] boş
