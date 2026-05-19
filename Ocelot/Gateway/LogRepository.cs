using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;


namespace OcelotAPI.Gateway;

public sealed record RequestLogEntry(
    string Path,
    string Method,
    string RequestBody,
    string ResponseBody,
    int StatusCode,
    int ElapsedTimeMs,
    DateTime StartTime,
    DateTime EndTime);

public sealed class LogRepository
{
    private readonly string _cs;
    public LogRepository(IConfiguration cfg) =>
        _cs = cfg.GetConnectionString("connectionString")!;

    public async Task LogRequestAsync(RequestLogEntry entry)
    {
        var p = new DynamicParameters();
        p.Add("Path", entry.Path);
        p.Add("Method", entry.Method);
        p.Add("RequestBody", entry.RequestBody);
        p.Add("ResponseBody", entry.ResponseBody);
        p.Add("StatusCode", entry.StatusCode);
        p.Add("ElapsedTimeMs", entry.ElapsedTimeMs);
        p.Add("StartTime", entry.StartTime.ToUniversalTime());
        p.Add("EndTime", entry.EndTime.ToUniversalTime());

        await using var db = new SqlConnection(_cs);
        await db.ExecuteAsync(
            sql: "sp_LogApiRequest",
            param: p,
            commandType: CommandType.StoredProcedure);
    }
}
