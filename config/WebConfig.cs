using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Nancy;
using Nancy.Conventions;
using Nancy.Hosting.Self;
using Newtonsoft.Json;
using opc_ae_relay.core;
using opc_ae_relay.util;
using Serilog;

namespace opc_ae_relay.config
{
    // 页面路由
    public class HomeModule : NancyModule
    {
        public HomeModule()
        {
            Get("/", _ =>
            {
                var opcList = OpcAeClientRun.oPCServerConfigs
                    .Select((cfg, idx) => new
                    {
                        Index = idx + 1,
                        Key = $"opc{idx + 1}",
                        cfg.Name,
                        cfg.IP,
                        cfg.ProgId,
                        ActiveClass = idx == 0 ? "active" : "", // 预计算
                        DisplayStyle = idx == 0 ? "block" : "none" // 预计算
                    })
                    .ToList();

                return View["index.html", new
                {
                    Title = "OPC AE 告警监控面板",
                    OpcServers = opcList
                }];
            });


            // API
            Get("/api/hello", _ => { return Response.AsJson(new { msg = "Hello API" }); });

            Get("/api/logs", _ =>
            {
                var list = new List<string>();
                while (LogBuffer.Queue.TryDequeue(out var line)) list.Add(line);
                return Response.AsJson(list);
            });


            Get("/api/status", _ =>
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
                        progid = OpcAeClientRun.hostInfo.ContainsKey(ip) ? OpcAeClientRun.hostInfo[ip] : config.ProgId,
                        running = OpcAeClientRun.opcThreadsRunning.TryGetValue(ip, out var isRunning) &&
                                  isRunning,
                        threadId = OpcAeClientRun.restartCount.TryGetValue(ip, out var restartCount) ? restartCount : 0
                    };
                }

                return Response.AsJson(result);
            });

            // 性能监控接口
            Get("/api/performance", _ =>
            {
                var proc = Process.GetCurrentProcess();

                var (cpuPercent, uptimeStr) = CpuUtil.GetInfo(proc);

                // 当前进程的网络连接（ETW 追踪）
                var monitor = EtwTrafficMonitor.Instance;
                var tcpConnections = monitor.GetConnections();
                var established = tcpConnections.Count(c => c.State == "ESTABLISHED");

                return Response.AsJson(new
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
                });
            });
        }
    }

    /// <summary>
    /// Web 应用层流量统计（解决 HTTP.sys 入站连接无法通过 API 采集的问题）
    /// </summary>
    internal static class WebTrafficCounter
    {
        private static long _totalBytesIn;
        private static long _totalBytesOut;
        private static long _requestCount;

        public static long TotalBytesIn => Interlocked.Read(ref _totalBytesIn);
        public static long TotalBytesOut => Interlocked.Read(ref _totalBytesOut);
        public static long RequestCount => Interlocked.Read(ref _requestCount);

        public static void AddRequest(long bytesIn)
        {
            Interlocked.Increment(ref _requestCount);
            Interlocked.Add(ref _totalBytesIn, bytesIn);
        }

        public static void AddResponse(long bytesOut)
        {
            Interlocked.Add(ref _totalBytesOut, bytesOut);
        }
    }


    // 启动配置：开启静态文件 + 模板目录 + 流量统计
    public class Bootstrapper : DefaultNancyBootstrapper
    {
        protected override void ConfigureConventions(NancyConventions conventions)
        {
            base.ConfigureConventions(conventions);

            // 1. 静态文件目录：static/（直接映射到 /static/xxx）
            conventions.StaticContentsConventions.Add(
                StaticContentConventionBuilder.AddDirectory("static", @"view\static")
            );

            // 2. 视图目录：从默认 Views 改为 view（适配你的小写目录名）
            conventions.ViewLocationConventions.Clear();

            // 其次查找 view/页面
            conventions.ViewLocationConventions.Add(
                (viewName, model, ctx) => $"view/{viewName}"
            );
        }

        protected override void ApplicationStartup(Nancy.TinyIoc.TinyIoCContainer container,
            Nancy.Bootstrapper.IPipelines pipelines)
        {
            base.ApplicationStartup(container, pipelines);

            // 请求前：统计入站字节
            pipelines.BeforeRequest += ctx =>
            {
                long bytesIn = ctx?.Request?.Headers?.ContentLength ?? 0;
                // 加上请求头的大致估算
                bytesIn += 300;
                WebTrafficCounter.AddRequest(bytesIn);
                return null;
            };

            // 响应后：统计出站字节
            pipelines.AfterRequest += ctx =>
            {
                long bytesOut = 0;
                if (ctx.Response.Headers.ContainsKey("Content-Length"))
                {
                    long.TryParse(ctx.Response.Headers["Content-Length"], out bytesOut);
                }

                if (bytesOut <= 0) bytesOut = 512;
                WebTrafficCounter.AddResponse(bytesOut);
            };

            // 开启错误详情显示（仅开发环境！）
            pipelines.OnError += (ctx, ex) =>
            {
                var errorModel = new
                {
                    statusCode = 500,
                    message = "服务器处理请求时发生异常",
                    detail = ex.ToString()
                };

                // 直接用 Newtonsoft 序列化
                string json = JsonConvert.SerializeObject(errorModel, Formatting.Indented);

                return new Response
                {
                    StatusCode = HttpStatusCode.InternalServerError,
                    ContentType = "application/json; charset=utf-8",
                    Contents = stream =>
                    {
                        using (var writer = new StreamWriter(stream))
                        {
                            writer.Write(json);
                        }
                    }
                };
            };
        }
    }


    public static class WebConfig
    {
        private static NancyHost _host;

        public static void Start()
        {
            var webConfig = AppConfigLoader.Config.Web ?? new WebServerConfig();
            var uri = new Uri(webConfig.BaseUrl);

            var hostConfig = new HostConfiguration
            {
                UrlReservations = new UrlReservations
                {
                    CreateAutomatically = true
                }
            };

            _host = new NancyHost(hostConfig, uri);
            _host.Start();

            Log.Information("Web 服务已启动：{Url}", uri);
        }

        public static void Stop()
        {
            try
            {
                _host?.Stop();

                Log.Information("Web 服务已停止");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Web 服务停止时出现异常");
            }
        }
    }
}