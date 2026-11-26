using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FastCraft.Core
{
    /// <summary>
    /// Parallel, SHA-1 verified downloader on top of a persistent verification cache.
    /// </summary>
    public sealed class Downloader
    {
        private readonly HashCache _cache;
        private readonly int _parallel;
        private long _bytes;
        private int _done;
        private int _skipped;
        private int _total;
        private string _current = "";

        public Downloader(HashCache cache, int parallel)
        {
            _cache = cache;
            _parallel = Math.Max(1, parallel);
        }

        public int Total { get { return Volatile.Read(ref _total); } }
        public int Done { get { return Volatile.Read(ref _done); } }
        public int Skipped { get { return Volatile.Read(ref _skipped); } }
        public long Bytes { get { return Interlocked.Read(ref _bytes); } }
        public string Current { get { return Volatile.Read(ref _current); } }

        public void ResetCounters()
        {
            Interlocked.Exchange(ref _bytes, 0);
            Interlocked.Exchange(ref _done, 0);
            Interlocked.Exchange(ref _skipped, 0);
            Interlocked.Exchange(ref _total, 0);
        }

        public async Task EnsureAsync(DownloadItem item, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _total);
            if (await Task.Run(() => IsUpToDate(item), cancellationToken).ConfigureAwait(false))
            {
                Interlocked.Increment(ref _skipped);
                Interlocked.Increment(ref _done);
                return;
            }
            await DownloadOneAsync(item, cancellationToken).ConfigureAwait(false);
        }

        public async Task RunAsync(IReadOnlyList<DownloadItem> items, CancellationToken cancellationToken)
        {
            if (items == null || items.Count == 0) return;
            Interlocked.Add(ref _total, items.Count);
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = _parallel,
                CancellationToken = cancellationToken
            };
            await Parallel.ForEachAsync(items, options, async (item, token) =>
            {
                if (await Task.Run(() => IsUpToDate(item), token).ConfigureAwait(false))
                {
                    Interlocked.Increment(ref _skipped);
                    Interlocked.Increment(ref _done);
                    return;
                }
                await DownloadOneAsync(item, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        private bool IsUpToDate(DownloadItem item)
        {
            try
            {
                if (!File.Exists(item.Dest)) return false;
                var info = new FileInfo(item.Dest);
                if (info.Length == 0) return false;

                if (!string.IsNullOrEmpty(item.Sha1))
                {
                    if (_cache.IsVerified(item.Dest, info.Length, item.Sha1)) return true;
                    if (item.Size > 0 && info.Length == item.Size)
                    {
                        var actual = Hashing.Sha1File(item.Dest);
                        if (string.Equals(actual, item.Sha1, StringComparison.OrdinalIgnoreCase))
                        {
                            _cache.Remember(item.Dest, item.Sha1);
                            return true;
                        }
                    }
                    return false;
                }
                // No hash to check (some very old libraries): a non-empty file counts as done.
                return info.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task DownloadOneAsync(DownloadItem item, CancellationToken cancellationToken)
        {
            var directory = Path.GetDirectoryName(item.Dest);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temp = item.Dest + ".part";

            Exception last = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(400 * attempt, cancellationToken).ConfigureAwait(false);
                }
                try
                {
                    using (var response = await Http.Client.GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        var buffer = new byte[81920];
                        using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                        using (var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
                        {
                            int read;
                            while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                            {
                                await target.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                                Interlocked.Add(ref _bytes, read);
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(item.Sha1))
                    {
                        var actual = Hashing.Sha1File(temp);
                        if (!string.Equals(actual, item.Sha1, StringComparison.OrdinalIgnoreCase))
                        {
                            TryDelete(temp);
                            last = new InvalidDataException("Checksum mismatch for " + Path.GetFileName(item.Dest));
                            continue;
                        }
                    }

                    if (File.Exists(item.Dest)) File.Delete(item.Dest);
                    File.Move(temp, item.Dest);
                    if (!string.IsNullOrEmpty(item.Sha1)) _cache.Remember(item.Dest, item.Sha1);
                    Interlocked.Increment(ref _done);
                    Volatile.Write(ref _current, Path.GetFileName(item.Dest));
                    return;
                }
                catch (OperationCanceledException)
                {
                    TryDelete(temp);
                    throw;
                }
                catch (Exception ex)
                {
                    last = ex;
                    TryDelete(temp);
                }
            }

            Interlocked.Increment(ref _done);
            throw new IOException("Could not download " + item.Url + ": " + (last != null ? last.Message : "unknown error"), last);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }
    }

    public sealed class DownloadItem
    {
        public string Url;
        public string Dest;
        public string Sha1;
        public long Size;
        public string Name;
    }
}
