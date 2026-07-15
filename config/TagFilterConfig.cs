using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Serilog;

namespace opc_ae_relay.config
{
    /// <summary>
    /// 告警标签过滤配置
    /// 从 tagFilter.xlsx 第一列加载标签，支持文件变更自动热更新
    /// 只有 SourceName 在过滤表中的告警才会入库和推送 MQ
    /// </summary>
    public static class TagFilterConfig
    {
        private static readonly object _lock = new object();
        private static HashSet<string> _allowedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string _filePath;
        private static DateTime _lastWriteTime;
        private static volatile bool _isLoading;
        private static FileSystemWatcher _watcher;

        /// <summary>
        /// 初始化标签过滤器，加载 tagFilter.xlsx 并启动文件监听
        /// </summary>
        public static void Init()
        {
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tagFilter.xlsx");

            if (!File.Exists(_filePath))
            {
                Log.Warning("tagFilter.xlsx 不存在，将不过滤任何标签（全部放行）");
                return;
            }

            LoadFromFile();
            StartFileWatcher();

            Log.Information("标签过滤器初始化完成，共 {Count} 个标签", _allowedTags.Count);
        }

        /// <summary>
        /// 判断标签是否允许通过过滤
        /// </summary>
        /// <param name="sourceName">告警源名称（标签名）</param>
        /// <returns>true=允许通过，false=过滤掉</returns>
        public static bool IsAllowed(string sourceName)
        {
            if (string.IsNullOrEmpty(sourceName))
                return false;

            // 如果过滤表为空（文件不存在），则全部放行
            if (_allowedTags.Count == 0)
                return true;

            //CheckReload();
            return _allowedTags.Contains(sourceName);
        }

        /// <summary>
        /// 获取当前过滤表中的标签数量
        /// </summary>
        public static int Count => _allowedTags.Count;

        /// <summary>
        /// 获取所有允许的标签（用于调试/展示）
        /// </summary>
        public static IReadOnlyCollection<string> GetAllTags()
        {
           // CheckReload();
            return _allowedTags.ToList().AsReadOnly();
        }

        private static void LoadFromFile()
        {
            try
            {
                var newTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (IsFileLocked(_filePath))
                {
                    // 文件被占用（如 Excel 打开中），复制到临时文件再读取
                    Log.Information("tagFilter.xlsx 被占用，使用临时文件读取");
                    var tempPath = Path.GetTempFileName();
                    tempPath = Path.ChangeExtension(tempPath, ".xlsx");
                    try
                    {
                        File.Copy(_filePath, tempPath, overwrite: true);
                        using (var workbook = new XLWorkbook(tempPath))
                        {
                            ParseTags(workbook, newTags);
                        }
                    }
                    finally
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                }
                else
                {
                    // 文件未被占用，直接读取
                    using (var workbook = new XLWorkbook(_filePath))
                    {
                        ParseTags(workbook, newTags);
                    }
                }

                lock (_lock)
                {
                    _allowedTags = newTags;
                    _lastWriteTime = File.GetLastWriteTime(_filePath);
                }

                Log.Information("标签过滤器已加载，共 {Count} 个标签", newTags.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "加载 tagFilter.xlsx 失败");
            }
        }

        /// <summary>
        /// 判断文件是否被其他进程占用
        /// </summary>
        private static bool IsFileLocked(string filePath)
        {
            try
            {
                using (var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return false;
                }
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        /// <summary>
        /// 从 workbook 中解析第一列标签
        /// </summary>
        private static void ParseTags(XLWorkbook workbook, HashSet<string> tags)
        {
            var worksheet = workbook.Worksheet(1);
            var rows = worksheet.RowsUsed();

            foreach (var row in rows)
            {
                var cell = row.Cell(1);
                if (cell == null || cell.IsEmpty())
                    continue;

                var value = cell.GetValue<string>()?.Trim();
                if (string.IsNullOrEmpty(value))
                    continue;

                if (row.RowNumber() == 1 && !LooksLikeTag(value))
                    continue;

                tags.Add(value);
            }
        }

        /// <summary>
        /// 判断是否为表头行（简单规则：纯中文或包含"标签/Tag/备注"等关键词）
        /// </summary>
        private static bool LooksLikeTag(string value)
        {
            // 纯中文 → 表头
            if (value.All(c => c >= 0x4E00 && c <= 0x9FFF))
                return false;

            // 包含常见表头关键词 → 表头
            var lower = value.ToLower();
            if (lower == "tag" || lower == "tags" || lower == "标签" || lower == "备注" || lower == "name" || lower == "标签名")
                return false;

            // 其他情况都当作标签
            return true;
        }

        /// <summary>
        /// 检查文件是否变更，变更则重新加载
        /// </summary>
        private static void CheckReload()
        {
            if (_filePath == null || !File.Exists(_filePath))
                return;

            var currentWriteTime = File.GetLastWriteTime(_filePath);
            if (currentWriteTime != _lastWriteTime)
            {
                Log.Information("检测到 tagFilter.xlsx 变更，重新加载...");
                LoadFromFile();
            }
            else
            {
                Log.Information("tagFilter.xlsx 内容检测未变更, 继续使用缓存数据！");
            }
        }

        private static void StartFileWatcher()
        {
            var dir = Path.GetDirectoryName(_filePath);
            var fileName = Path.GetFileName(_filePath);

            _watcher = new FileSystemWatcher(dir, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
            };

            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.Renamed += OnFileChanged;

            _watcher.EnableRaisingEvents = true;
        }

        private static void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // 延迟避免 Excel 保存过程中多次触发，也兼容文件替换场景
            System.Threading.Thread.Sleep(1500);
            Log.Information("tagFilter.xlsx 文件变更事件触发: {ChangeType}", e.ChangeType);

            if (_isLoading)
            {
                Log.Information("tagFilter.xlsx 正在加载中，跳过本次触发");
                return;
            }

            _isLoading = true;
            try
            {
                CheckReload();
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// 释放文件监听器
        /// </summary>
        public static void Shutdown()
        {
            _watcher?.Dispose();
            _watcher = null;
            Log.Warning("{fileName} 文件状态监听已停止", "tagFilter.xlsx");
        }
    }
}