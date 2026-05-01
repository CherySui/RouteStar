using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;
using System.Threading.Tasks;
// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace RouteStar
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // 核心：无边框沉浸式并保留原生阴影贴靠
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);
            InitializeWebViewAsync();
        }

        private async void InitializeWebViewAsync()
        {
            await LauncherWebView.EnsureCoreWebView2Async();
            
            // 重要：让 WebView2 原生接管 CSS 的 -webkit-app-region: drag 拖拽属性
            LauncherWebView.CoreWebView2.Settings.IsNonClientRegionSupportEnabled = true;

            // 彻底去 Web 化：禁用各种浏览器的默认行为和提示
            LauncherWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            LauncherWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            LauncherWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            LauncherWebView.CoreWebView2.Settings.IsSwipeNavigationEnabled = false;
            LauncherWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            
            string baseFolder = AppContext.BaseDirectory;
            string webPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseFolder, "web"));
            
            if (System.IO.Directory.Exists(webPath))
            {
                // 先绑事件（极其重要）
                LauncherWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                // 核心：一劳永逸直接映射电脑的所有物理驱动器为虚拟服务器
                // 这将完美允许 Chromium 内核利用底层 C++ 播放器去自动分片(HTTP 206)抓取本地大体积视频
                foreach (var drive in System.IO.DriveInfo.GetDrives())
                {
                    if (drive.IsReady)
                    {
                        string letter = drive.Name.Substring(0, 1).ToLower();
                        try 
                        {
                            LauncherWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                                $"drive-{letter}.local", drive.RootDirectory.FullName, CoreWebView2HostResourceAccessKind.Allow);
                        } catch { } // 有可能某些保留盘符无法映射
                    }
                }

                LauncherWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "launcher.local", webPath, CoreWebView2HostResourceAccessKind.Allow);
                    
                LauncherWebView.CoreWebView2.NavigationCompleted += async (s, ev) => {
                    // 1. 发送配置数据给 JS
                    string configPath = System.IO.Path.Combine(AppContext.BaseDirectory, "RouteStarConfig.json");
                    string json = System.IO.File.Exists(configPath) ? System.IO.File.ReadAllText(configPath) : "null";
                    string payload = $"{{\"type\":\"load_config\", \"data\": {json}}}";
                    LauncherWebView.CoreWebView2.PostWebMessageAsJson(payload);

                    // 2. 强行等待 0.4 秒，确保 Web 渲染至少已经开始了
                    await System.Threading.Tasks.Task.Delay(400);
                    
                    // 3. 消失
                    HideLoadingMask();
                };

                LauncherWebView.Source = new Uri("http://launcher.local/index.html");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Error] Web resources path not found: {webPath}");
            }
        }

        private async void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                // 用 WebMessageAsJson 读取 JS 直接传过来的 Object 对象
                string message = e.WebMessageAsJson;
                if (string.IsNullOrEmpty(message)) return;

                using var doc = JsonDocument.Parse(message);
                var root = doc.RootElement;

                if (root.TryGetProperty("type", out var typeEl))
                {
                    string actionType = typeEl.GetString();
                    if (actionType == "open_file_picker")
                    {
                        var picker = new Windows.Storage.Pickers.FileOpenPicker();
                        // 绑定到当前窗口指针
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
                        picker.FileTypeFilter.Add(".exe");
                        picker.FileTypeFilter.Add(".lnk");

                        var file = await picker.PickSingleFileAsync();
                        if (file != null)
                        {
                            var icon = System.Drawing.Icon.ExtractAssociatedIcon(file.Path);
                            if (icon != null)
                            {
                                using var bmp = icon.ToBitmap();
                                using var ms = new MemoryStream();
                                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                string base64 = Convert.ToBase64String(ms.ToArray());

                                var response = new
                                {
                                    type = "app_selected",
                                    base64Icon = base64,
                                    appName = file.DisplayName,
                                    path = file.Path
                                };

                                LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(response));
                            }
                        }
                    }
                    else if (actionType == "open_folder_picker")
                    {
                        var picker = new Windows.Storage.Pickers.FolderPicker();
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                        picker.FileTypeFilter.Add("*");
                        var folder = await picker.PickSingleFolderAsync();
                        if (folder != null)
                        {
                            var response = new
                            {
                                type = "folder_selected",
                                path = folder.Path,
                                tag = root.TryGetProperty("tag", out var tagEl) ? tagEl.GetString() : ""
                            };
                            LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(response));
                        }
                    }
                    else if (actionType == "launch_app")
                    {
                        if (root.TryGetProperty("path", out var pathEl))
                        {
                            string targetPath = pathEl.GetString();
                            string appKey = root.TryGetProperty("appKey", out var keyEl) ? keyEl.GetString() : targetPath;

                            if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                                {
                                    FileName = targetPath,
                                    UseShellExecute = true
                                });

                                // Check if a specific process name is designated for time tracking
                                if (root.TryGetProperty("processName", out var procEl))
                                {
                                    string procName = procEl.GetString();
                                    if (!string.IsNullOrEmpty(procName))
                                    {
                                        // Start a disconnected background task to monitor the process
                                        _ = System.Threading.Tasks.Task.Run(async () =>
                                        {
                                            // Wait a bit initially for the slow-loading process to appear in the system (up to 60 seconds)
                                            int initRetries = 60;
                                            while (System.Diagnostics.Process.GetProcessesByName(procName).Length == 0 && initRetries > 0)
                                            {
                                                await System.Threading.Tasks.Task.Delay(1000);
                                                initRetries--;
                                            }

                                            // If found, enter the 1-second tick loop
                                            while (System.Diagnostics.Process.GetProcessesByName(procName).Length > 0)
                                            {
                                                await System.Threading.Tasks.Task.Delay(1000);

                                                // We must dispatch back to UI thread to communicate with WebView2 safely
                                                DispatcherQueue.TryEnqueue(() =>
                                                {
                                                    var payloadObj = new { type = "update_time_tick", path = targetPath, appKey = appKey };
                                                    LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payloadObj));
                                                });
                                            }
                                        });
                                    }
                                }
                            }
                        }
                    }
                    else if (actionType == "save_config")
                    {
                        if (root.TryGetProperty("data", out var dataEl))
                        {
                            string configData = dataEl.GetRawText();
                            string configPath = Path.Combine(AppContext.BaseDirectory, "RouteStarConfig.json");
                            File.WriteAllText(configPath, configData);
                        }
                    }
                    else if (actionType == "load_gacha_cache")
                    {
                        // 读取本地缓存，直接返回给前端，不调用 API
                        string gameBiz = root.GetProperty("gameBiz").GetString();
                        string cacheFile = Path.Combine(AppContext.BaseDirectory, $"gacha_{gameBiz}.json");
                        var cached = LoadGachaCache(cacheFile);
                        if (cached.Count > 0)
                        {
                            var resp = new { type = "gacha_logs_result", success = true, logs = cached };
                            LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(resp));
                        }
                        // 如果没有缓存则静默，保持空状态占位
                    }
                    else if (actionType == "get_gacha_url")
                    {
                        string gameBiz = root.GetProperty("gameBiz").GetString();
                        string gamePath = root.TryGetProperty("gamePath", out var gpEl) ? gpEl.GetString() : null;
                        string url = await Task.Run(() => ExtractGachaUrl(gameBiz, gamePath));

                        if (string.IsNullOrEmpty(url))
                        {
                            var failResp = new { type = "gacha_logs_result", success = false, error = "未能提取到 URL，请确保您最近在游戏中打开过抽卡记录页面，或在偏好设置中设置游戏路径。" };
                            LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(failResp));
                            return;
                        }

                        // 通知前端开始抓取
                        var startResp = new { type = "gacha_fetch_started" };
                        LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(startResp));

                        // 加载本地缓存
                        string cacheFile = Path.Combine(AppContext.BaseDirectory, $"gacha_{gameBiz}.json");
                        var cachedLogs = LoadGachaCache(cacheFile);

                        // 计算每个卡池最新的已知 ID（增量拉取的停止点）
                        var maxKnownIds = cachedLogs
                            .GroupBy(l => l.gacha_type)
                            .ToDictionary(g => g.Key, g => g.Max(l => l.id));

                        // 在 C# 后端执行 HTTP 请求，绕过 CORS
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var newLogs = await FetchAllGachaLogsAsync(gameBiz, url, maxKnownIds, (status) =>
                                {
                                    DispatcherQueue.TryEnqueue(() =>
                                    {
                                        var prog = new { type = "gacha_fetch_progress", message = status };
                                        LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(prog));
                                    });
                                });

                                // 合并：新记录在前，去重，按 ID 升序排序
                                var merged = newLogs.Concat(cachedLogs)
                                    .GroupBy(l => l.id)
                                    .Select(g => g.First())
                                    .OrderBy(l => l.id)
                                    .ToList();

                                // 保存到本地缓存
                                SaveGachaCache(cacheFile, merged);

                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    var successResp = new { type = "gacha_logs_result", success = true, logs = merged };
                                    LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(successResp));
                                });
                            }
                            catch (Exception ex)
                            {
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    var errResp = new { type = "gacha_logs_result", success = false, error = ex.Message };
                                    LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(errResp));
                                });
                            }
                        });
                    }
                    else if (actionType == "pick_media")
                    {
                        string type = root.GetProperty("mediaType").GetString();
                        string appPath = root.GetProperty("appPath").GetString();

                        var picker = new Windows.Storage.Pickers.FileOpenPicker();
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
                        if (type == "video")
                        {
                            picker.FileTypeFilter.Add(".mp4");
                            picker.FileTypeFilter.Add(".webm");
                        }
                        else
                        {
                            picker.FileTypeFilter.Add(".png");
                            picker.FileTypeFilter.Add(".jpg");
                            picker.FileTypeFilter.Add(".jpeg");
                        }

                        var file = await picker.PickSingleFileAsync();
                        if (file != null)
                        {
                            // 解析路径例如 D:\Videos\bg.mp4 -> http://drive-d.local/Videos/bg.mp4
                            string realPath = file.Path;
                            if (realPath.Length >= 3 && realPath.Substring(1, 2) == ":\\")
                            {
                                string letter = realPath.Substring(0, 1).ToLower();
                                string urlPath = realPath.Substring(3).Replace('\\', '/');

                                string[] segments = urlPath.Split('/');
                                for (int i = 0; i < segments.Length; i++)
                                {
                                    segments[i] = Uri.EscapeDataString(segments[i]);
                                }

                                string finalUrl = $"http://drive-{letter}.local/" + string.Join("/", segments);

                                var response = new
                                {
                                    type = "media_picked",
                                    mediaType = type,
                                    appPath = appPath,
                                    path = finalUrl
                                };
                                LauncherWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(response));
                            }
                        }
                        else if (actionType == "open_folder")
                        {
                            if (root.TryGetProperty("path", out var pEl))
                            {
                                string folderPath = pEl.GetString();
                                if (!string.IsNullOrEmpty(folderPath) && System.IO.Directory.Exists(folderPath))
                                {
                                    System.Diagnostics.Process.Start("explorer.exe", folderPath);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebMessageError] {ex.Message}");
            }
        }

        private void HideLoadingMask()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (LoadingMask.Visibility == Visibility.Collapsed) return;

                var fadeAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = new Duration(TimeSpan.FromSeconds(0.25))
                };

                var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
                sb.Children.Add(fadeAnim);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeAnim, LoadingMask);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeAnim, "Opacity");

                sb.Completed += (s, e) =>
                {
                    LoadingMask.Visibility = Visibility.Collapsed;
                    LoadingMask.IsHitTestVisible = false;
                };

                sb.Begin();
            });
        }

        private static readonly System.Net.Http.HttpClient _gachaHttpClient = new System.Net.Http.HttpClient();

        // =========================================================
        // 本地缓存 I/O
        // =========================================================

        private record GachaLogEntry(string id, string name, string item_type, string rank_type, string gacha_type, string gacha_type_name, string time);

        private List<GachaLogEntry> LoadGachaCache(string path)
        {
            try
            {
                if (!File.Exists(path)) return new List<GachaLogEntry>();
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<GachaLogEntry>>(json) ?? new List<GachaLogEntry>();
            }
            catch { return new List<GachaLogEntry>(); }
        }

        private void SaveGachaCache(string path, List<GachaLogEntry> logs)
        {
            try
            {
                var json = JsonSerializer.Serialize(logs, new JsonSerializerOptions { WriteIndented = false });
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Gacha] Failed to save cache: {ex.Message}");
            }
        }

        // =========================================================
        // API 抓取（带增量停止逻辑）
        // =========================================================

        private async Task<List<GachaLogEntry>> FetchAllGachaLogsAsync(
            string gameBiz, string gachaUrl,
            Dictionary<string, string> maxKnownIds,
            Action<string> progressCallback)
        {
            // 根据游戏类型决定 API 端点和卡池列表
            string apiBase;
            int[] gachaTypes;

            if (gameBiz.StartsWith("hk4e"))
            {
                apiBase = gameBiz.Contains("global")
                    ? "https://public-operation-hk4e-sg.hoyoverse.com/gacha_info/api/getGachaLog"
                    : "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog";
                gachaTypes = new[] { 301, 302, 200, 100 }; // 角色UP、武器UP、常驻、新手
            }
            else if (gameBiz.StartsWith("hkrpg"))
            {
                apiBase = gameBiz.Contains("global")
                    ? "https://public-operation-hkrpg-sg.hoyoverse.com/common/gacha_record/api/getGachaLog"
                    : "https://public-operation-hkrpg.mihoyo.com/common/gacha_record/api/getGachaLog";
                gachaTypes = new[] { 1, 2, 11, 12 }; // 常驻、新手、角色UP、光锥UP
            }
            else if (gameBiz.StartsWith("nap"))
            {
                apiBase = gameBiz.Contains("global")
                    ? "https://public-operation-nap-sg.hoyoverse.com/common/gacha_record/api/getGachaLog"
                    : "https://public-operation-nap.mihoyo.com/common/gacha_record/api/getGachaLog";
                gachaTypes = new[] { 1, 2, 3, 5 };
            }
            else throw new Exception($"未知的游戏类型: {gameBiz}");

            // 从 gachaUrl 中提取鉴权参数
            var srcUri = new Uri(gachaUrl);
            // 手动解析 Query String
            var authParams = new Dictionary<string, string>();
            foreach (var part in srcUri.Query.TrimStart('?').Split('&'))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2)
                    authParams[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv[1]);
            }

            // 需要保留的鉴权参数
            var keepParams = new[] { "authkey_ver", "sign_type", "auth_appid", "authkey", "lang", "game_biz", "region" };

            var allLogs = new List<GachaLogEntry>();
            string typeKey = string.Empty;

            foreach (var gachaType in gachaTypes)
            {
                typeKey = gachaType.ToString();
                string endId = "0";
                int page = 1;
                string typeName = GetGachaTypeName(gameBiz, gachaType);
                maxKnownIds.TryGetValue(typeKey, out string maxKnownId); // null 表示没有本地缓存
                bool reachedKnown = false;

                while (true)
                {
                    progressCallback($"正在抓取 [{typeName}] 第 {page} 页...");

                    // 构建请求 URL
                    var queryParts = new List<string>();
                    foreach (var key in keepParams)
                    {
                        if (authParams.TryGetValue(key, out var val))
                            queryParts.Add($"{key}={Uri.EscapeDataString(val)}");
                    }
                    queryParts.Add($"gacha_type={gachaType}");
                    queryParts.Add("size=20");
                    queryParts.Add($"end_id={endId}");

                    string requestUrl = $"{apiBase}?{string.Join("&", queryParts)}";
                    System.Diagnostics.Debug.WriteLine($"[Gacha] Fetching: {requestUrl}");

                    var resp = await _gachaHttpClient.GetStringAsync(requestUrl);
                    var json = JsonSerializer.Deserialize<JsonElement>(resp);

                    if (json.GetProperty("retcode").GetInt32() != 0)
                    {
                        string msg = json.GetProperty("message").GetString();
                        throw new Exception($"API 返回错误: {msg}");
                    }

                    var list = json.GetProperty("data").GetProperty("list");
                    int count = list.GetArrayLength();

                    if (count == 0) break;

                    foreach (var item in list.EnumerateArray())
                    {
                        string itemId = item.GetProperty("id").GetString();

                        // 增量停止：遇到已知最大 ID 则跳出
                        if (maxKnownId != null && string.Compare(itemId, maxKnownId, StringComparison.Ordinal) <= 0)
                        {
                            reachedKnown = true;
                            break;
                        }

                        allLogs.Add(new GachaLogEntry(
                            id: itemId,
                            name: item.GetProperty("name").GetString(),
                            item_type: item.TryGetProperty("item_type", out var it) ? it.GetString() : "",
                            rank_type: item.GetProperty("rank_type").GetString(),
                            gacha_type: typeKey,
                            gacha_type_name: typeName,
                            time: item.GetProperty("time").GetString()
                        ));
                    }

                    if (reachedKnown || count < 20) break;

                    endId = list[count - 1].GetProperty("id").GetString();
                    page++;

                    // 增加页面间延迟，防止 visit too frequently
                    await Task.Delay(750);
                }

                progressCallback($"[{typeName}] 完成，共获取 {allLogs.Count(l => l.gacha_type == typeKey)} 条新记录");
                
                // 卡池切换间歇休息，进一步降低触发频率限制的风险
                await Task.Delay(1000);
            }

            return allLogs;
        }

        private static string GetGachaTypeName(string gameBiz, int gachaType)
        {
            if (gameBiz.StartsWith("hk4e"))
            {
                return gachaType switch { 301 => "角色活动祈愿", 302 => "武器活动祈愿", 200 => "常驻祈愿", 100 => "新手祈愿", _ => gachaType.ToString() };
            }
            else if (gameBiz.StartsWith("hkrpg"))
            {
                return gachaType switch { 1 => "常驻跃迁", 2 => "新手跃迁", 11 => "角色活动跃迁", 12 => "光锥活动跃迁", _ => gachaType.ToString() };
            }
            else
            {
                return gachaType switch { 1 => "常驻调频", 2 => "新手调频", 3 => "邦布调频", 5 => "独家频段", _ => gachaType.ToString() };
            }
        }

        private string ExtractGachaUrl(string gameBiz, string userGamePath = null)
        {
            try
            {
                // 1. 常见的米哈游缓存根目录 (LocalLow)
                string localLow = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "miHoYo");
                
                string localLowFolder = "";
                string dataDataPath = ""; // 相对安装目录的路径

                switch (gameBiz)
                {
                    case "hk4e_cn":
                        localLowFolder = "Genshin Impact";
                        dataDataPath = @"YuanShen_Data\webCaches";
                        break;
                    case "hk4e_global":
                        localLowFolder = "Genshin Impact";
                        dataDataPath = @"GenshinImpact_Data\webCaches";
                        break;
                    case "hkrpg_cn":
                    case "hkrpg_global":
                        localLowFolder = "崩坏：星穹铁道";
                        dataDataPath = @"StarRail_Data\webCaches";
                        break;
                    case "nap_cn":
                    case "nap_global":
                        localLowFolder = "ZenlessZoneZero";
                        dataDataPath = @"ZenlessZoneZero_Data\webCaches";
                        break;
                }

                List<string> cacheFiles = new List<string>();

                // A. 搜索安装目录 (用户手动指定)
                if (!string.IsNullOrEmpty(userGamePath) && Directory.Exists(userGamePath))
                {
                    string installWebCache = Path.Combine(userGamePath, dataDataPath);
                    if (Directory.Exists(installWebCache))
                    {
                        // 1. 直接搜索目录下的 data_2 (Starward 逻辑)
                        string directTarget = Path.Combine(installWebCache, @"Cache\Cache_Data\data_2");
                        if (File.Exists(directTarget)) cacheFiles.Add(directTarget);

                        // 2. 搜索版本子目录下的 data_2
                        foreach (var sub in Directory.GetDirectories(installWebCache))
                        {
                            string target = Path.Combine(sub, @"Cache\Cache_Data\data_2");
                            if (File.Exists(target)) cacheFiles.Add(target);
                        }
                    }
                }

                // B. 搜索 LocalLow (默认位置)
                if (!string.IsNullOrEmpty(localLowFolder))
                {
                    string basePath = Path.Combine(localLow, localLowFolder);
                    // 兼容星铁的英文名文件夹
                    if (!Directory.Exists(basePath) && gameBiz.Contains("hkrpg"))
                    {
                        basePath = Path.Combine(localLow, "Honkai: Star Rail");
                    }

                    if (Directory.Exists(basePath))
                    {
                        string webCachePath = Path.Combine(basePath, "webCaches");
                        if (Directory.Exists(webCachePath))
                        {
                            // 递归寻找所有 data_2
                            var files = Directory.GetFiles(webCachePath, "data_2", SearchOption.AllDirectories);
                            cacheFiles.AddRange(files);
                        }
                    }
                }

                if (cacheFiles.Count == 0) return "";

                // 按修改时间排序，取最新的
                var sortedFiles = cacheFiles.Distinct().OrderByDescending(f => File.GetLastWriteTime(f));

                // 准备搜索前缀 (更宽松的匹配)
                string[] prefixes = gameBiz.Contains("global") 
                    ? new[] { "https://gs.hoyoverse.com/", "https://webstatic-sea.hoyoverse.com/", "https://webstatic-sea.mihoyo.com/" }
                    : new[] { "https://webstatic.mihoyo.com/", "https://public-operation" };

                foreach (var file in sortedFiles)
                {
                    try {
                        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var ms = new MemoryStream();
                        fs.CopyTo(ms);
                        byte[] data = ms.ToArray();
                        var span = new ReadOnlySpan<byte>(data);

                        foreach (var prefix in prefixes)
                        {
                            byte[] prefixBytes = System.Text.Encoding.UTF8.GetBytes(prefix);
                            var prefixSpan = new ReadOnlySpan<byte>(prefixBytes);

                            // 关键：使用 LastIndexOf 取最后一个匹配，与 Starward 完全一致
                            // data_2 文件会积累很多旧 URL，只有最后一个才有有效的 authkey
                            int index = span.LastIndexOf(prefixSpan);
                            if (index >= 0)
                            {
                                // 找结束边界：\0 或 " 或 '
                                var remaining = span[index..];
                                int length = remaining.IndexOfAny((byte)0, (byte)'"', (byte)'\'');
                                if (length < 0) length = remaining.Length;

                                string url = System.Text.Encoding.UTF8.GetString(data, index, length);
                                if (url.Contains("authkey=")) return url;
                            }
                        }
                    } catch { continue; }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GachaError] {ex.Message}");
            }
            return "";
        }

        private int FindBytes(byte[] src, byte[] find, int startIndex = 0)
        {
            for (int i = startIndex; i <= src.Length - find.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < find.Length; j++)
                {
                    if (src[i + j] != find[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}
