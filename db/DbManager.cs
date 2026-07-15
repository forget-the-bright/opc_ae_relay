using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using opc_ae_relay.config;
using Serilog;

namespace opc_ae_relay.db
{
    /// <summary>
    /// 数据库管理器，统一管理多个数据库实例的生命周期和操作
    /// 类似 MqManager 的设计模式，支持多数据库、可配置、可关闭
    /// 整合了 DBUtil.GetConnection() 功能，保持向后兼容
    /// </summary>
    public static class DbManager
    {
        private static readonly ConcurrentDictionary<string, IDbProvider> _providers = new ConcurrentDictionary<string, IDbProvider>();
        private static string _defaultDbName;

        /// <summary>
        /// 初始化所有启用的数据库提供者
        /// </summary>
        public static void Init()
        {
            var dbConfigs = AppConfigLoader.Config.Databases;
            if (dbConfigs == null || dbConfigs.Count == 0)
            {
                Log.Warning("未配置任何数据库，数据库访问功能不可用");
                return;
            }

            foreach (var dbConfig in dbConfigs)
            {
                if (!dbConfig.Enabled)
                {
                    Log.Information("[DB:{Name}] 已禁用，跳过", dbConfig.Name);
                    continue;
                }

                try
                {
                    var provider = DbProviderFactory.Create(dbConfig);
                    _providers.TryAdd(dbConfig.Name, provider);

                    if (dbConfig.IsDefault)
                    {
                        _defaultDbName = dbConfig.Name;
                    }

                    Log.Information("[DB:{Name}] 初始化成功，类型={Type}", dbConfig.Name, dbConfig.Type);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[DB:{Name}] 初始化失败", dbConfig.Name);
                }
            }

            // 如果没有设置默认数据库，使用第一个启用的数据库
            if (string.IsNullOrEmpty(_defaultDbName) && _providers.Count > 0)
            {
                _defaultDbName = _providers.Keys.First();
                Log.Information("[DB] 未指定默认数据库，使用第一个: {Name}", _defaultDbName);
            }
        }

        /// <summary>
        /// 获取指定名称的数据库提供者
        /// </summary>
        /// <param name="name">数据库名称，为空则使用默认数据库</param>
        /// <returns>IDbProvider 实例</returns>
        public static IDbProvider GetProvider(string name = null)
        {
            var dbName = string.IsNullOrEmpty(name) ? _defaultDbName : name;

            if (string.IsNullOrEmpty(dbName))
            {
                throw new InvalidOperationException("未配置任何可用的数据库");
            }

            if (!_providers.TryGetValue(dbName, out var provider))
            {
                throw new KeyNotFoundException($"未找到数据库提供者: {dbName}");
            }

            return provider;
        }

        /// <summary>
        /// 获取数据库连接（整合原 DBUtil.GetConnection 功能）
        /// 注意：返回的连接需要调用方自行释放
        /// </summary>
        /// <param name="name">数据库名称，为空则使用默认数据库</param>
        /// <returns>IDbConnection 实例</returns>
        public static IDbConnection GetConnection(string name = null)
        {
            var provider = GetProvider(name);
            return provider.CreateConnection();
        }

        /// <summary>
        /// 获取所有已初始化的数据库提供者
        /// </summary>
        public static IReadOnlyCollection<IDbProvider> GetAllProviders()
        {
            return _providers.Values.ToList().AsReadOnly();
        }

        // ====================== 查询操作 ======================

        /// <summary>
        /// 查询返回多条记录（同步）
        /// </summary>
        public static List<T> Query<T>(string sql, object param = null, string dbName = null)
        {
            return GetProvider(dbName).Query<T>(sql, param);
        }

        /// <summary>
        /// 查询返回多条记录（异步）
        /// </summary>
        public static async System.Threading.Tasks.Task<List<T>> QueryAsync<T>(string sql, object param = null, string dbName = null)
        {
            return await GetProvider(dbName).QueryAsync<T>(sql, param);
        }

        /// <summary>
        /// 查询返回单条记录，不存在则抛异常（同步）
        /// </summary>
        public static T QuerySingle<T>(string sql, object param = null, string dbName = null)
        {
            return GetProvider(dbName).QuerySingle<T>(sql, param);
        }

        /// <summary>
        /// 查询返回单条记录，不存在则抛异常（异步）
        /// </summary>
        public static async System.Threading.Tasks.Task<T> QuerySingleAsync<T>(string sql, object param = null, string dbName = null)
        {
            return await GetProvider(dbName).QuerySingleAsync<T>(sql, param);
        }

        /// <summary>
        /// 查询返回单条记录，不存在则返回默认值（同步）
        /// </summary>
        public static T QuerySingleOrDefault<T>(string sql, object param = null, string dbName = null)
        {
            return GetProvider(dbName).QuerySingleOrDefault<T>(sql, param);
        }

        /// <summary>
        /// 查询返回单条记录，不存在则返回默认值（异步）
        /// </summary>
        public static async System.Threading.Tasks.Task<T> QuerySingleOrDefaultAsync<T>(string sql, object param = null, string dbName = null)
        {
            return await GetProvider(dbName).QuerySingleOrDefaultAsync<T>(sql, param);
        }

        /// <summary>
        /// 查询返回第一条记录（同步）
        /// </summary>
        public static T QueryFirst<T>(string sql, object param = null, string dbName = null)
        {
            return GetProvider(dbName).QueryFirst<T>(sql, param);
        }

        /// <summary>
        /// 查询返回第一条记录（异步）
        /// </summary>
        public static async System.Threading.Tasks.Task<T> QueryFirstAsync<T>(string sql, object param = null, string dbName = null)
        {
            return await GetProvider(dbName).QueryFirstAsync<T>(sql, param);
        }

        /// <summary>
        /// 查询返回第一条记录，不存在则返回默认值（同步）
        /// </summary>
        public static T QueryFirstOrDefault<T>(string sql, object param = null, string dbName = null)
        {
            return GetProvider(dbName).QueryFirstOrDefault<T>(sql, param);
        }

        /// <summary>
        /// 查询返回第一条记录，不存在则返回默认值（异步）
        /// </summary>
        public static async System.Threading.Tasks.Task<T> QueryFirstOrDefaultAsync<T>(string sql, object param = null, string dbName = null)
        {
            return await GetProvider(dbName).QueryFirstOrDefaultAsync<T>(sql, param);
        }

        /// <summary>
        /// 执行多结果集查询（同步）
        /// </summary>
        public static SqlMapper.GridReader QueryMultiple(string sql, object param = null, string dbName = null)
        {
            return GetProvider(dbName).QueryMultiple(sql, param);
        }

        /// <summary>
        /// 执行多结果集查询（异步）
        /// </summary>
        public static async System.Threading.Tasks.Task<SqlMapper.GridReader> QueryMultipleAsync(string sql, object param = null, string dbName = null)
        {
            return await GetProvider(dbName).QueryMultipleAsync(sql, param);
        }

        // ====================== 增删改操作 ======================

        /// <summary>
        /// 执行增删改操作，返回受影响行数（同步）
        /// </summary>
        public static int Execute(string sql, object param = null, string dbName = null)
        {
            return GetProvider(dbName).Execute(sql, param);
        }

        /// <summary>
        /// 执行增删改操作，返回受影响行数（异步）
        /// </summary>
        public static async System.Threading.Tasks.Task<int> ExecuteAsync(string sql, object param = null, string dbName = null)
        {
            return await GetProvider(dbName).ExecuteAsync(sql, param);
        }

        /// <summary>
        /// 执行查询并返回单个值（同步）
        /// </summary>
        public static object ExecuteScalar(string sql, object param = null, string dbName = null)
        {
            return GetProvider(dbName).ExecuteScalar(sql, param);
        }

        /// <summary>
        /// 执行查询并返回单个值（异步）
        /// </summary>
        public static async System.Threading.Tasks.Task<object> ExecuteScalarAsync(string sql, object param = null, string dbName = null)
        {
            return await GetProvider(dbName).ExecuteScalarAsync(sql, param);
        }

        /// <summary>
        /// 执行查询并返回单个值（泛型，同步）
        /// </summary>
        public static T ExecuteScalar<T>(string sql, object param = null, string dbName = null)
        {
            return GetProvider(dbName).ExecuteScalar<T>(sql, param);
        }

        /// <summary>
        /// 执行查询并返回单个值（泛型，异步）
        /// </summary>
        public static async System.Threading.Tasks.Task<T> ExecuteScalarAsync<T>(string sql, object param = null, string dbName = null)
        {
            return await GetProvider(dbName).ExecuteScalarAsync<T>(sql, param);
        }

        // ====================== 事务操作 ======================

        /// <summary>
        /// 在事务中执行操作（同步）
        /// </summary>
        public static void Transaction(Action<IDbConnection, IDbTransaction> action, string dbName = null)
        {
            GetProvider(dbName).Transaction(action);
        }

        /// <summary>
        /// 在事务中执行操作（异步）
        /// </summary>
        public static async System.Threading.Tasks.Task TransactionAsync(Func<IDbConnection, IDbTransaction, System.Threading.Tasks.Task> action, string dbName = null)
        {
            await GetProvider(dbName).TransactionAsync(action);
        }

        // ====================== 实体操作 ======================

        /// <summary>
        /// 插入实体并返回自增 ID（同步）
        /// </summary>
        public static int InsertGetId<T>(T entity, string dbName = null) where T : class
        {
            return GetProvider(dbName).InsertGetId(entity);
        }

        /// <summary>
        /// 插入实体并返回自增 ID（异步）
        /// </summary>
        public static async System.Threading.Tasks.Task<int> InsertGetIdAsync<T>(T entity, string dbName = null) where T : class
        {
            return await GetProvider(dbName).InsertGetIdAsync(entity);
        }

        /// <summary>
        /// 根据实体更新记录（同步）
        /// </summary>
        public static int Update<T>(T entity, object key, string dbName = null) where T : class
        {
            return GetProvider(dbName).Update(entity, key);
        }

        /// <summary>
        /// 根据实体更新记录（异步）
        /// </summary>
        public static async System.Threading.Tasks.Task<int> UpdateAsync<T>(T entity, object key, string dbName = null) where T : class
        {
            return await GetProvider(dbName).UpdateAsync(entity, key);
        }

        /// <summary>
        /// 根据主键删除记录（同步）
        /// </summary>
        public static int Delete<T>(object key, string dbName = null) where T : class
        {
            return GetProvider(dbName).Delete<T>(key);
        }

        /// <summary>
        /// 根据主键删除记录（异步）
        /// </summary>
        public static async System.Threading.Tasks.Task<int> DeleteAsync<T>(object key, string dbName = null) where T : class
        {
            return await GetProvider(dbName).DeleteAsync<T>(key);
        }

        /// <summary>
        /// 关闭所有数据库连接并释放资源
        /// </summary>
        public static void Shutdown()
        {
            foreach (var provider in _providers.Values)
            {
                try
                {
                    provider.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[DB] 关闭数据库提供者 {Name} 时异常", provider.Name);
                }
            }

            _providers.Clear();
            _defaultDbName = null;
            Log.Information("所有数据库提供者已关闭");
        }
    }
}