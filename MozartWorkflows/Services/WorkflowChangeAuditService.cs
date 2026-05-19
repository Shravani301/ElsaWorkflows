using Dapper;
using Microsoft.Extensions.Configuration;
using MozartWorkflows.Models;
using MozartWorkflows.Services.Interfaces;

namespace MozartWorkflows.Services;

public class WorkflowChangeAuditService : IWorkflowChangeAuditService
{
    private const string SqlServerProvider = "SqlServer";
    private const string PostgreSqlProvider = "PostgreSql";
    private const string MySqlProvider = "MySql";
    private const string OracleProvider = "Oracle";
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly string _provider;

    public WorkflowChangeAuditService(IDbConnectionFactory connectionFactory, IConfiguration config)
    {
        _connectionFactory = connectionFactory;
        _provider = config["DatabaseProvider"] ?? SqlServerProvider;
    }

    // ─────────────────────────────────────────────────────────── DDL ──────

    public async Task EnsureTableExistsAsync()
    {
        // Each provider block: CREATE TABLE if missing, then ADD COLUMN if missing.
        var ddl = _provider switch
        {
            SqlServerProvider => @"
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'WorkflowChangeAudit')
                BEGIN
                    CREATE TABLE WorkflowChangeAudit (
                        Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
                        DefinitionId    NVARCHAR(100)  NOT NULL,
                        WorkflowName    NVARCHAR(500)  NOT NULL,
                        Version         INT            NOT NULL DEFAULT 0,
                        ChangeType      NVARCHAR(50)   NOT NULL,
                        ChangedBy       NVARCHAR(256)  NOT NULL,
                        ChangedByUserId NVARCHAR(100)  NULL,
                        ChangedAt       DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
                        ActivityCount   INT            NULL,
                        ChangeDetails   NVARCHAR(MAX)  NULL,
                        IpAddress       NVARCHAR(50)   NULL
                    );
                    CREATE INDEX IX_WCA_DefinitionId ON WorkflowChangeAudit (DefinitionId);
                    CREATE INDEX IX_WCA_ChangedAt    ON WorkflowChangeAudit (ChangedAt DESC);
                END
                ELSE
                BEGIN
                    -- Migrate: add ChangedByUserId if the table already exists without it
                    IF NOT EXISTS (
                        SELECT 1 FROM sys.columns
                        WHERE object_id = OBJECT_ID('WorkflowChangeAudit')
                          AND name = 'ChangedByUserId')
                        ALTER TABLE WorkflowChangeAudit ADD ChangedByUserId NVARCHAR(100) NULL;
                END",

            PostgreSqlProvider => @"
                CREATE TABLE IF NOT EXISTS ""WorkflowChangeAudit"" (
                    ""Id""              BIGSERIAL PRIMARY KEY,
                    ""DefinitionId""    VARCHAR(100)  NOT NULL,
                    ""WorkflowName""    VARCHAR(500)  NOT NULL,
                    ""Version""         INT           NOT NULL DEFAULT 0,
                    ""ChangeType""      VARCHAR(50)   NOT NULL,
                    ""ChangedBy""       VARCHAR(256)  NOT NULL,
                    ""ChangedByUserId"" VARCHAR(100)  NULL,
                    ""ChangedAt""       TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
                    ""ActivityCount""   INT           NULL,
                    ""ChangeDetails""   TEXT          NULL,
                    ""IpAddress""       VARCHAR(50)   NULL
                );
                DO $$ BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name='WorkflowChangeAudit' AND column_name='ChangedByUserId')
                    THEN ALTER TABLE ""WorkflowChangeAudit"" ADD COLUMN ""ChangedByUserId"" VARCHAR(100) NULL;
                    END IF;
                END $$;
                CREATE INDEX IF NOT EXISTS ""IX_WCA_DefinitionId""
                    ON ""WorkflowChangeAudit"" (""DefinitionId"");
                CREATE INDEX IF NOT EXISTS ""IX_WCA_ChangedAt""
                    ON ""WorkflowChangeAudit"" (""ChangedAt"" DESC);",

            MySqlProvider => @"
                CREATE TABLE IF NOT EXISTS WorkflowChangeAudit (
                    Id              BIGINT AUTO_INCREMENT PRIMARY KEY,
                    DefinitionId    VARCHAR(100)  NOT NULL,
                    WorkflowName    VARCHAR(500)  NOT NULL,
                    Version         INT           NOT NULL DEFAULT 0,
                    ChangeType      VARCHAR(50)   NOT NULL,
                    ChangedBy       VARCHAR(256)  NOT NULL,
                    ChangedByUserId VARCHAR(100)  NULL,
                    ChangedAt       DATETIME      NOT NULL DEFAULT UTC_TIMESTAMP(),
                    ActivityCount   INT           NULL,
                    ChangeDetails   LONGTEXT      NULL,
                    IpAddress       VARCHAR(50)   NULL,
                    INDEX IX_WCA_DefinitionId (DefinitionId),
                    INDEX IX_WCA_ChangedAt (ChangedAt)
                );
                -- Migrate existing table
                SET @exists = (SELECT COUNT(*) FROM information_schema.columns
                    WHERE table_schema = DATABASE() AND table_name = 'WorkflowChangeAudit'
                    AND column_name = 'ChangedByUserId');
                SET @sql = IF(@exists = 0,
                    'ALTER TABLE WorkflowChangeAudit ADD COLUMN ChangedByUserId VARCHAR(100) NULL',
                    'SELECT 1');
                PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;",

            OracleProvider => @"
                DECLARE v_count NUMBER;
                BEGIN
                    SELECT COUNT(*) INTO v_count FROM user_tables
                        WHERE table_name = 'WORKFLOWCHANGEAUDIT';
                    IF v_count = 0 THEN
                        EXECUTE IMMEDIATE '
                            CREATE TABLE WORKFLOWCHANGEAUDIT (
                                Id              NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                                DefinitionId    NVARCHAR2(100)  NOT NULL,
                                WorkflowName    NVARCHAR2(500)  NOT NULL,
                                Version         NUMBER(10)      DEFAULT 0 NOT NULL,
                                ChangeType      NVARCHAR2(50)   NOT NULL,
                                ChangedBy       NVARCHAR2(256)  NOT NULL,
                                ChangedByUserId NVARCHAR2(100)  NULL,
                                ""ChangedAt""       TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
                                ActivityCount   NUMBER(10)      NULL,
                                ChangeDetails   NCLOB           NULL,
                                IpAddress       NVARCHAR2(50)   NULL
                            )';
                        EXECUTE IMMEDIATE '
                            CREATE INDEX IX_WCA_DefinitionId ON WORKFLOWCHANGEAUDIT(DefinitionId)';
                        EXECUTE IMMEDIATE '
                            CREATE INDEX IX_WCA_ChangedAt ON WORKFLOWCHANGEAUDIT(ChangedAt DESC)';
                    ELSE
                        SELECT COUNT(*) INTO v_count FROM user_tab_columns
                            WHERE table_name = ''WORKFLOWCHANGEAUDIT'' AND column_name = ''CHANGEDBYUSERID'';
                        IF v_count = 0 THEN
                            EXECUTE IMMEDIATE ''ALTER TABLE WORKFLOWCHANGEAUDIT ADD ChangedByUserId NVARCHAR2(100) NULL'';
                        END IF;
                    END IF;
                END;",

            _ => throw new NotSupportedException($"Provider '{_provider}' not supported.")
        };

        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(ddl);
    }

    // ─────────────────────────────────────────────────────────── INSERT ───

    public async Task LogAsync(WorkflowChangeAudit audit)
    {
        audit.ChangedAt = DateTime.UtcNow;

        var sql = _provider switch
        {
            PostgreSqlProvider => @"
                INSERT INTO ""WorkflowChangeAudit""
                    (""DefinitionId"",""WorkflowName"",""Version"",""ChangeType"",
                     ""ChangedBy"",""ChangedByUserId"",""ChangedAt"",
                     ""ActivityCount"",""ChangeDetails"",""IpAddress"")
                VALUES
                    (@DefinitionId,@WorkflowName,@Version,@ChangeType,
                     @ChangedBy,@ChangedByUserId,@ChangedAt,
                     @ActivityCount,@ChangeDetails,@IpAddress)",

            _ => @"
                INSERT INTO WorkflowChangeAudit
                    (DefinitionId, WorkflowName, Version, ChangeType,
                     ChangedBy, ChangedByUserId, ChangedAt,
                     ActivityCount, ChangeDetails, IpAddress)
                VALUES
                    (@DefinitionId, @WorkflowName, @Version, @ChangeType,
                     @ChangedBy, @ChangedByUserId, @ChangedAt,
                     @ActivityCount, @ChangeDetails, @IpAddress)"
        };

        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, audit);
    }

    // ─── Remove duplicate Saved rows (emitted by Elsa during publish) ─────

    public async Task RemoveRecentSavedAsync(string definitionId, int version)
    {
        // Elsa emits WorkflowDefinitionSaved as a side-effect of every publish /
        // retract. Remove any 'Saved' record written in the last 30 s for this
        // definition+version so only the Published / Unpublished row is kept.
        var sql = _provider switch
        {
            SqlServerProvider => @"
                DELETE FROM WorkflowChangeAudit
                WHERE DefinitionId = @DefinitionId
                  AND Version      = @Version
                  AND ChangeType   = 'Saved'
                  AND ChangedAt   >= DATEADD(SECOND, -30, GETUTCDATE())",

            PostgreSqlProvider => @"
                DELETE FROM ""WorkflowChangeAudit""
                WHERE ""DefinitionId"" = @DefinitionId
                  AND ""Version""      = @Version
                  AND ""ChangeType""   = 'Saved'
                  AND ""ChangedAt""   >= NOW() - INTERVAL '30 seconds'",

            MySqlProvider => @"
                DELETE FROM WorkflowChangeAudit
                WHERE DefinitionId = @DefinitionId
                  AND Version      = @Version
                  AND ChangeType   = 'Saved'
                  AND ChangedAt   >= DATE_SUB(UTC_TIMESTAMP(), INTERVAL 30 SECOND)",

            OracleProvider => @"
                DELETE FROM WORKFLOWCHANGEAUDIT
                WHERE DefinitionId = :DefinitionId
                  AND Version      = :Version
                  AND ChangeType   = 'Saved'
                  AND ChangedAt   >= SYS_EXTRACT_UTC(SYSTIMESTAMP) - INTERVAL '30' SECOND",

            _ => throw new NotSupportedException($"Provider '{_provider}' not supported.")
        };

        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new { DefinitionId = definitionId, Version = version });
    }

    // ─────────────────────────────────────────────────────────── SELECT ───

    public async Task<IEnumerable<WorkflowChangeAudit>> GetRecentAsync(int top = 200)
    {
        var sql = _provider switch
        {
            SqlServerProvider => $"SELECT TOP {top} * FROM WorkflowChangeAudit ORDER BY ChangedAt DESC",
            PostgreSqlProvider => $@"SELECT * FROM ""WorkflowChangeAudit"" ORDER BY ""ChangedAt"" DESC LIMIT {top}",
            MySqlProvider => $"SELECT * FROM WorkflowChangeAudit ORDER BY ChangedAt DESC LIMIT {top}",
            OracleProvider => $"SELECT * FROM WorkflowChangeAudit ORDER BY ChangedAt DESC FETCH FIRST {top} ROWS ONLY",
            _ => throw new NotSupportedException($"Provider '{_provider}' not supported.")
        };

        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<WorkflowChangeAudit>(sql);
    }

    public async Task<IEnumerable<WorkflowChangeAudit>> GetByDefinitionAsync(string definitionId)
    {
        var sql = _provider switch
        {
            PostgreSqlProvider => @"SELECT * FROM ""WorkflowChangeAudit""
                              WHERE ""DefinitionId"" = @DefinitionId ORDER BY ""ChangedAt"" DESC",
            _ => "SELECT * FROM WorkflowChangeAudit WHERE DefinitionId = @DefinitionId ORDER BY ChangedAt DESC"
        };

        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<WorkflowChangeAudit>(sql, new { DefinitionId = definitionId });
    }

    // ─────────────────────────────────────────────────────── PAGED SELECT ──

    public async Task<PagedResult<WorkflowChangeAudit>> GetPagedAsync(
        int page, int pageSize,
        string? workflowFilter = null, string? changeTypeFilter = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Max(1, pageSize);
        int offset = (page - 1) * pageSize;

        // Build dynamic WHERE clause (provider-agnostic column names for SQL Server / MySQL)
        var whereParts = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", pageSize);

        if (!string.IsNullOrWhiteSpace(workflowFilter))
        {
            whereParts.Add(_provider == PostgreSqlProvider
                ? @"""WorkflowName"" ILIKE @WorkflowFilter"
                : "WorkflowName LIKE @WorkflowFilter");
            parameters.Add("WorkflowFilter", $"%{workflowFilter}%");
        }

        if (!string.IsNullOrWhiteSpace(changeTypeFilter))
        {
            whereParts.Add(_provider == PostgreSqlProvider
                ? @"""ChangeType"" = @ChangeTypeFilter"
                : "ChangeType = @ChangeTypeFilter");
            parameters.Add("ChangeTypeFilter", changeTypeFilter);
        }

        var whereClause = whereParts.Count > 0
            ? "WHERE " + string.Join(" AND ", whereParts)
            : string.Empty;

        string countSql;
        string dataSql;

        switch (_provider)
        {
            case PostgreSqlProvider:
                countSql = $@"SELECT COUNT(*) FROM ""WorkflowChangeAudit"" {whereClause}";
                dataSql = $@"SELECT * FROM ""WorkflowChangeAudit"" {whereClause}
                              ORDER BY ""ChangedAt"" DESC
                              LIMIT @PageSize OFFSET @Offset";
                break;

            case MySqlProvider:
                countSql = $"SELECT COUNT(*) FROM WorkflowChangeAudit {whereClause}";
                dataSql = $"SELECT * FROM WorkflowChangeAudit {whereClause} ORDER BY ChangedAt DESC LIMIT @PageSize OFFSET @Offset";
                break;

            case OracleProvider:
                countSql = $"SELECT COUNT(*) FROM WORKFLOWCHANGEAUDIT {whereClause}";
                dataSql = $@"SELECT * FROM WORKFLOWCHANGEAUDIT {whereClause}
                              ORDER BY ChangedAt DESC
                              OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
                break;

            default: // SqlServer
                countSql = $"SELECT COUNT(*) FROM WorkflowChangeAudit {whereClause}";
                dataSql = $@"SELECT * FROM WorkflowChangeAudit {whereClause}
                              ORDER BY ChangedAt DESC
                              OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
                break;
        }

        using var conn = _connectionFactory.CreateConnection();
        int totalCount = await conn.ExecuteScalarAsync<int>(countSql, parameters);
        var items = await conn.QueryAsync<WorkflowChangeAudit>(dataSql, parameters);

        return new PagedResult<WorkflowChangeAudit>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    // ─────────────────────────────────────────────────────────── DELETE ───

    public async Task DeleteAsync(IEnumerable<long> ids)
    {
        var idList = ids?.ToList();
        if (idList == null || idList.Count == 0) return;

        var sql = _provider switch
        {
            PostgreSqlProvider => @"DELETE FROM ""WorkflowChangeAudit"" WHERE ""Id"" = ANY(@Ids)",
            _ => "DELETE FROM WorkflowChangeAudit WHERE Id IN @Ids"
        };

        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new { Ids = idList });
    }
}
