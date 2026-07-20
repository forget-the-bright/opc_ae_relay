using System;
using System.Diagnostics;

namespace opc_ae_relay.util;

public class CpuUtil
{
    // CPU 计算用的上次采样数据
    private static TimeSpan _lastCpuTime = TimeSpan.Zero;
    private static DateTime _lastCpuCheck = DateTime.MinValue;


    public static (double cpuPercent, string uptimeStr) GetInfo(Process proc)
    {
        // CPU 使用率计算（两次采样差值）
        double cpuPercent = 0;
        var currentCpuTime = proc.TotalProcessorTime;
        var now = DateTime.UtcNow;
        if (_lastCpuCheck != DateTime.MinValue)
        {
            var cpuDelta = (currentCpuTime - _lastCpuTime).TotalMilliseconds;
            var timeDelta = (now - _lastCpuCheck).TotalMilliseconds;
            if (timeDelta > 0)
                cpuPercent = Math.Round(cpuDelta / (Environment.ProcessorCount * timeDelta) * 100, 1);
        }

        _lastCpuTime = currentCpuTime;
        _lastCpuCheck = now;

        // 运行时长
        var uptime = DateTime.Now - proc.StartTime;
        string uptimeStr;
        if (uptime.TotalDays >= 1)
            uptimeStr = $"{(int)uptime.TotalDays}天{uptime.Hours}时{uptime.Minutes}分";
        else if (uptime.TotalHours >= 1)
            uptimeStr = $"{(int)uptime.TotalHours}时{uptime.Minutes}分{uptime.Seconds}秒";
        else
            uptimeStr = $"{uptime.Minutes}分{uptime.Seconds}秒";
        return (cpuPercent, uptimeStr);
    }
}