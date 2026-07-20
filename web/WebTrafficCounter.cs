using System.Threading;

namespace opc_ae_relay.web;

#region 流量计数器

/// <summary>
/// Web 应用层流量统计（由 TrafficTrackingModule 全局驱动）
/// </summary>
public static class WebTrafficCounter
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

#endregion