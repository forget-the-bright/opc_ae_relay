using System;
using System.Text.RegularExpressions;
using opc_ae_relay.config;

namespace opc_ae_relay.opc;

public class AlarmInfo
{
    /// 标签名称 WI-11301
    public string TagName { get; set; } = string.Empty;

    /// 中文/设备描述
    public string Description { get; set; } = string.Empty;

    /// 字段类型 PV/SP/OP/MV/IN
    public string FieldType { get; set; } = string.Empty;

    /// 等号后数值/状态文本，无=则空
    public string Value { get; set; } = string.Empty;

    /// 单位
    public string Unit { get; set; } = string.Empty;

    /// 末尾全部后缀完整保留
    public string Suffix { get; set; } = string.Empty;

    public AlarmRule MatchedRule { get; set; } // 匹配到的规则

    /// 全局匹配解析方法
    public static AlarmInfo ParseAlarm(string input, string eventTypeId)
    {
        var result = new AlarmInfo();
        var raw = input.Trim();

        var alarmRule = AlarmConfigLoader.Match(raw);
        if (alarmRule != null) result.MatchedRule = alarmRule;
        // 分支1：包含 = 号（数值/状态格式）
        if (raw.Contains("="))
        {
            // 修复正则：等号后(.+?)包含值+单位，两个以上空格才分割后缀
            var regEqual = @"^(\S+)\s+(.+?)\s+(\w+)\s*=\s*(.+?)(?=\s{2,}.*)?(\s{2}.*)?$";
            var match = Regex.Match(raw, regEqual, RegexOptions.Singleline);
            if (match.Success)
            {
                result.TagName = match.Groups[1].Value.Trim();
                result.Description = match.Groups[2].Value.Trim();
                result.FieldType = match.Groups[3].Value.Trim();
                var valRaw = match.Groups[4].Value.Trim();
                result.Suffix = match.Groups[5].Value?.Trim() ?? "";

                // 拆分 valRaw 提取数值、单位
                var valParts = valRaw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (valParts.Length >= 1 && double.TryParse(valParts[0], out _))
                {
                    result.Value = valParts[0];
                    // 存在第二段就是单位
                    result.Unit = valParts.Length >= 2 ? valParts[1] : "";
                }
                else
                {
                    // 文字状态无单位
                    result.Value = valRaw;
                    result.Unit = "";
                }

                return result;
            }
        }

        // 分支2：无= 纯设备告警，修复正则贪婪匹配，解决带符号描述匹配失败
        // ^(\S+) 标签，\s+(.+)整段描述，最后\s+(\w+)固定英文字段，剩余全部后缀
        var regNoEqual = @"^(\S+)\s+(.+)\s+(\w+)(\s+.*)?$";
        var matchNoEq = Regex.Match(raw, regNoEqual, RegexOptions.Singleline);
        if (matchNoEq.Success)
        {
            result.TagName = matchNoEq.Groups[1].Value.Trim();
            result.Description = matchNoEq.Groups[2].Value.Trim();
            result.FieldType = matchNoEq.Groups[3].Value.Trim();
            result.Value = "";
            result.Unit = "";
            result.Suffix = matchNoEq.Groups[4].Value?.Trim() ?? "";
            return result;
        }

        // 兜底：两种正则都匹配失败，原始文本塞入Suffix，不抛异常阻断事件
        result.Suffix = raw;
        return result;
    }
}