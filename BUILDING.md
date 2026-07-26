# Сборка Smart Launcher 1.0

## Требования

- Windows 10/11 x64;
- .NET 8 SDK;
- Inno Setup 6 — только для компиляции установщика.

Промежуточные файлы сохраняются в
`%LOCALAPPDATA%\SmartLauncher\Build`, поэтому заблокированные `bin/obj`
в папке проекта не мешают сборке.

## Запуск для разработки

Из папки `SmartLauncher.UI`:

```powershell
dotnet run --project SmartLauncher.UI.csproj
```

## Проверка

```powershell
dotnet build SmartLauncher.UI.csproj -c Debug
dotnet build SmartLauncher.UI.csproj -c Release
```

Анализаторы .NET включены, любое предупреждение считается ошибкой.

## Основная установленная версия

```powershell
dotnet publish SmartLauncher.UI.csproj -p:PublishProfile=installed-win-x64
```

Результат: `dist\installed-win-x64`. Это основной и более быстрый формат:
несколько файлов, которые затем упаковываются установщиком.

После установки Inno Setup 6:

```powershell
.\installer\build-installer.ps1
```

Результат: `dist\installer\SmartLauncher-Setup-1.0.0.exe`.

## Portable-версия

```powershell
.\installer\build-portable.ps1
```

Результат: `dist\portable-win-x64\SmartLauncher.exe`. Это автономный
single-file EXE; он остаётся дополнительным вариантом, а не основным.

## Манифест автообновления

После загрузки установщика на HTTPS-сервер:

```powershell
.\installer\New-UpdateManifest.ps1 `
  -InstallerUrl 'https://downloads.example.com/SmartLauncher-Setup-1.0.0.exe' `
  -ReleaseNotes 'Smart Launcher 1.0 — Самая первая версия'
```

Скрипт вычислит SHA-256 и создаст
`dist\installer\update-manifest.json`. Опубликуйте JSON и укажите его
HTTPS-адрес в настройках Smart Launcher.
