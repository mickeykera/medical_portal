using System.Net;
using Npgsql;

namespace MedicalPortal.Infrastructure.Data;

public static class PostgresConnectionHelper
{
    public static bool IsPostgres(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        var trimmed = connectionString.Trim();
        return trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Contains("Port=", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("Database=", StringComparison.OrdinalIgnoreCase);
    }

    public static string ToNpgsqlConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        var trimmed = connectionString.Trim();

        // Handle URI formatted connection strings (e.g. postgres://user:pass@host:5432/dbname?sslmode=require)
        if (trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(trimmed);

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port > 0 ? uri.Port : 5432,
                Database = uri.AbsolutePath.TrimStart('/')
            };

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var userParts = uri.UserInfo.Split(':', 2);
                if (userParts.Length > 0 && !string.IsNullOrEmpty(userParts[0]))
                {
                    builder.Username = Uri.UnescapeDataString(userParts[0]);
                }
                if (userParts.Length > 1 && !string.IsNullOrEmpty(userParts[1]))
                {
                    builder.Password = Uri.UnescapeDataString(userParts[1]);
                }
            }

            // Parse Query Parameters
            if (!string.IsNullOrEmpty(uri.Query))
            {
                var query = uri.Query.TrimStart('?');
                var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);

                foreach (var pair in pairs)
                {
                    var kv = pair.Split('=', 2);
                    var key = Uri.UnescapeDataString(kv[0]).Trim();
                    var val = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]).Trim() : "";

                    if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Enum.TryParse<SslMode>(val, true, out var sslMode))
                        {
                            builder.SslMode = sslMode;
                        }
                    }
                    else if (key.Equals("pooling", StringComparison.OrdinalIgnoreCase) && bool.TryParse(val, out var pooling))
                    {
                        builder.Pooling = pooling;
                    }
                    else if (key.Equals("timeout", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out var timeout))
                    {
                        builder.Timeout = timeout;
                    }
                    else if (key.Equals("channel_binding", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Enum.TryParse<ChannelBinding>(val, true, out var cb))
                        {
                            builder.ChannelBinding = cb;
                        }
                    }
                    else if (key.Equals("searchpath", StringComparison.OrdinalIgnoreCase) || key.Equals("search_path", StringComparison.OrdinalIgnoreCase))
                    {
                        builder.SearchPath = val;
                    }
                    else if (key.Equals("application_name", StringComparison.OrdinalIgnoreCase))
                    {
                        builder.ApplicationName = val;
                    }
                }
            }

            return builder.ConnectionString;
        }

        // Standard ADO.NET connection string format - validate with NpgsqlConnectionStringBuilder
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(trimmed);
            return builder.ConnectionString;
        }
        catch
        {
            // If it already looks like a valid connection string, return as is
            return trimmed;
        }
    }
}
