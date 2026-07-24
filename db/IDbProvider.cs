using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;

namespace opc_ae_relay.db
{
    /// <summary>
    /// 数据库提供者接口，定义统一的数据库操作契约
    /// 支持 SQL Server、MySQL 等多种数据库驱动的扩展
    /// </summary>
    public interface IDbProvider : IDisposable
    {
        /// <summary>
        /// 获取数据库实例名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 获取数据库类型标识（如 sqlserver, mysql）
        /// </summary>
        string DbType { get; }

        /// <summary>
        /// 创建新的数据库连接（由调用方负责释放）
        /// </summary>
        IDbConnection CreateConnection();
        
        // 新增：获取数据库端口
        int GetDatabasePort();

        // ====================== 查询操作 ======================

        /// <summary>
        /// 查询返回多条记录（同步）
        /// </summary>
        List<T> Query<T>(string sql, object param = null, IDbTransaction transaction = null);

        /// <summary>
        /// 查询返回多条记录（异步）
        /// </summary>
        Task<List<T>> QueryAsync<T>(string sql, object param = null, IDbTransaction transaction = null);

        /// <summary>
        /// 查询返回单条记录，不存在则抛异常（同步）
        /// </summary>
        T QuerySingle<T>(string sql, object param = null, IDbTransaction transaction = null);

        /// <summary>
        /// 查询返回单条记录，不存在则抛异常（异步）
        /// </summary>
        Task<T> QuerySingleAsync<T>(string sql, object param = null, IDbTransaction transaction = null);

        /// <summary>
        /// 查询返回单条记录，不存在则返回默认值（同步）
        /// </summary>
        T QuerySingleOrDefault<T>(string sql, object param = null, IDbTransaction transaction = null);

        /// <summary>
        /// 查询返回单条记录，不存在则返回默认值（异步）
        /// </summary>
        Task<T> QuerySingleOrDefaultAsync<T>(string sql, object param = null, IDbTransaction transaction = null);

        /// <summary>
        /// 查询返回第一条记录（同步）
        /// </summary>
        T QueryFirst<T>(string sql, object param = null, IDbTransaction transaction = null);

        /// <summary>
        /// 查询返回第一条记录（异步）
        /// </summary>
        Task<T> QueryFirstAsync<T>(string sql, object param = null, IDbTransaction transaction = null);

        /// <summary>
        /// 查询返回第一条记录，不存在则返回默认值（同步）
        /// </summary>
        T QueryFirstOrDefault<T>(string sql, object param = null, IDbTransaction transaction = null);

        /// <summary>
        /// 查询返回第一条记录，不存在则返回默认值（异步）
        /// </summary>
        Task<T> QueryFirstOrDefaultAsync<T>(string sql, object param = null, IDbTransaction transaction = null);

        /// <summary>
        /// 执行多结果集查询（同步）
        /// </summary>
        SqlMapper.GridReader QueryMultiple(string sql, object param = null, IDbTransaction transaction = null);

        /// <summary>
        /// 执行多结果集查询（异步）
        /// </summary>
        Task<SqlMapper.GridReader> QueryMultipleAsync(string sql, object param = null, IDbTransaction transaction = null);

        // ====================== 增删改操作 ======================

        /// <summary>
        /// 执行增删改操作，返回受影响行数（同步）
        /// </summary>
        int Execute(string sql, object param = null, IDbTransaction transaction = null);

        /// <summary>
        /// 执行增删改操作，返回受影响行数（异步）
        /// </summary>
        Task<int> ExecuteAsync(string sql, object param = null, IDbTransaction transaction = null);

        /// <summary>
        /// 执行查询并返回单个值（同步）
        /// </summary>
        object ExecuteScalar(string sql, object param = null, IDbTransaction transaction = null);

        /// <summary>
        /// 执行查询并返回单个值（异步）
        /// </summary>
        Task<object> ExecuteScalarAsync(string sql, object param = null, IDbTransaction transaction = null);

        /// <summary>
        /// 执行查询并返回单个值（泛型，同步）
        /// </summary>
        T ExecuteScalar<T>(string sql, object param = null, IDbTransaction transaction = null);

        /// <summary>
        /// 执行查询并返回单个值（泛型，异步）
        /// </summary>
        Task<T> ExecuteScalarAsync<T>(string sql, object param = null, IDbTransaction transaction = null);

        // ====================== 事务操作 ======================

        /// <summary>
        /// 在事务中执行操作（同步）
        /// </summary>
        void Transaction(Action<IDbConnection, IDbTransaction> action);

        /// <summary>
        /// 在事务中执行操作（异步）
        /// </summary>
        Task TransactionAsync(Func<IDbConnection, IDbTransaction, Task> action);

        // ====================== 实体操作 ======================

        /// <summary>
        /// 插入实体并返回自增 ID（同步）
        /// </summary>
        int InsertGetId<T>(T entity) where T : class;

        /// <summary>
        /// 插入实体并返回自增 ID（异步）
        /// </summary>
        Task<int> InsertGetIdAsync<T>(T entity) where T : class;

        /// <summary>
        /// 根据实体更新记录（同步）
        /// </summary>
        int Update<T>(T entity, object key) where T : class;

        /// <summary>
        /// 根据实体更新记录（异步）
        /// </summary>
        Task<int> UpdateAsync<T>(T entity, object key) where T : class;

        /// <summary>
        /// 根据主键删除记录（同步）
        /// </summary>
        int Delete<T>(object key) where T : class;

        /// <summary>
        /// 根据主键删除记录（异步）
        /// </summary>
        Task<int> DeleteAsync<T>(object key) where T : class;
    }
}