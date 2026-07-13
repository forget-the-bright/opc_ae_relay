using System;
using System.Collections.Generic;
using System.Linq;
using Nancy;
using Nancy.Conventions;
using Nancy.Hosting.Self;
using opc_ae_relay.core;
using Serilog;

namespace opc_ae_relay.config
{
    // 页面路由
    public class HomeModule : NancyModule
    {
        // 1. 声明随机数生成器（建议全局只实例化一次）
        // private static readonly Random _random = new Random();

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
                // double num4 = 1.5 + _random.NextDouble() * (10.5 - 1.5);
                // Log.Information($"OPC-AE- testtesttest - {num4}");

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
                        // progid = config.ProgId,
                        running = OpcAeClientRun.opcThreadsRunning.TryGetValue(ip, out var isRunning) &&
                                  isRunning, // 如果当前字典有值, true && 字典中的值，那么最后看的就是字典中的值是否为true
                        threadId = OpcAeClientRun.restartCount.TryGetValue(ip, out var restartCount) ? restartCount : 0
                    };
                }

                return Response.AsJson(result);
            });
        }
    }

    // 启动配置：开启静态文件 + 模板目录
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