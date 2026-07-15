using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using opc_ae_relay.db;

namespace opc_ae_relay.util
{
    /// <summary>
    /// 数据库工具类（兼容层）
    /// 内部委托给 DbManager 实现，保持向后兼容
    /// 建议新代码直接使用 DbManager
    /// </summary>
    public static class DBUtil
    {
        /// <summary>
        /// 获取连接（整合到 DbManager，保持向后兼容）
        /// 注意：返回的连接需要调用方自行释放
        /// </summary>
        public static IDbConnection GetConnection(string dbName = null)
        {
            return DbManager.GetConnection(dbName);
        }
        

        // ====================== 查询 ======================
        /*
         * var list = DBUtil.Query<User>("select * from Users");
         */
        public static List<T> Query<T>(string sql, object param = null, string dbName = null)
        {
            return DbManager.Query<T>(sql, param, dbName);
        }

        public static async Task<List<T>> QueryAsync<T>(string sql, object param = null, string dbName = null)
        {
            return await DbManager.QueryAsync<T>(sql, param, dbName);
        }

        public static T QueryFirst<T>(string sql, object param = null, string dbName = null)
        {
            return DbManager.QueryFirst<T>(sql, param, dbName);
        }

        public static async Task<T> QueryFirstAsync<T>(string sql, object param = null, string dbName = null)
        {
            return await DbManager.QueryFirstAsync<T>(sql, param, dbName);
        }

        /*
         * var user = DBUtil.QueryFirstOrDefault<User>("select * from Users where Id=@Id", new { Id = 1 });
         */
        public static T QueryFirstOrDefault<T>(string sql, object param = null, string dbName = null)
        {
            return DbManager.QueryFirstOrDefault<T>(sql, param, dbName);
        }

        public static async Task<T> QueryFirstOrDefaultAsync<T>(string sql, object param = null, string dbName = null)
        {
            return await DbManager.QueryFirstOrDefaultAsync<T>(sql, param, dbName);
        }

        public static T QuerySingle<T>(string sql, object param = null, string dbName = null)
        {
            return DbManager.QuerySingle<T>(sql, param, dbName);
        }

        public static async Task<T> QuerySingleAsync<T>(string sql, object param = null, string dbName = null)
        {
            return await DbManager.QuerySingleAsync<T>(sql, param, dbName);
        }

        public static T QuerySingleOrDefault<T>(string sql, object param = null, string dbName = null)
        {
            return DbManager.QuerySingleOrDefault<T>(sql, param, dbName);
        }

        public static async Task<T> QuerySingleOrDefaultAsync<T>(string sql, object param = null, string dbName = null)
        {
            return await DbManager.QuerySingleOrDefaultAsync<T>(sql, param, dbName);
        }

        // ====================== 增删改 ======================
        /*
            var rows = DBUtil.Execute("update Users set Age=@Age where Id=@Id", new { Id = 1, Age = 21 });
         */
        public static int Execute(string sql, object param = null, string dbName = null)
        {
            return DbManager.Execute(sql, param, dbName);
        }

        public static async Task<int> ExecuteAsync(string sql, object param = null, string dbName = null)
        {
            return await DbManager.ExecuteAsync(sql, param, dbName);
        }

        /*
            var id = DBUtil.ExecuteScalar<int>(@"
            insert into Users(Name,Age)
            values(@Name,@Age);
            select SCOPE_IDENTITY();",
            new { Name = "张三", Age = 20 });
         */
        public static object ExecuteScalar(string sql, object param = null, string dbName = null)
        {
            return DbManager.ExecuteScalar(sql, param, dbName);
        }

        public static async Task<object> ExecuteScalarAsync(string sql, object param = null, string dbName = null)
        {
            return await DbManager.ExecuteScalarAsync(sql, param, dbName);
        }

        public static T ExecuteScalar<T>(string sql, object param = null, string dbName = null)
        {
            return DbManager.ExecuteScalar<T>(sql, param, dbName);
        }

        public static async Task<T> ExecuteScalarAsync<T>(string sql, object param = null, string dbName = null)
        {
            return await DbManager.ExecuteScalarAsync<T>(sql, param, dbName);
        }

        // ====================== 事务 ======================
        /*
            DBUtil.Transaction((conn, tran) =>
            {
                conn.Execute("update ...", new { Id = 1 }, tran);
                conn.Execute("insert ...", new { ... }, tran);
            });
         */
        public static void Transaction(Action<IDbConnection, IDbTransaction> action, string dbName = null)
        {
            DbManager.Transaction(action, dbName);
        }

        public static async Task TransactionAsync(Func<IDbConnection, IDbTransaction, Task> action, string dbName = null)
        {
            await DbManager.TransactionAsync(action, dbName);
        }


        // ====================== 通用 Insert<T> 根据实体新增 ======================
        public static int Insert<T>(T entity, string dbName = null) where T : class
        {
            var type = typeof(T);
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => (p.CanRead && p.Name != "Id") || p.Name != "ID")
                .ToList();

            var columnNames = string.Join(", ", props.Select(p => p.Name));
            var paramNames = string.Join(", ", props.Select(p => "@" + p.Name));

            var sql = $"INSERT INTO {type.Name} ({columnNames}) VALUES ({paramNames})";

            return DbManager.Execute(sql, entity, dbName);
        }

        public static async Task<int> InsertAsync<T>(T entity, string dbName = null) where T : class
        {
            var type = typeof(T);
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => (p.CanRead && p.Name != "Id") || p.Name != "ID")
                .ToList();

            var columnNames = string.Join(", ", props.Select(p => p.Name));
            var paramNames = string.Join(", ", props.Select(p => "@" + p.Name));

            var sql = $"INSERT INTO {type.Name} ({columnNames}) VALUES ({paramNames})";

            return await DbManager.ExecuteAsync(sql, entity, dbName);
        }

        // ====================== 通用 Insert 并返回自增 ID ======================
        public static int InsertGetId<T>(T entity, string dbName = null) where T : class
        {
            return DbManager.InsertGetId(entity, dbName);
        }

        public static async Task<int> InsertGetIdAsync<T>(T entity, string dbName = null) where T : class
        {
            return await DbManager.InsertGetIdAsync(entity, dbName);
        }

        public static int Update<T>(T entity, object key, string dbName = null) where T : class
        {
            return DbManager.Update(entity, key, dbName);
        }

        public static async Task<int> UpdateAsync<T>(T entity, object key, string dbName = null) where T : class
        {
            return await DbManager.UpdateAsync(entity, key, dbName);
        }

        public static int Delete<T>(object key, string dbName = null) where T : class
        {
            return DbManager.Delete<T>(key, dbName);
        }

        public static async Task<int> DeleteAsync<T>(object key, string dbName = null) where T : class
        {
            return await DbManager.DeleteAsync<T>(key, dbName);
        }
    }
}