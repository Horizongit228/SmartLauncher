using SmartLauncher.UI.Models;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace SmartLauncher.UI.Services
{
    public sealed class UpdateService
    {
        private static readonly HttpClient HttpClient =
            new()
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

        private static readonly JsonSerializerOptions JsonOptions =
            new()
            {
                PropertyNameCaseInsensitive = true
            };

        public async Task<UpdateCheckResult> CheckAsync(
            string manifestUrl,
            Version currentVersion,
            CancellationToken cancellationToken = default)
        {
            Uri manifestUri =
                ValidateWebUri(
                    manifestUrl,
                    "адрес манифеста");

            using HttpResponseMessage response =
                await HttpClient.GetAsync(
                    manifestUri,
                    cancellationToken);
            response.EnsureSuccessStatusCode();

            await using Stream content =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);
            UpdateManifest manifest =
                await JsonSerializer
                    .DeserializeAsync<UpdateManifest>(
                        content,
                        JsonOptions,
                        cancellationToken)
                ?? throw new InvalidDataException(
                    "Сервер вернул пустой манифест обновления.");

            if (!Version.TryParse(
                    manifest.Version,
                    out Version? availableVersion))
            {
                throw new InvalidDataException(
                    "В манифесте указана некорректная версия.");
            }

            _ = ValidateWebUri(
                manifest.InstallerUrl,
                "адрес установщика");

            if (string.IsNullOrWhiteSpace(manifest.Sha256)
                || manifest.Sha256.Length != 64)
            {
                throw new InvalidDataException(
                    "В манифесте отсутствует корректная контрольная сумма SHA-256.");
            }

            return new UpdateCheckResult
            {
                Manifest = manifest,
                IsUpdateAvailable =
                    availableVersion > currentVersion
            };
        }

        public async Task<string> DownloadInstallerAsync(
            UpdateManifest manifest,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Uri installerUri =
                ValidateWebUri(
                    manifest.InstallerUrl,
                    "адрес установщика");
            string downloadDirectory =
                Path.Combine(
                    Path.GetTempPath(),
                    "SmartLauncher",
                    "Updates");
            Directory.CreateDirectory(downloadDirectory);

            string downloadPath =
                Path.Combine(
                    downloadDirectory,
                    $"SmartLauncher-Setup-{manifest.Version}.exe");
            string temporaryPath =
                downloadPath + ".download";

            using HttpResponseMessage response =
                await HttpClient.GetAsync(
                    installerUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            response.EnsureSuccessStatusCode();

            long? totalBytes =
                response.Content.Headers.ContentLength;
            await using Stream source =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);
            await using var destination =
                new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true);

            var buffer = new byte[81920];
            long copied = 0;
            int read;
            while ((read = await source.ReadAsync(
                       buffer,
                       cancellationToken)) > 0)
            {
                await destination.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken);
                copied += read;
                if (totalBytes > 0)
                {
                    progress?.Report(
                        copied / (double)totalBytes.Value);
                }
            }

            await destination.FlushAsync(
                cancellationToken);
            destination.Close();

            string actualHash;
            await using (FileStream installer =
                         File.OpenRead(temporaryPath))
            {
                actualHash =
                    Convert.ToHexString(
                        await SHA256.HashDataAsync(
                            installer,
                            cancellationToken));
            }

            if (!string.Equals(
                    actualHash,
                    manifest.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temporaryPath);
                throw new InvalidDataException(
                    "Контрольная сумма установщика не совпала. Обновление отменено.");
            }

            File.Move(
                temporaryPath,
                downloadPath,
                overwrite: true);
            return downloadPath;
        }

        public static void StartInstaller(
            string installerPath)
        {
            if (!File.Exists(installerPath))
            {
                throw new FileNotFoundException(
                    "Загруженный установщик не найден.",
                    installerPath);
            }

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments =
                        "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                    UseShellExecute = true
                });
        }

        private static Uri ValidateWebUri(
            string value,
            string fieldName)
        {
            if (!Uri.TryCreate(
                    value,
                    UriKind.Absolute,
                    out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttps
                    && !(uri.Scheme
                            == Uri.UriSchemeHttp
                         && uri.IsLoopback)))
            {
                throw new InvalidDataException(
                    $"Некорректный {fieldName}. Требуется HTTPS.");
            }

            return uri;
        }
    }
}
