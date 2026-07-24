using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using opc_ae_relay.config;
using opc_ae_relay.db;
using Serilog;
using Swan;

namespace opc_ae_relay.util;

/// <summary>
/// 基于 ETW 内核网络事件的进程级 TCP 连接与流量监控。
/// 通过归一化四元组追踪连接，Send/Recv 事件自动合并为同一条记录。
/// 需要管理员权限运行。
/// </summary>
public sealed class EtwTrafficMonitor : IDisposable
{
    private const string SessionName = "OpcAeRelayNetTraffic";

    private TraceEventSession _session;
    private ETWTraceEventSource _source;
    private Task _processTask;
    private Timer _closeDetectTimer;
    private volatile bool _running;
    private volatile bool _started;
    private volatile bool _permissionDenied;
    private int _pid;
    private HashSet<string> _localIps;

    private readonly ConcurrentDictionary<string, TrackedConnection> _connections =
        new ConcurrentDictionary<string, TrackedConnection>();

    public static EtwTrafficMonitor Instance { get; } = new EtwTrafficMonitor();

    private EtwTrafficMonitor()
    {
    }

    public bool IsRunning => _running;
    public bool PermissionDenied => _permissionDenied;
    public bool HasAttempted => _started;
    public bool StatsAvailable => _running;
    public bool StatsChecked => _started;

    #region 启动 / 停止

    public void Start()
    {
        if (_running) return;
        _started = true;

        try
        {
            _pid = Process.GetCurrentProcess().Id;
            _localIps = GetLocalIPs();

            CleanupStaleSession();

            _session = new TraceEventSession(SessionName);
            _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

            _source = new ETWTraceEventSource(SessionName, TraceEventSourceType.Session);

            _source.Kernel.TcpIpSend += OnTcpIpSend;
            _source.Kernel.TcpIpRecv += OnTcpIpRecv;
            _source.Kernel.TcpIpConnect += OnTcpIpConnect;
            _source.Kernel.TcpIpDisconnect += OnTcpIpDisconnect;

            // TraceEvent 未暴露 TcpIpClose 事件，通过定时轮询系统 TCP 表检测连接关闭
            _closeDetectTimer = new Timer(_ => DetectClosedConnections(), null, 3000, 3000);

            _running = true;

            _processTask = Task.Factory.StartNew(() =>
            {
                try
                {
                    _source.Process();
                }
                catch (Exception ex)
                {
                    if (_running)
                        Log.Warning(ex, "[ETW] 事件处理异常");
                }
            }, TaskCreationOptions.LongRunning);

            Log.Information("[ETW] 网络流量监控已启动 (PID={Pid}, 本机IP: {Ips})", _pid, string.Join(",", _localIps));
        }
        catch (UnauthorizedAccessException)
        {
            _permissionDenied = true;
            _running = false;
            Log.Warning("[ETW] 权限不足，无法启动网络流量监控（需管理员运行）");
        }
        catch (Exception ex)
        {
            _permissionDenied = true;
            _running = false;
            Log.Warning(ex, "[ETW] 网络流量监控启动失败");
        }
    }

    public void Stop()
    {
        if (!_running && _session == null) return;
        _running = false;

        try
        {
            _closeDetectTimer?.Dispose();
            _closeDetectTimer = null;
        }
        catch
        {
        }

        try
        {
            _source?.StopProcessing();
            _source?.Dispose();
            _source = null;
        }
        catch
        {
        }

        try
        {
            _session?.Stop();
            _session?.Dispose();
            _session = null;
        }
        catch
        {
        }

        Log.Information("[ETW] 网络流量监控已停止");
    }

    public void Dispose() => Stop();

    #endregion

    #region 对外查询接口

    /// <summary>
    /// 获取本进程所有已追踪的 TCP 连接（含历史）
    /// </summary>
    public List<TcpConnectionInfo> GetConnections()
    {
        var list = new List<TcpConnectionInfo>();
        var applicationConfig = AppConfigLoader.Config;
        var applicationConfigWeb = applicationConfig.Web;
        Dictionary<string, (long webCountBytesIn, long webCountBytesOut, string webState, string localIp, DateTime? lastConnect, DateTime? lastClose)>
            webCountBytesDict =
                new Dictionary<string, (long, long, string, string, DateTime?, DateTime?)>();
        foreach (var kv in _connections)
        {
            var c = kv.Value;
            if (c.BytesIn == 0 && c.BytesOut == 0 && c.State.Equals("CLOSED"))
            {
                continue;
            }


            if (c.LocalPort == applicationConfigWeb.Port)
            {
                var (webCountBytesIn, webCountBytesOut, webState, localIp, lastConnect, lastClose) = webCountBytesDict
                    .GetOrAdd(c.RemoteIp, _ => (0, 0, c.State, c.LocalIp, (DateTime?)c.LastConnectTime, (DateTime?)c.LastCloseTime));
                if (!webState.Equals("ESTABLISHED") && c.State.Equals("ESTABLISHED"))
                {
                    webState = "ESTABLISHED";
                }

                webCountBytesIn += c.BytesIn;
                webCountBytesOut += c.BytesOut;
                // 取最新的连接开始时间和关闭时间
                if (c.LastConnectTime > lastConnect) lastConnect = c.LastConnectTime;
                if (c.LastCloseTime > lastClose) lastClose = c.LastCloseTime;
                webCountBytesDict[c.RemoteIp] = (webCountBytesIn, webCountBytesOut, webState, localIp, lastConnect, lastClose);
                continue;
            }

            list.Add(new TcpConnectionInfo
            {
                Local = $"{c.LocalIp}:{c.LocalPort}",
                LocalIp = $"{c.LocalIp}",
                LocalPort = $"{c.LocalPort}",
                Remote = $"{c.RemoteIp}:{c.RemotePort}",
                RemoteIp = $"{c.RemoteIp}",
                RemotePort = $"{c.RemotePort}",
                State = c.State,
                BytesIn = c.BytesIn,
                BytesOut = c.BytesOut,
                LastConnectTime = c.LastConnectTime,
                LastCloseTime = c.LastCloseTime
            });
        }

        // 统计 Web 服务器的流量
        foreach (KeyValuePair<string, (long webCountBytesIn, long webCountBytesOut, string webState, string localIp, DateTime? lastConnect, DateTime? lastClose)>
                     keyValuePair in webCountBytesDict)
        {
            var RemoteIp = keyValuePair.Key;
            var (webCountBytesIn, webCountBytesOut, webState, localIp, lastConnect, lastClose) = keyValuePair.Value;
            list.Add(new TcpConnectionInfo
            {
                Local = $"LocalWeb[{localIp}:{applicationConfigWeb.Port}]",
                LocalIp = $"{localIp}",
                LocalPort = $"{applicationConfigWeb.Port}",
                Remote = $"WEB访问[{RemoteIp}]",
                RemoteIp = $"{RemoteIp}",
                RemotePort = "",
                State = webState,
                BytesIn = webCountBytesIn,
                BytesOut = webCountBytesOut,
                LastConnectTime = lastConnect,
                LastCloseTime = lastClose
            });
        }

        // 统计连接中间件的流量
        var groupRemote = list.GroupBy(x => (x.Remote, x.RemoteIp, x.RemotePort)).ToList();
        foreach (var tcpConnectionInfos in groupRemote)
        {
            if (tcpConnectionInfos.Count() > 1)
            {
                var (remote, remoteIp, remotePort) = tcpConnectionInfos.Key;
                // if (IsLocalAddress(remoteIp)) 这里不好判断是不是本地的ip 因为可能部署到其他服务器啊。
                var BytesInSum = tcpConnectionInfos.Sum(x => x.BytesIn);
                var BytesOutSum = tcpConnectionInfos.Sum(x => x.BytesOut);
                var state = tcpConnectionInfos.Select(x => x.State.Equals("ESTABLISHED")).Count() > 0
                    ? "ESTABLISHED"
                    : "CLOSED";
                // 聚合组内取最新的连接开始/关闭时间
                var groupLastConnect = tcpConnectionInfos.Max(x => x.LastConnectTime);
                var groupLastClose = tcpConnectionInfos.Max(x => x.LastCloseTime);
                list = list.Where(x => !x.Remote.Equals(remote)).ToList();
                list.Add(new TcpConnectionInfo
                {
                    Local = $"Local",
                    LocalIp = $"Local",
                    LocalPort = $"Local",
                    Remote = $"{remote}",
                    RemoteIp = $"{remoteIp}",
                    RemotePort = $"{remotePort}",
                    State = state,
                    BytesIn = BytesInSum,
                    BytesOut = BytesOutSum,
                    LastConnectTime = groupLastConnect,
                    LastCloseTime = groupLastClose
                });
            }
        }
        
        // 过滤掉已关闭的OPC服务器连接,给连接特性识别并包装名称
        list = list.Where(c =>
        {
            var dbConfig = AppConfigLoader.GetDatabases().Find(s => DbManager.GetDatabasePort(s.Name).ToString()== c.RemotePort);
            if (dbConfig != null)
            {
                c.Remote = $"{dbConfig.Name}[{c.Remote}]";
            }
            
            var mqConfig = AppConfigLoader.GetMqServers().Find(s => s.Port.ToString() == c.RemotePort);
            if (mqConfig != null)
            {
                c.Remote = $"{mqConfig.Name}[{c.Remote}]";
                c.Local = "Local";
            }
            var opcServerConfig = AppConfigLoader.GetOPCServers().Find(s => s.IP == c.RemoteIp);
            if (opcServerConfig != null)
            {
                c.Remote = $"{opcServerConfig.Name}[{c.Remote}]";
                if (c.Local=="Local")
                {
                    c.Local = $"OPC心跳[{c.Local}]";
                }
                else
                {
                    c.Local = $"OPC流量[{c.Local}]";
                }
            }
            return !(opcServerConfig != null && c.State.Equals("CLOSED"));
        }).ToList();
        
        var sortedList = list
            // 1. 先按 ESTABLISHED 状态优先
            .OrderByDescending(x => x.State == "ESTABLISHED")
            // 2. 再按远程 IP 分组排序（相同 IP 放一起）
            .ThenByDescending(x => IsLocalAddress(x.RemoteIp))
            .ThenBy(x => AppConfigLoader.GetOPCServers().Find(s => s.IP == x.RemoteIp)?.IP)
            .ThenByDescending(x => x.RemoteIp)  
            .ThenByDescending(x => x.Local == "OPC心跳[Local]")
            // 3. 再按接收量降序
            .ThenByDescending(x => x.BytesIn)
            // 4. 最后按发送量降序
            .ThenByDescending(x => x.BytesOut)
            .ToList();

        return sortedList;
    }

    /// <summary>当前活跃连接数</summary>
    public int ActiveCount => _connections.Count(c => c.Value.State == "ESTABLISHED");

    /// <summary>清空所有记录</summary>
    public void Reset() => _connections.Clear();

    #endregion

    #region ETW 事件处理

    private void OnTcpIpSend(TcpIpSendTraceData data)
    {
        if (!_running || data.ProcessID != _pid) return;
        try
        {
            var conn = GetOrCreateNormalized(
                data.saddr.ToString(), data.sport,
                data.daddr.ToString(), data.dport);
            if (data.size > 0)
                Interlocked.Add(ref conn._bytesOut, data.size);
        }
        catch
        {
        }
    }

    private void OnTcpIpRecv(TcpIpTraceData data)
    {
        if (!_running || data.ProcessID != _pid) return;
        try
        {
            var conn = GetOrCreateNormalized(
                data.saddr.ToString(), data.sport,
                data.daddr.ToString(), data.dport);
            if (data.size > 0)
                Interlocked.Add(ref conn._bytesIn, data.size);
        }
        catch
        {
        }
    }

    private void OnTcpIpConnect(TcpIpConnectTraceData data)
    {
        if (!_running || data.ProcessID != _pid) return;
        try
        {
            var conn = GetOrCreateNormalized(
                data.saddr.ToString(), data.sport,
                data.daddr.ToString(), data.dport);
            conn.State = "ESTABLISHED";
            conn.LastConnectTime = DateTime.Now;
        }
        catch
        {
        }
    }

    private void OnTcpIpDisconnect(TcpIpTraceData data)
    {
        if (!_running || data.ProcessID != _pid) return;
        try
        {
            MarkClosed(data.saddr.ToString(), data.sport,
                data.daddr.ToString(), data.dport);
        }
        catch
        {
        }
    }

    /// <summary>
    /// 定时轮询系统 TCP 连接表，检测已消失的连接并记录关闭时间。
    /// 补充 ETW 未暴露的 TcpIpClose 事件（应用直接关闭 socket / RST / 远端断开等场景）。
    /// </summary>
    private void DetectClosedConnections()
    {
        if (!_running) return;
        try
        {
            var activeSet = new HashSet<string>();
            foreach (var conn in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections())
            {
                // 归一化为与 _connections 相同的 key 格式
                string key = BuildNormalizedKey(
                    conn.LocalEndPoint.Address.ToString(), conn.LocalEndPoint.Port,
                    conn.RemoteEndPoint.Address.ToString(), conn.RemoteEndPoint.Port);
                activeSet.Add(key);
            }

            var now = DateTime.Now;
            foreach (var kv in _connections)
            {
                var c = kv.Value;
                if (!c.State.Equals("ESTABLISHED")) continue;
                if (activeSet.Contains(kv.Key)) continue;

                // 宽限期：避免误判刚创建还未出现在表中的连接
                if (c.LastConnectTime.HasValue && (now - c.LastConnectTime.Value).TotalSeconds < 6)
                    continue;

                c.State = "CLOSED";
                c.LastCloseTime = now;
            }
        }
        catch
        {
        }
    }

    /// <summary>标记连接关闭并记录关闭时间</summary>
    private void MarkClosed(string addr1, int port1, string addr2, int port2)
    {
        string key = BuildNormalizedKey(addr1, port1, addr2, port2);
        if (_connections.TryGetValue(key, out var conn))
        {
            conn.State = "CLOSED";
            conn.LastCloseTime = DateTime.Now;
        }
    }

    /// <summary>
    /// 归一化：无论事件方向如何，始终把本机 IP 放在 Local 侧
    /// </summary>
    private TrackedConnection GetOrCreateNormalized(string addr1, int port1, string addr2, int port2)
    {
        string localIp, remoteIp;
        int localPort, remotePort;

        if (IsLocalAddress(addr1))
        {
            localIp = addr1;
            localPort = port1;
            remoteIp = addr2;
            remotePort = port2;
        }
        else
        {
            localIp = addr2;
            localPort = port2;
            remoteIp = addr1;
            remotePort = port1;
        }

        string key = $"{localIp}:{localPort}-{remoteIp}:{remotePort}";
        return _connections.GetOrAdd(key, _ => new TrackedConnection
        {
            LocalIp = localIp,
            LocalPort = localPort,
            RemoteIp = remoteIp,
            RemotePort = remotePort,
            State = "ESTABLISHED",
            LastConnectTime = DateTime.Now
        });
    }

    private string BuildNormalizedKey(string addr1, int port1, string addr2, int port2)
    {
        if (IsLocalAddress(addr1))
            return $"{addr1}:{port1}-{addr2}:{port2}";
        return $"{addr2}:{port2}-{addr1}:{port1}";
    }

    private bool IsLocalAddress(string ip)
    {
        return _localIps != null && _localIps.Contains(ip);
    }

    #endregion

    #region 辅助

    private static HashSet<string> GetLocalIPs()
    {
        var ips = new HashSet<string> { "127.0.0.1", "::1" };
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    ips.Add(addr.Address.ToString());
                }
            }
        }
        catch
        {
        }

        return ips;
    }

    private static void CleanupStaleSession()
    {
        try
        {
            var active = TraceEventSession.GetActiveSessionNames();
            if (active != null && active.Contains(SessionName))
            {
                using (var stale = new TraceEventSession(SessionName))
                    stale.Stop();
                Thread.Sleep(200);
            }
        }
        catch
        {
        }
    }

    #endregion

    /// <summary>内部连接追踪对象</summary>
    private class TrackedConnection
    {
        public string LocalIp;
        public int LocalPort;
        public string RemoteIp;
        public int RemotePort;
        public volatile string State = "ESTABLISHED";
        internal long _bytesIn;
        internal long _bytesOut;

        /// <summary>最后一次连接建立时间</summary>
        public DateTime? LastConnectTime;

        /// <summary>最后一次连接关闭时间</summary>
        public DateTime? LastCloseTime;

        public long BytesIn => Interlocked.Read(ref _bytesIn);
        public long BytesOut => Interlocked.Read(ref _bytesOut);
    }
}

/// <summary>
/// TCP 连接信息（含每连接流量）
/// </summary>
public class TcpConnectionInfo
{
    public string Local { get; set; }
    public string LocalIp { get; set; }
    public string LocalPort { get; set; }
    public string Remote { get; set; }
    public string RemoteIp { get; set; }
    public string RemotePort { get; set; }
    public string State { get; set; }

    public long BytesIn { get; set; } = -1;
    public long BytesOut { get; set; } = -1;

    /// <summary>最后一次连接建立时间</summary>
    public DateTime? LastConnectTime { get; set; }

    /// <summary>最后一次连接关闭时间</summary>
    public DateTime? LastCloseTime { get; set; }

    public string BytesInStr => BytesIn >= 0 ? FormatBytes(BytesIn) : "-";
    public string BytesOutStr => BytesOut >= 0 ? FormatBytes(BytesOut) : "-";

    public string LastConnectTimeStr => LastConnectTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
    public string LastCloseTimeStr => LastCloseTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "-";
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:F1} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:F2} MB";
        double gb = mb / 1024.0;
        if (gb < 1024) return $"{gb:F2} GB";
        return $"{gb / 1024.0:F2} TB";
    }
}