using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Actions;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using opc_ae_relay.config;
using opc_ae_relay.core;
using opc_ae_relay.util;
using Scriban;
using Serilog;

namespace opc_ae_relay.web
{
    /// <summary>
    /// 基于 EmbedIO + Scriban 的 Web 服务模块。
    /// 替代原 Nancy 方案，日志通过 WebSocket 实时推送。
    /// </summary>
    public static class EmbedIoWebServer
    {
        private static WebServer _server;
        private static LogWebSocketModule _logWsModule;

        public static void Start()
        {
            // 关闭 EmbedIO 内置的 Swan 请求日志
            Swan.Logging.Logger.NoLogging();

            var webConfig = AppConfigLoader.Config.Web ?? new WebServerConfig();
            var url = webConfig.BaseUrl + "/";

            _logWsModule = new LogWebSocketModule("/ws/logs");
            LogBroadcaster.SetSink(msg => _logWsModule.BroadcastMessageAsync(msg));

            var staticDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "view", "static");

            _server = new WebServer(o => o
                    .WithUrlPrefix(url)
                    .WithMode(HttpListenerMode.EmbedIO))
                .WithModule(new TrafficTrackingModule())
                .WithModule(_logWsModule)
                .WithModule(new ActionModule("/api", HttpVerbs.Get, HandleApi))
                .WithStaticFolder("/static", staticDir, false)
                .WithModule(new ActionModule("/", HttpVerbs.Get, HandleHome));

            _server.StateChanged += (s, e) =>
                Log.Debug("[Web] 状态变更: {State}", e.NewState);

            _server.RunAsync();
            Log.Information("Web 服务已启动（EmbedIO）：{Url}", url);
        }

        public static void Stop()
        {
            try
            {
                _server?.Dispose();
                _server = null;
                Log.Information("Web 服务已停止");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Web 服务停止时出现异常");
            }
        }

        #region 路由处理

        /// <summary>
        /// 首页：Scriban 模板渲染
        /// </summary>
        private static async Task HandleHome(IHttpContext ctx)
        {
            var opcList = OpcAeClientRun.oPCServerConfigs
                .Select((cfg, idx) => new OpcServerViewModel
                {
                    Index = idx + 1,
                    Key = $"opc{idx + 1}",
                    Name = cfg.Name,
                    IP = cfg.IP,
                    ProgId = cfg.ProgId,
                    ActiveClass = idx == 0 ? "active" : "",
                    DisplayStyle = idx == 0 ? "block" : "none"
                })
                .ToList();

            var model = new { Title = "OPC AE 告警监控面板", OpcServers = opcList };
            var html = RenderTemplate("index.html", model);

            await ctx.SendStringAsync(html, "text/html; charset=utf-8", Encoding.UTF8);
            WebTrafficCounter.AddResponse(Encoding.UTF8.GetByteCount(html));
        }

        /// <summary>
        /// API 路由分发
        /// </summary>
        private static async Task HandleApi(IHttpContext ctx)
        {
            var path = ctx.Request.Url.AbsolutePath.TrimEnd('/');

            switch (path)
            {
                case "/api/hello":
                    await SendJson(ctx, new { msg = "Hello API" });
                    break;

                case "/api/status":
                    await SendJson(ctx, BuildStatus());
                    break;

                case "/api/performance":
                    await SendJson(ctx, BuildPerformance());
                    break;

                default:
                    ctx.Response.StatusCode = 404;
                    await SendJson(ctx, new { error = "Not Found" });
                    break;
            }
        }

        #endregion

        #region 业务数据构建

        private static object BuildStatus()
        {
            var result = new Dictionary<string, object>();
            for (var i = 0; i < OpcAeClientRun.oPCServerConfigs.Count; i++)
            {
                var config = OpcAeClientRun.oPCServerConfigs[i];
                var ip = config.IP;
                var key = $"opc{i + 1}";

                result[key] = new
                {
                    ip,
                    progid = OpcAeClientRun.hostInfo.ContainsKey(ip)
                        ? OpcAeClientRun.hostInfo[ip]
                        : config.ProgId,
                    running = OpcAeClientRun.opcThreadsRunning.TryGetValue(ip, out var isRunning) && isRunning,
                    threadId = OpcAeClientRun.restartCount.TryGetValue(ip, out var rc) ? rc : 0
                };
            }

            return result;
        }

        private static object BuildPerformance()
        {
            var proc = Process.GetCurrentProcess();
            var (cpuPercent, uptimeStr) = CpuUtil.GetInfo(proc);

            var monitor = EtwTrafficMonitor.Instance;
            var tcpConnections = monitor.GetConnections();
            var established = tcpConnections.Count(c => c.State == "ESTABLISHED");

            return new
            {
                memory = new
                {
                    workingSetMb = Math.Round(proc.WorkingSet64 / 1024.0 / 1024.0, 1),
                    privateMb = Math.Round(proc.PrivateMemorySize64 / 1024.0 / 1024.0, 1),
                    gcHeapMb = Math.Round(GC.GetTotalMemory(false) / 1024.0 / 1024.0, 1),
                    gcGen0 = GC.CollectionCount(0),
                    gcGen1 = GC.CollectionCount(1),
                    gcGen2 = GC.CollectionCount(2),
                    handleCount = proc.HandleCount
                },
                cpu = new
                {
                    percent = cpuPercent,
                    threadCount = proc.Threads.Count,
                    uptime = uptimeStr
                },
                network = new
                {
                    total = tcpConnections.Count,
                    established,
                    statsAvailable = monitor.StatsAvailable,
                    statsChecked = monitor.StatsChecked,
                    connections = tcpConnections,
                    webTraffic = new
                    {
                        bytesIn = WebTrafficCounter.TotalBytesIn,
                        bytesOut = WebTrafficCounter.TotalBytesOut,
                        bytesInStr = TcpConnectionInfo.FormatBytes(WebTrafficCounter.TotalBytesIn),
                        bytesOutStr = TcpConnectionInfo.FormatBytes(WebTrafficCounter.TotalBytesOut),
                        requests = WebTrafficCounter.RequestCount
                    }
                }
            };
        }

        #endregion

        #region 辅助方法

        private static string RenderTemplate(string templateName, object model)
        {
            var viewDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "view");
            var templatePath = Path.Combine(viewDir, templateName);

            if (!File.Exists(templatePath))
                return $"<h1>模板文件不存在: {templateName}</h1>";

            var templateText = File.ReadAllText(templatePath);
            var template = Template.Parse(templateText);

            if (template.HasErrors)
                return $"<h1>模板解析错误</h1><pre>{string.Join("\n", template.Messages)}</pre>";

            try
            {
                return template.Render(model, member => member.Name);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Web] 模板渲染失败: {Template}", templateName);
                return $"<h1>模板渲染异常</h1><pre>{ex.Message}</pre>";
            }
        }

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private static async Task SendJson(IHttpContext ctx, object data)
        {
            var json = JsonConvert.SerializeObject(data, JsonSettings);
            await ctx.SendStringAsync(json, "application/json; charset=utf-8", Encoding.UTF8);
            WebTrafficCounter.AddResponse(Encoding.UTF8.GetByteCount(json));
        }

        #endregion
    }



    

   

 
}
