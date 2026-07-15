using System;
using opc_ae_relay.config;

namespace opc_ae_relay.db
{
    /// <summary>
    /// 数据库提供者工厂，根据配置创建对应的 IDbProvider 实例
    /// 支持动态扩展新的数据库类型
    /// </summary>
    public static class DbProviderFactory
    {
        /// <summary>
        /// 根据数据库配置创建对应的 Provider 实例
        /// </summary>
        /// <param name="config">数据库配置对象</param>
        /// <returns>IDbProvider 实例</returns>
        /// <exception cref="NotSupportedException">不支持的数据库类型</exception>
        public static IDbProvider Create(DatabaseConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var dbType = (config.Type ?? "sqlserver").ToLower();

            switch (dbType)
            {
                case "sqlserver":
                case "mssql":
                    return new SqlServerProvider(config.Name, config.ConnectionString);

                case "mysql":
                case "mariadb":
                    return new MySqlProvider(config.Name, config.ConnectionString);

                // 未来可扩展其他数据库类型
                // case "postgresql":
                //     return new PostgreSqlProvider(config.Name, config.ConnectionString);
                // case "oracle":
                //     return new OracleProvider(config.Name, config.ConnectionString);

                default:
                    throw new NotSupportedException($"不支持的数据库类型: {dbType}，当前支持：sqlserver, mysql");
            }
        }
    }
}