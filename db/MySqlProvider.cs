using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Dapper;
using MySqlConnector;
using Serilog;

namespace opc_ae_relay.db
{
    /// <summary>
    /// MySQL 数据库提供者实现（基于 MySqlConnector 驱动）
    /// 基于 Dapper 封装，提供完整的同步和异步操作
    /// </summary>
    public class MySqlProvider : IDbProvider
    {
        private readonly string _connectionString;
        private MySqlConnectionStringBuilder builder;
        private bool _disposed;

        public string Name { get; }
        public string DbType => "mysql";

        public MySqlProvider(string name, string connectionString)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            builder = new MySqlConnectionStringBuilder(connectionString);
        }

        public IDbConnection CreateConnection()
        {
            return new MySqlConnection(_connectionString);
        }

        public int GetDatabasePort()
        {
            
            // MySQL 连接字符串自带 Port 属性，默认 3306
            return (int)(builder.Port == 0 ? 3306 : builder.Port);
        }

        // ====================== 查询操作 ======================

        public List<T> Query<T>(string sql, object param = null, IDbTransaction transaction = null)
        {
            using (var conn = CreateConnection())
            {
                return conn.Query<T>(sql, param, transaction).ToList();
            }
        }

        public async Task<List<T>> QueryAsync<T>(string sql, object param = null, IDbTransaction transaction = null)
        {
            using (var conn = CreateConnection())
            {
                var result = await conn.QueryAsync<T>(sql, param, transaction);
                return result.ToList();
            }
        }

        public T QuerySingle<T>(string sql, object param = null, IDbTransaction transaction = null)
        {
            using (var conn = CreateConnection())
            {
                return conn.QuerySingle<T>(sql, param, transaction);
            }
        }

        public async Task<T> QuerySingleAsync<T>(string sql, object param = null, IDbTransaction transaction = null)
        {
            using (var conn = CreateConnection())
            {
                return await conn.QuerySingleAsync<T>(sql, param, transaction);
            }
        }

        public T QuerySingleOrDefault<T>(string sql, object param = null, IDbTransaction transaction = null)
        {
            using (var conn = CreateConnection())
            {
                return conn.QuerySingleOrDefault<T>(sql, param, transaction);
            }
        }

        public async Task<T> QuerySingleOrDefaultAsync<T>(string sql, object param = null, IDbTransaction transaction = null)
        {
            using (var conn = CreateConnection())
            {
                return await conn.QuerySingleOrDefaultAsync<T>(sql, param, transaction);
            }
        }

        public T QueryFirst<T>(string sql, object param = null, IDbTransaction transaction = null)
        {
            using (var conn = CreateConnection())
            {
                return conn.QueryFirst<T>(sql, param, transaction);
            }
        }

        public async Task<T> QueryFirstAsync<T>(string sql, object param = null, IDbTransaction transaction = null)
        {
            using (var conn = CreateConnection())
            {
                return await conn.QueryFirstAsync<T>(sql, param, transaction);
            }
        }

        public T QueryFirstOrDefault<T>(string sql, object param = null, IDbTransaction transaction = null)
        {
            using (var conn = CreateConnection())
            {
                return conn.QueryFirstOrDefault<T>(sql, param, transaction);
            }
        }

        public async Task<T> QueryFirstOrDefaultAsync<T>(string sql, object param = null, IDbTransaction transaction = null)
        {
            using (var conn = CreateConnection())
            {
                return await conn.QueryFirstOrDefaultAsync<T>(sql, param, transaction);
            }
        }

        public SqlMapper.GridReader QueryMultiple(string sql, object param = null, IDbTransaction transaction = null)
        {
            var conn = CreateConnection();
            conn.Open();
            return conn.QueryMultiple(sql, param, transaction);
        }

        public async Task<SqlMapper.GridReader> QueryMultipleAsync(string sql, object param = null, IDbTransaction transaction = null)
        {
            var conn = CreateConnection();
            await ((MySqlConnection)conn).OpenAsync();
            return await conn.QueryMultipleAsync(sql, param, transaction);
        }

        // ====================== 增删改操作 ======================

        public int Execute(string sql, object param = null, IDbTransaction transaction = null)
        {
            using (var conn = CreateConnection())
            {
                return conn.Execute(sql, param, transaction);
            }
        }

        public async Task<int> ExecuteAsync(string sql, object param = null, IDbTransaction transaction = null)
        {
            using (var conn = CreateConnection())
            {
                return await conn.ExecuteAsync(sql, param, transaction);
            }
        }

        public object ExecuteScalar(string sql, object param = null, IDbTransaction transaction = null)
        {
            using (var conn = CreateConnection())
            {
                return conn.ExecuteScalar(sql, param, transaction);
            }
        }

        public async Task<object> ExecuteScalarAsync(string sql, object param = null, IDbTransaction transaction = null)
        {
            using (var conn = CreateConnection())
            {
                return await conn.ExecuteScalarAsync(sql, param, transaction);
            }
        }

        public T ExecuteScalar<T>(string sql, object param = null, IDbTransaction transaction = null)
        {
            using (var conn = CreateConnection())
            {
                return conn.ExecuteScalar<T>(sql, param, transaction);
            }
        }

        public async Task<T> ExecuteScalarAsync<T>(string sql, object param = null, IDbTransaction transaction = null)
        {
            using (var conn = CreateConnection())
            {
                return await conn.ExecuteScalarAsync<T>(sql, param, transaction);
            }
        }

        // ====================== 事务操作 ======================

        public void Transaction(Action<IDbConnection, IDbTransaction> action)
        {
            using (var conn = CreateConnection())
            {
                conn.Open();
                using (var tran = conn.BeginTransaction())
                {
                    try
                    {
                        action(conn, tran);
                        tran.Commit();
                    }
                    catch
                    {
                        tran.Rollback();
                        throw;
                    }
                }
            }
        }

        public async Task TransactionAsync(Func<IDbConnection, IDbTransaction, Task> action)
        {
            using (var conn = CreateConnection())
            {
                await ((MySqlConnection)conn).OpenAsync();
                using (var tran = conn.BeginTransaction())
                {
                    try
                    {
                        await action(conn, tran);
                        tran.Commit();
                    }
                    catch
                    {
                        tran.Rollback();
                        throw;
                    }
                }
            }
        }

        // ====================== 实体操作 ======================

        public int InsertGetId<T>(T entity) where T : class
        {
            var type = typeof(T);
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.Name != "Id" && p.Name != "ID")
                .ToList();

            var columnNames = string.Join(", ", props.Select(p => p.Name));
            var paramNames = string.Join(", ", props.Select(p => "@" + p.Name));

            var sql = $"INSERT INTO {type.Name} ({columnNames}) VALUES ({paramNames}); SELECT LAST_INSERT_ID();";

            using (var conn = CreateConnection())
            {
                return conn.ExecuteScalar<int>(sql, entity);
            }
        }

        public async Task<int> InsertGetIdAsync<T>(T entity) where T : class
        {
            var type = typeof(T);
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.Name != "Id" && p.Name != "ID")
                .ToList();

            var columnNames = string.Join(", ", props.Select(p => p.Name));
            var paramNames = string.Join(", ", props.Select(p => "@" + p.Name));

            var sql = $"INSERT INTO {type.Name} ({columnNames}) VALUES ({paramNames}); SELECT LAST_INSERT_ID();";

            using (var conn = CreateConnection())
            {
                return await conn.ExecuteScalarAsync<int>(sql, entity);
            }
        }

        public int Update<T>(T entity, object key) where T : class
        {
            var type = typeof(T);
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.Name != "Id" && p.Name != "ID")
                .ToList();

            var setParts = string.Join(", ", props.Select(p => $"{p.Name}=@{p.Name}"));
            var sql = $"UPDATE {type.Name} SET {setParts} WHERE Id=@Id";

            using (var conn = CreateConnection())
            {
                return conn.Execute(sql, entity);
            }
        }

        public async Task<int> UpdateAsync<T>(T entity, object key) where T : class
        {
            var type = typeof(T);
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.Name != "Id" && p.Name != "ID")
                .ToList();

            var setParts = string.Join(", ", props.Select(p => $"{p.Name}=@{p.Name}"));
            var sql = $"UPDATE {type.Name} SET {setParts} WHERE Id=@Id";

            using (var conn = CreateConnection())
            {
                return await conn.ExecuteAsync(sql, entity);
            }
        }

        public int Delete<T>(object key) where T : class
        {
            var type = typeof(T);
            var sql = $"DELETE FROM {type.Name} WHERE Id=@Id";

            using (var conn = CreateConnection())
            {
                return conn.Execute(sql, new { Id = key });
            }
        }

        public async Task<int> DeleteAsync<T>(object key) where T : class
        {
            var type = typeof(T);
            var sql = $"DELETE FROM {type.Name} WHERE Id=@Id";

            using (var conn = CreateConnection())
            {
                return await conn.ExecuteAsync(sql, new { Id = key });
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Log.Information("[DB:{Name}] MySqlProvider 已释放", Name);
        }
    }
}