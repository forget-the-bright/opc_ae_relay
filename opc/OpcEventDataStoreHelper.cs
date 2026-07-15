using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Opc.UaFx;

namespace opc_ae_relay.opc;

public static class OpcEventDataStoreHelper
{
    /// <summary>
    ///     实时反射获取 OpcEvent 的 protected internal DataStore
    ///     无静态构造，不会触发类型初始化异常
    /// </summary>
    /// <param name="evt">OPC事件对象</param>
    /// <returns>底层IOpcReadOnlyNodeDataStore，失败返回null</returns>
    public static IOpcReadOnlyNodeDataStore TryGetDataStore(this OpcEvent evt)
    {
        if (evt == null)
            return null;

        try
        {
            // 反射查找 DataStore 属性，字符串规避nameof权限报错
            var dataStoreProp = typeof(OpcEvent)
                .GetProperty(
                    "DataStore",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );

            if (dataStoreProp == null)
                return null;

            // 取值转换接口
            var rawValue = dataStoreProp.GetValue(evt);
            return rawValue as IOpcReadOnlyNodeDataStore;
        }
        catch (Exception)
        {
            // 任意反射失败直接返回null，不抛出全局初始化异常
            return null;
        }
    }

    /// <summary>
    ///     通过DataStore读取指定字段值
    /// </summary>
    public static T GetDataStoreField<T>(this OpcEvent evt, string fieldName)
    {
        var store = evt.TryGetDataStore();
        if (store == null)
            return default;

        try
        {
            // 查找IOpcReadOnlyNodeDataStore的Get<T>(string)泛型方法
            var getMethod = store.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m =>
                    m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(string)
                );

            if (getMethod == null)
                return default;

            // 构造泛型方法调用
            var genericGet = getMethod.MakeGenericMethod(typeof(T));
            var result = genericGet.Invoke(store, new object[] { fieldName });
            return result == null ? default : (T)result;
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    ///     获取DataStore内所有原始值数组（无字段名，仅调试）
    /// </summary>
    public static object[] GetAllDataStoreValues(this OpcEvent evt)
    {
        var store = evt.TryGetDataStore();
        if (store == null)
            return Array.Empty<object>();

        try
        {
            return store.ToArray();
        }
        catch
        {
            return Array.Empty<object>();
        }
    }

    /// <summary>
    ///     object[] 转为 [[0],[1],[2]] 格式字符串
    /// </summary>
    public static string FormatObjectArray(object[] arr)
    {
        if (arr == null || arr.Length == 0)
            return "[]";

        var sb = new StringBuilder();
        sb.Append('[');
        for (var i = 0; i < arr.Length; i++)
        {
            var val = arr[i];
            var strVal = val == null ? "" : val.ToString();
            sb.Append($"[{strVal}]");
            // 非最后一个元素加逗号分隔
            if (i != arr.Length - 1)
                sb.Append(',');
        }

        sb.Append(']');
        return sb.ToString();
    }
}