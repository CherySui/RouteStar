using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace RouteStar
{
    // ── 游戏下载状态枚举 ────────────────────────────────────────────────────
    public enum GameInstallState
    {
        NotInstalled,   // 未安装
        Installed,      // 已安装，版本一致
        NeedsUpdate,    // 需要更新
        PreDownload,    // 预下载可用
        Downloading,    // 正在下载
        Extracting,     // 正在解压
    }

    // ── 游戏状态查询结果 ─────────────────────────────────────────────────────
    public class GameStatusResult
    {
        public GameInstallState State { get; set; }
        public string? LocalVersion { get; set; }
        public string? LatestVersion { get; set; }
        public string? PreDownloadVersion { get; set; }
        public long DownloadSizeBytes { get; set; }
        public long DecompressedSizeBytes { get; set; }
        public bool UseIncrementalPatch { get; set; }
    }

    // ── HoYoPlay API 数据模型 ────────────────────────────────────────────────
    public class HypApiWrapper<T>
    {
        public int retcode { get; set; }
        public string? message { get; set; }
        public T? data { get; set; }
    }

    public class HypGamePackagesData
    {
        public List<HypGamePackage>? game_packages { get; set; }
    }

    public class HypGamePackage
    {
        public HypGame? game { get; set; }
        public HypPackageVersion? main { get; set; }
        public HypPackageVersion? pre_download { get; set; }
    }

    public class HypGame
    {
        public string? id { get; set; }
        public string? biz { get; set; }
    }

    public class HypPackageVersion
    {
        public HypPackageResource? major { get; set; }
        public List<HypPackageResource>? patches { get; set; }
    }

    public class HypPackageResource
    {
        public string? version { get; set; }
        public List<HypPackageFile>? game_pkgs { get; set; }
        public List<HypPackageFile>? audio_pkgs { get; set; }
        public string? res_list_url { get; set; }
    }

    public class HypPackageFile
    {
        public string? url { get; set; }
        public string? md5 { get; set; }
        public long size { get; set; }
        public long decompressed_size { get; set; }
        public string? language { get; set; }
    }

    // ── 下载进度报告 ─────────────────────────────────────────────────────────
    public class DownloadProgress
    {
        public string Phase { get; set; } = "downloading"; // downloading | verifying | extracting | done | error
        public long BytesDownloaded { get; set; }
        public long TotalBytes { get; set; }
        public double SpeedBytesPerSec { get; set; }
        public string? CurrentFile { get; set; }
        public string? ErrorMessage { get; set; }
    }

    // ── 游戏下载服务 ─────────────────────────────────────────────────────────
    public class GameDownloadService
    {
        // 国服 Launcher ID
        private const string LAUNCHER_ID_CN = "jGHBHlcOq1";
        private const string API_BASE_CN = "https://hyp-api.mihoyo.com/hyp/hyp-connect/api";

        // 内部 AppKey → HoYoPlay Game ID 映射（国服）
        public static readonly Dictionary<string, string> HOYO_GAME_IDS = new()
        {
            { "Hoyo_Genshin",  "1Z8W5NHUQb" },  // 原神
            { "Hoyo_StarRail", "64kMb5iAWu" },  // 崩坏：星穹铁道
            { "Hoyo_ZZZ",      "x6znKlJ0xK" },  // 绝区零
            { "Hoyo_H3",       "osvnlOc0S8" },  // 崩坏3
        };

        // 游戏 exe 文件名（用于验证安装目录）
        public static readonly Dictionary<string, string[]> HOYO_EXE_NAMES = new()
        {
            { "Hoyo_Genshin",  new[] { "GenshinImpact.exe", "YuanShen.exe" } },
            { "Hoyo_StarRail", new[] { "StarRail.exe" } },
            { "Hoyo_ZZZ",      new[] { "ZenlessZoneZero.exe" } },
            { "Hoyo_H3",       new[] { "BH3.exe" } },
        };

        // 游戏版本配置文件名（用于检测本地版本）
        private static readonly string[] VERSION_CONFIG_NAMES = { "config.ini", "game_version.ini", "GameVersion.ini" };

        private static readonly HttpClient _http = new(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        // ── 获取游戏安装包信息 ────────────────────────────────────────────────
        public static async Task<HypGamePackage?> FetchGamePackageAsync(string appKey, CancellationToken ct = default)
        {
            if (!HOYO_GAME_IDS.TryGetValue(appKey, out string? gameId)) return null;

            string url = $"{API_BASE_CN}/getGamePackages?launcher_id={LAUNCHER_ID_CN}&language=zh-cn&game_ids[]={gameId}";
            var resp = await _http.GetStringAsync(url, ct);

            var wrapper = JsonSerializer.Deserialize<JsonNode>(resp);
            var packages = wrapper?["data"]?["game_packages"]?.AsArray();
            if (packages == null) return null;

            foreach (var pkg in packages)
            {
                string? id = pkg?["game"]?["id"]?.GetValue<string>();
                if (id == gameId)
                {
                    return JsonSerializer.Deserialize<HypGamePackage>(pkg!.ToJsonString(), _jsonOptions);
                }
            }
            return null;
        }

        // ── 读取本地游戏版本 ──────────────────────────────────────────────────
        public static string? ReadLocalVersion(string installDir)
        {
            if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir)) return null;

            foreach (var cfgName in VERSION_CONFIG_NAMES)
            {
                string cfgPath = Path.Combine(installDir, cfgName);
                if (!File.Exists(cfgPath)) continue;

                foreach (var line in File.ReadLines(cfgPath))
                {
                    if (line.StartsWith("game_version=", StringComparison.OrdinalIgnoreCase))
                        return line.Split('=')[1].Trim();
                }
            }
            return null;
        }

        // ── 在安装目录中搜索游戏 exe ──────────────────────────────────────────
        public static string? FindGameExe(string appKey, string installDir)
        {
            if (!HOYO_EXE_NAMES.TryGetValue(appKey, out string[]? exeNames)) return null;
            if (!Directory.Exists(installDir)) return null;

            foreach (var exeName in exeNames)
            {
                // 递归搜索两层目录
                foreach (var candidate in Directory.EnumerateFiles(installDir, exeName, SearchOption.AllDirectories))
                {
                    return candidate;
                }
            }
            return null;
        }

        // ── 综合检测游戏状态 ──────────────────────────────────────────────────
        public static async Task<GameStatusResult> GetGameStatusAsync(string appKey, string? installDir, CancellationToken ct = default)
        {
            try
            {
                var pkg = await FetchGamePackageAsync(appKey, ct);
                if (pkg?.main?.major == null)
                    return new GameStatusResult { State = GameInstallState.NotInstalled };

                string latestVersion = pkg.main.major.version ?? "";
                long fullSize = pkg.main.major.game_pkgs?.Sum(p => p.size) ?? 0;
                long fullDecomp = pkg.main.major.game_pkgs?.Sum(p => p.decompressed_size) ?? 0;

                string? localVersion = ReadLocalVersion(installDir ?? "");

                if (string.IsNullOrEmpty(localVersion))
                {
                    // 检测预下载
                    string? preVer = pkg.pre_download?.major?.version;
                    if (!string.IsNullOrEmpty(preVer))
                    {
                        long preSize = pkg.pre_download!.major!.game_pkgs?.Sum(p => p.size) ?? 0;
                        return new GameStatusResult
                        {
                            State = GameInstallState.PreDownload,
                            LatestVersion = latestVersion,
                            PreDownloadVersion = preVer,
                            DownloadSizeBytes = preSize,
                        };
                    }
                    return new GameStatusResult
                    {
                        State = GameInstallState.NotInstalled,
                        LatestVersion = latestVersion,
                        DownloadSizeBytes = fullSize,
                        DecompressedSizeBytes = fullDecomp,
                    };
                }

                if (localVersion == latestVersion)
                {
                    // 已是最新版，检测是否有预下载
                    string? preVer = pkg.pre_download?.major?.version;
                    if (!string.IsNullOrEmpty(preVer) && preVer != latestVersion)
                    {
                        long preSize = pkg.pre_download!.major!.game_pkgs?.Sum(p => p.size) ?? 0;
                        return new GameStatusResult
                        {
                            State = GameInstallState.PreDownload,
                            LocalVersion = localVersion,
                            LatestVersion = latestVersion,
                            PreDownloadVersion = preVer,
                            DownloadSizeBytes = preSize,
                        };
                    }
                    return new GameStatusResult { State = GameInstallState.Installed, LocalVersion = localVersion, LatestVersion = latestVersion };
                }

                // 需要更新：优先找增量 patch 包
                var patch = pkg.main.patches?.FirstOrDefault(p => p.version == localVersion);
                if (patch != null)
                {
                    long patchSize = patch.game_pkgs?.Sum(p => p.size) ?? 0;
                    return new GameStatusResult
                    {
                        State = GameInstallState.NeedsUpdate,
                        LocalVersion = localVersion,
                        LatestVersion = latestVersion,
                        DownloadSizeBytes = patchSize,
                        UseIncrementalPatch = true,
                    };
                }

                // 无增量包，需完整重装
                return new GameStatusResult
                {
                    State = GameInstallState.NeedsUpdate,
                    LocalVersion = localVersion,
                    LatestVersion = latestVersion,
                    DownloadSizeBytes = fullSize,
                    DecompressedSizeBytes = fullDecomp,
                    UseIncrementalPatch = false,
                };
            }
            catch
            {
                return new GameStatusResult { State = GameInstallState.NotInstalled };
            }
        }

        // ── 下载单个文件（支持断点续传）────────────────────────────────────────
        public static async Task DownloadFileAsync(
            string url,
            string destPath,
            Action<long, long, double> onProgress, // (downloaded, total, speed)
            CancellationToken ct)
        {
            long existingSize = File.Exists(destPath) ? new FileInfo(destPath).Length : 0;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existingSize > 0)
                request.Headers.Range = new RangeHeaderValue(existingSize, null);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            // 文件已完整下载（Range 请求超出文件长度时服务器返回 416）
            if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                onProgress(existingSize, existingSize, 0);
                return;
            }
            response.EnsureSuccessStatusCode();

            long totalSize = (response.Content.Headers.ContentLength ?? 0) + existingSize;

            using var fs = new FileStream(destPath, existingSize > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None);
            using var stream = await response.Content.ReadAsStreamAsync(ct);

            byte[] buffer = new byte[81920]; // 80KB chunks
            long downloaded = existingSize;
            var speedTimer = System.Diagnostics.Stopwatch.StartNew();
            long speedBase = existingSize;

            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                downloaded += bytesRead;

                if (speedTimer.Elapsed.TotalSeconds >= 0.5)
                {
                    double speed = (downloaded - speedBase) / speedTimer.Elapsed.TotalSeconds;
                    onProgress(downloaded, totalSize, speed);
                    speedBase = downloaded;
                    speedTimer.Restart();
                }
            }
            onProgress(downloaded, totalSize, 0);
        }

        // ── MD5 校验 ──────────────────────────────────────────────────────────
        public static async Task<bool> VerifyMd5Async(string filePath, string expectedMd5, CancellationToken ct)
        {
            using var md5 = MD5.Create();
            using var fs = File.OpenRead(filePath);
            byte[] hash = await Task.Run(() => md5.ComputeHash(fs), ct);
            string actual = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return actual == expectedMd5.ToLowerInvariant();
        }

        // ── 解压 ZIP（带进度报告，支持分卷）────────────────────────────────────────────
        public static async Task ExtractZipAsync(List<string> zipParts, string destDir, Action<string> onFileExtracted, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(destDir);
                using Stream stream = zipParts.Count > 1 ? new CombinedStream(zipParts) : File.OpenRead(zipParts[0]);
                using var archive = SharpCompress.Archives.Zip.ZipArchive.Open(stream);
                var options = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };
                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                {
                    ct.ThrowIfCancellationRequested();
                    entry.WriteToDirectory(destDir, options);
                    onFileExtracted(entry.Key ?? "");
                }
            }, ct);
        }

        private class CombinedStream : Stream
        {
            private readonly FileStream[] _streams;
            private readonly long[] _lengths;
            private long _position;

            public CombinedStream(IEnumerable<string> filePaths)
            {
                _streams = filePaths.Select(f => new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read)).ToArray();
                _lengths = _streams.Select(s => s.Length).ToArray();
                _position = 0;
            }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _lengths.Sum();

            public override long Position
            {
                get => _position;
                set => Seek(value, SeekOrigin.Begin);
            }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int totalBytesRead = 0;

                while (totalBytesRead < count)
                {
                    long pos = _position;
                    int streamIdx = 0;
                    long streamStart = 0;

                    while (streamIdx < _streams.Length && pos >= streamStart + _lengths[streamIdx])
                    {
                        streamStart += _lengths[streamIdx];
                        streamIdx++;
                    }

                    if (streamIdx >= _streams.Length) break; // EOF across all parts

                    var currentStream = _streams[streamIdx];
                    currentStream.Position = pos - streamStart;

                    int remainingInPart = (int)(_lengths[streamIdx] - (pos - streamStart));
                    int bytesToRead = Math.Min(count - totalBytesRead, remainingInPart);
                    int bytesRead = currentStream.Read(buffer, offset + totalBytesRead, bytesToRead);
                    if (bytesRead == 0) break; // unexpected EOF within a part

                    _position += bytesRead;
                    totalBytesRead += bytesRead;
                }
                return totalBytesRead;
            }

            public override int ReadByte()
            {
                byte[] single = new byte[1];
                int n = Read(single, 0, 1);
                return n <= 0 ? -1 : single[0];
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                if (origin == SeekOrigin.Begin) _position = offset;
                else if (origin == SeekOrigin.Current) _position += offset;
                else if (origin == SeekOrigin.End) _position = Length + offset;
                return _position;
            }

            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                foreach (var s in _streams) s.Dispose();
                base.Dispose(disposing);
            }
        }

        // ── 写入本地版本配置 ──────────────────────────────────────────────────
        public static void WriteLocalVersion(string installDir, string version)
        {
            string cfgPath = Path.Combine(installDir, "config.ini");
            string content = $"[General]\r\ngame_version={version}\r\n";
            File.WriteAllText(cfgPath, content);
        }
    }
}
