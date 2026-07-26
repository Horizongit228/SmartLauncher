# Обновления через GitHub Releases

## Файлы релиза

Для каждого тега `vX.Y.Z` workflow публикует:

- `SmartLauncher-Setup-X.Y.Z.exe`;
- `SmartLauncher.exe` — portable-версию;
- `update-manifest.json`.

Стабильный URL проверки:

```text
https://github.com/Horizongit228/SmartLauncher/releases/latest/download/update-manifest.json
```

Этот адрес задан по умолчанию и автоматически восстанавливается при
миграции пустого значения из предыдущей сборки.

## Создание релиза

```powershell
git tag v1.0.2
git push origin v1.0.2
```

GitHub Actions проверит совпадение тега с версией `.csproj`, соберёт оба
варианта, вычислит SHA-256, сформирует манифест и создаст GitHub Release.

## Безопасность

- установщик загружается только по HTTPS;
- SHA-256 из манифеста обязателен;
- несовпадение хэша отменяет обновление;
- перед установкой требуется подтверждение пользователя;
- для публичного релиза рекомендуется Authenticode-подпись.
