using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EmbedIO.WebSockets;
using Serilog;
using System.Text;

namespace opc_ae_relay.web;

#region WebSocket 日志模块

/// <summary>
/// WebSocket 日志推送模块，客户端连接后实时接收日志。
/// 首次连接的新客户端会收到最近 200 条历史日志。
/// </summary>
public class LogWebSocketModule : WebSocketModule
{
    private const int MaxHistory = 600;
    private static readonly ConcurrentQueue<string> LogBuffer = new ConcurrentQueue<string>();
    private static readonly ConcurrentDictionary<string, byte> SeenClients = new ConcurrentDictionary<string, byte>();

    public LogWebSocketModule(string urlPath) : base(urlPath, true)
    {
    }

    /// <summary>
    /// 广播文本消息给所有已连接客户端（使用文本帧，浏览器直接收到 string）
    /// </summary>
    public Task BroadcastMessageAsync(string message)
    {
        WebTrafficCounter.AddResponse(Encoding.UTF8.GetByteCount(message));
        EnqueueLog(message);
        return BroadcastAsync(message);
    }

    private static void EnqueueLog(string message)
    {
        LogBuffer.Enqueue(message);
        while (LogBuffer.Count > MaxHistory && LogBuffer.TryDequeue(out _)) { }
    }

    protected override async Task OnClientConnectedAsync(IWebSocketContext context)
    {
        var clientId = GetQueryParam(context, "clientId");
        Log.Debug("[WS] 日志客户端已连接: {Remote}, ClientId={ClientId}", context.RequestUri, clientId);

        if (!string.IsNullOrEmpty(clientId) && SeenClients.TryAdd(clientId, 0))
        {
            var history = LogBuffer.ToArray();
            foreach (var msg in history)
            {
                await SendAsync(context, msg);
            }

            Log.Debug("[WS] 已向新客户端推送 {Count} 条历史日志", history.Length);
        }
    }

    protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
    {
        Log.Debug("[WS] 日志客户端已断开");
        return Task.CompletedTask;
    }

    protected override Task OnMessageReceivedAsync(
        IWebSocketContext context, byte[] rxBuffer, IWebSocketReceiveResult rxResult)
    {
        return Task.CompletedTask;
    }

    private static string GetQueryParam(IWebSocketContext context, string key)
    {
        var query = context.RequestUri.Query;
        if (string.IsNullOrEmpty(query)) return null;
        return query.TrimStart('?')
            .Split('&')
            .Select(p => p.Split(new[] { '=' }, 2))
            .Where(kv => kv.Length == 2 && kv[0] == key)
            .Select(kv => Uri.UnescapeDataString(kv[1]))
            .FirstOrDefault();
    }
}

/// <summary>
/// 日志广播委托
/// </summary>
public static class LogBroadcaster
{
    public static Func<string, Task> Broadcast { get; set; }

    public static void SetSink(Func<string, Task> broadcast)
    {
        Broadcast = broadcast;
    }

    public static void Push(string message)
    {
        Broadcast?.Invoke(message);
    }
}

#endregion