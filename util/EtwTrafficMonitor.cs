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
using Serilog;

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
        foreach (var kv in _connections)
        {
            var c = kv.Value;
            if (c.BytesIn == 0 && c.BytesOut == 0 && c.State.Equals("CLOSED"))
            {
                continue;
            }

            var opcServerConfig = AppConfigLoader.GetOPCServers().Find(s => s.IP == c.RemoteIp);
            if (opcServerConfig != null && c.State.Equals("CLOSED"))
            {
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
                BytesOut = c.BytesOut
            });
        }

        var sortedList = list
            // 1. 先按远程 IP 分组排序（相同 IP 放一起）
            .OrderBy(x => x.RemoteIp)
            // 2. 再按 ESTABLISHED 状态优先
            .ThenByDescending(x => x.State == "ESTABLISHED")
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
            string key = BuildNormalizedKey(
                data.saddr.ToString(), data.sport,
                data.daddr.ToString(), data.dport);
            if (_connections.TryGetValue(key, out var conn))
                conn.State = "CLOSED";
        }
        catch
        {
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
            State = "ESTABLISHED"
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

    public string BytesInStr => BytesIn >= 0 ? FormatBytes(BytesIn) : "-";
    public string BytesOutStr => BytesOut >= 0 ? FormatBytes(BytesOut) : "-";

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