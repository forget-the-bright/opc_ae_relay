using Dapper;
using opcLearn.config;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace opcLearn.util
{
    public static class DBUtil
    {
        /// <summary>
        /// 获取连接
        /// </summary>
        public static SqlConnection GetConnection(string dbName = "Default")
        {
            var connStr = AppConfigLoader.GetDbConnection(dbName);
            return new SqlConnection(connStr);
        }

        // ====================== 查询 ======================
        /*
         * var list = DBUtil.Query<User>("select * from Users");
         */
        public static List<T> Query<T>(string sql, object param = null)
        {
            using (var conn = GetConnection())
            {
                return conn.Query<T>(sql, param).ToList();
            }
        }

        public static T QueryFirst<T>(string sql, object param = null)
        {
            using (var conn = GetConnection())
            {
                return conn.QueryFirst<T>(sql, param);
            }
        }
        /*
         * var user = DBUtil.QueryFirstOrDefault<User>("select * from Users where Id=@Id", new { Id = 1 });
         */
        public static T QueryFirstOrDefault<T>(string sql, object param = null)
        {
            using (var conn = GetConnection())
            {
                return conn.QueryFirstOrDefault<T>(sql, param);
            }
        }

        // ====================== 增删改 ======================
        /*
         var rows = DBUtil.Execute("update Users set Age=@Age where Id=@Id", new { Id = 1, Age = 21 });
         */
        public static int Execute(string sql, object param = null)
        {
            using (var conn = GetConnection())
            {
                return conn.Execute(sql, param);
            }
        }
        /*
            var id = DBUtil.ExecuteScalar<int>(@"
            insert into Users(Name,Age)
            values(@Name,@Age);
            select SCOPE_IDENTITY();",
            new { Name = "张三", Age = 20 });
         */
        public static object ExecuteScalar(string sql, object param = null)
        {
            using (var conn = GetConnection())
            {
                return conn.ExecuteScalar(sql, param);
            }
        }

        public static T ExecuteScalar<T>(string sql, object param = null)
        {
            using (var conn = GetConnection())
            {
                return conn.ExecuteScalar<T>(sql, param);
            }
        }

        // ====================== 事务 ======================
        /*
            DBUtil.Transaction((conn, tran) =>
            {
                conn.Execute("update ...", new { Id = 1 }, tran);
                conn.Execute("insert ...", new { ... }, tran);
            });
         */
        public static void Transaction(Action<SqlConnection, IDbTransaction> action)
        {
            using (var conn = GetConnection())
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


        // ====================== 通用 Insert<T> 根据实体新增 ======================
        public static int Insert<T>(T entity) where T : class
        {
            var type = typeof(T);
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Where(p => p.CanRead && p.Name != "Id" || p.Name != "ID") // 自增主键一般不插
                            .ToList();

            var columnNames = string.Join(", ", props.Select(p => p.Name));
            var paramNames = string.Join(", ", props.Select(p => "@" + p.Name));

            var sql = $"INSERT INTO {type.Name} ({columnNames}) VALUES ({paramNames})";

            using (var conn = GetConnection())
            {
                return conn.Execute(sql, entity);
            }
        }

        // ====================== 通用 Insert 并返回自增 ID ======================
        public static int InsertGetId<T>(T entity) where T : class
        {
            var type = typeof(T);
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Where(p => p.CanRead && p.Name != "Id" && p.Name != "ID")
                            .ToList();

            var columnNames = string.Join(", ", props.Select(p => p.Name));
            var paramNames = string.Join(", ", props.Select(p => "@" + p.Name));

            var sql = $"INSERT INTO {type.Name} ({columnNames}) VALUES ({paramNames}); SELECT SCOPE_IDENTITY();";

            using (var conn = GetConnection())
            {
                return conn.ExecuteScalar<int>(sql, entity);
            }
        }

        public static int Update<T>(T entity, object key) where T : class
        {
            var type = typeof(T);
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Where(p => p.CanRead && p.Name != "Id" && p.Name != "ID")
                            .ToList();

            var setParts = string.Join(", ", props.Select(p => $"{p.Name}=@{p.Name}"));
            var sql = $"UPDATE {type.Name} SET {setParts} WHERE Id=@Id";

            using (var conn = GetConnection())
            {
                return conn.Execute(sql, entity);
            }
        }
    }
}
