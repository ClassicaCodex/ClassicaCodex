namespace ClassicaCodex.Ingestion;

/// <summary>
/// Downloads one file over HTTPS with progress and cancellation - the
/// DirectDownload counterpart to GitCorpusFetchService, for sources where
/// cloning the whole repository would be wildly disproportionate (Natural
/// Earth's repo is several gigabytes; the one map file we want is ~800KB).
///
/// Downloads to a temporary sibling file first and renames on completion,
/// so a cancelled or failed download can never leave a half-written file
/// sitting at the real path looking like a finished one.
/// </summary>
public class FileDownloadService
{
    public async Task DownloadAsync(
        string url,
        string destinationFilePath,
        IProgress<string> progress,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath)!);
        var tempPath = destinationFilePath + ".partial";

        try
        {
            using var http = new HttpClient();
            using var response = await http.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = File.Create(tempPath))
            {
                var buffer = new byte[81920];
                long bytesDone = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    bytesDone += read;
                    progress.Report(totalBytes.HasValue
                        ? $"Downloading... {bytesDone / 1024:N0} KB of {totalBytes.Value / 1024:N0} KB"
                        : $"Downloading... {bytesDone / 1024:N0} KB");
                }
            }

            File.Move(tempPath, destinationFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best effort cleanup */ }
            }
        }
    }
}
