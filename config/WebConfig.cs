using Nancy;
using Nancy.Conventions;
using Nancy.Hosting.Self;
using opcLearn.core;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YokogawaAE;

namespace opcLearn.config

{


    // 页面路由
    public class HomeModule : NancyModule
    {
        // 1. 声明随机数生成器（建议全局只实例化一次）
        private static readonly Random _random = new Random();
        public HomeModule()
        {
            // 首页：渲染模板 Views/index.html
            Get("/", _ =>
            {
                return View["index.html", new
                {
                    Title = "告警监控面板",
                    Now = DateTime.Now,
                    Logs = new[]{
                                new { Level = "info", Msg = "系统启动" },
                                new { Level = "warn", Msg = "连接超时" },
                                new { Level = "error", Msg = "IOP 报警" }
                        }
                }];
            }
            );


            // API
            Get("/api/hello", _ =>
            {
                return Response.AsJson(new { msg = "Hello API" });
            });

            Get("/api/logs", _ =>
            {
                var list = new List<string>();
                while (LogBuffer.Queue.TryDequeue(out string line))
                {
                    list.Add(line);
                }
                return Response.AsJson(list);
            });






            Get("/api/status", _ =>
            {
                // double num4 = 1.5 + _random.NextDouble() * (10.5 - 1.5);
                // Log.Information($"OPC-AE- testtesttest - {num4}");

                var result = new Dictionary<string, object>();

                for (int i = 0; i < OpcAeClientRun.oPCServerConfigs.Count; i++)
                {
                    var config = OpcAeClientRun.oPCServerConfigs[i];
                    string ip = config.IP;
                    string key = $"opc{i + 1}";

                    result[key] = new
                    {
                        ip = ip,
                        progid = OpcAeClientRun.hostInfo.ContainsKey(ip),
                       // progid = config.ProgId,
                        running = OpcAeClientRun.opcThreadsRunning.TryGetValue(ip, out var isRunning) && isRunning,
                        threadId = OpcAeClientRun.opcThreads.TryGetValue(ip, out var thread) ? thread.ManagedThreadId : 0
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

        public static void run() {
            var uri = new Uri("http://localhost:9000");
            // 关键配置：自动创建 URL 保留，解决权限问题
            var hostConfig = new HostConfiguration
            {
                UrlReservations = new UrlReservations
                {
                    CreateAutomatically = true
                }
            };

            var host = new NancyHost(hostConfig, uri);
            host.Start();

            Console.WriteLine($"Web 运行在：{uri}");
            Console.ReadLine();
            host.Stop();
        }
    }
}
