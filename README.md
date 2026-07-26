# Smart Launcher 1.0

**Самая первая версия.**

Smart Launcher — современный Windows-лаунчер сценариев. Один режим может
открыть приложения, сайты, файлы, папки, Steam-игры, команды и целые наборы
файлов проекта в заданном порядке.

## Главное

- визуальный редактор режимов и предварительный просмотр карточки;
- каталог EXE, UWP/MSIX, Chrome/Edge PWA и ярлыков Windows;
- несколько наборов файлов для одного проекта и drag-and-drop;
- задержка между действиями и безопасная остановка режима;
- системный трей, `Ctrl+L`, светлая/тёмная тема и прозрачность;
- резервные копии JSON, журнал и восстановление данных;
- установщик, portable-версия и автоматическое обновление через GitHub Releases.

Полная единая документация — функции, дизайн, архитектура, сборка,
обновления, публикация на GitHub и отличия от 0.3.0 — находится в
[PROJECT.md](PROJECT.md).

## Запуск из исходников

```powershell
dotnet run --project SmartLauncher.UI.csproj
```

Требуется Windows 10/11 и .NET 8 SDK.

## Сборка релиза

```powershell
.\installer\build-installer.ps1
.\installer\build-portable.ps1
```

Подробности: [BUILDING.md](BUILDING.md).

## Обновления

Релизный workflow собирает установщик, portable EXE и
`update-manifest.json`, а затем загружает их в GitHub Releases.
Инструкция: [updates/README.md](updates/README.md).
