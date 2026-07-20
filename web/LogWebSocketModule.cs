using System;
using System.Threading.Tasks;
using EmbedIO.WebSockets;
using Serilog;
using System.Text;

namespace opc_ae_relay.web;

#region WebSocket 日志模块

/// <summary>
/// WebSocket 日志推送模块，客户端连接后实时接收日志
/// </summary>
public class LogWebSocketModule : WebSocketModule
{
    public LogWebSocketModule(string urlPath) : base(urlPath, true)
    {
    }

    /// <summary>
    /// 广播文本消息给所有已连接客户端（使用文本帧，浏览器直接收到 string）
    /// </summary>
    public Task BroadcastMessageAsync(string message)
    {
        WebTrafficCounter.AddResponse(Encoding.UTF8.GetByteCount(message));
        return BroadcastAsync(message);
    }

    protected override Task OnClientConnectedAsync(IWebSocketContext context)
    {
        Log.Debug("[WS] 日志客户端已连接: {Remote}", context.RequestUri);
        return Task.CompletedTask;
    }

    protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
    {
        Log.Debug("[WS] 日志客户端已断开");
        return Task.CompletedTask;
    }

    protected override Task OnMessageReceivedAsync(
        IWebSocketContext context, byte[] rxBuffer, IWebSocketReceiveResult rxResult)
    {
        // 客户端无需发送消息，忽略
        return Task.CompletedTask;
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