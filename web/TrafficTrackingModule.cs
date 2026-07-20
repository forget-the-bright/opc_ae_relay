using System;
using System.IO;
using System.Threading.Tasks;
using EmbedIO;

namespace opc_ae_relay.web;

#region 全局流量拦截

    /// <summary>
    /// 全局流量统计中间件，注册在管道最前面，
    /// 所有请求（HTTP / WebSocket 升级 / 静态文件）均经过此模块统一计量入站字节。
    /// 出站字节由各响应出口（SendJson / HandleHome / BroadcastMessageAsync）精确记录。
    /// </summary>
    public class TrafficTrackingModule : WebModuleBase
    {
        private static readonly string StaticDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "view", "static");

        public TrafficTrackingModule() : base("/")
        {
        }

        public override bool IsFinalHandler => false;

        protected override Task OnRequestAsync(IHttpContext context)
        {
            var req = context.Request;
            var path = req.Url.AbsolutePath;

            // ===== 入站：请求行 + 头部 + Body =====
            long bytesIn = req.ContentLength64 > 0 ? req.ContentLength64 : 0;
            bytesIn += EstimateRequestHeaders(req);
            WebTrafficCounter.AddRequest(bytesIn);

            // ===== 出站：静态文件从磁盘获取精确大小，其余由响应出口自行记录 =====
            if (path.StartsWith("/static/"))
            {
                var relativePath = path.Substring("/static/".Length).Replace('/', Path.DirectorySeparatorChar);
                var filePath = Path.Combine(StaticDir, relativePath);
                if (File.Exists(filePath))
                    WebTrafficCounter.AddResponse(new FileInfo(filePath).Length);
            }
            else if (path.StartsWith("/ws/"))
            {
                // WebSocket 升级握手响应（固定 101 Switching Protocols）
                WebTrafficCounter.AddResponse(130);
            }
            // API 和首页的出站由 SendJson / HandleHome 精确记录，此处不重复计算

            return Task.CompletedTask;
        }

        /// <summary>
        /// 估算请求头大小（请求行 + 常见头部）
        /// </summary>
        private static long EstimateRequestHeaders(IHttpRequest req)
        {
            // 请求行: "GET /path HTTP/1.1\r\n"
            long size = req.Url.PathAndQuery.Length + 20;
            // 常见头部: Host, User-Agent, Accept, Connection, Upgrade-Insecure-Requests 等
            size += 250;
            // WebSocket 升级额外头部
            if (req.Headers["Upgrade"] != null)
                size += 100;
            return size;
        }
    }

    #endregion