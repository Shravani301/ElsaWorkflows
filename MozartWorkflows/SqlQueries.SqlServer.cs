namespace MozartWorkflows
{
    public static partial class SqlQueries
    {
        /// <summary>SQL Server (T-SQL) query strings.</summary>
        public static class SqlServer
        {
            private const string TableDashboardUsers = "ElsaDashboardUsers";
            private const string WhereId = "WHERE  Id = @Id";
            private const string UpdateDashboardUsers = "UPDATE " + TableDashboardUsers + " ";
            private const string FromDashboardUsers = "FROM   " + TableDashboardUsers + " ";

            // ── Auth ─────────────────────────────────────────────────────────

            /// <summary>Authenticate a dashboard user by username or e-mail.</summary>
            public const string LoginUser =
                "SELECT TOP 1 id, username, email, passwordhash, salt, ISNULL(IsAdmin, 0) AS IsAdmin " +
                "FROM   elsadashboardusers " +
                "WHERE  active = 1 " +
                "  AND  (username = @Input OR email = @Input)";

            /// <summary>Record a login attempt in the audit log.</summary>
            public const string InsertLoginAudit =
                "INSERT INTO elsaloginaudit " +
                "       (userid, emailorusername, logintime, ipaddress, useragent, issuccessful, failurereason) " +
                "VALUES (@UserId, @Input, GETUTCDATE(), @IP, @Agent, @Success, @Reason)";

            /// <summary>Mark the most-recent successful login row as logged-out.</summary>
            public const string UpdateLogoutAudit =
                "UPDATE ElsaLoginAudit " +
                "SET    IsLogout   = 1, " +
                "       LogoutTime = GETUTCDATE() " +
                "WHERE  Id = ( " +
                "    SELECT TOP 1 Id " +
                "    FROM   ElsaLoginAudit " +
                "    WHERE  UserId       = @UserId " +
                "      AND  IsSuccessful = 1 " +
                "      AND  (IsLogout IS NULL OR IsLogout = 0) " +
                "    ORDER BY LoginTime DESC " +
                ")";

            // ── User management ──────────────────────────────────────────────

            /// <summary>Create the Elsa dashboard auth tables if a fresh database does not have them yet.</summary>
            public const string EnsureDashboardAuthTables =
                @"
IF OBJECT_ID(N'dbo.ElsaDashboardUsers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ElsaDashboardUsers (
        Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ElsaDashboardUsers PRIMARY KEY,
        UserId       NVARCHAR(100)     NOT NULL,
        Username     NVARCHAR(100)     NOT NULL,
        Email        NVARCHAR(200)     NOT NULL,
        PasswordHash NTEXT             NOT NULL,
        Salt         NVARCHAR(100)     NULL,
        Active       BIT               NOT NULL CONSTRAINT DF_ElsaDashboardUsers_Active DEFAULT ((1)),
        CreatedAt    DATETIME          NOT NULL CONSTRAINT DF_ElsaDashboardUsers_CreatedAt DEFAULT (GETUTCDATE()),
        UpdatedAt    DATETIME          NULL,
        IsAdmin      BIT               NOT NULL CONSTRAINT DF_ElsaDashboardUsers_IsAdmin DEFAULT ((0))
    );
END;

IF OBJECT_ID(N'dbo.ElsaDashboardUsers', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.ElsaDashboardUsers', N'IsAdmin') IS NULL
BEGIN
    ALTER TABLE dbo.ElsaDashboardUsers
        ADD IsAdmin BIT NOT NULL CONSTRAINT DF_ElsaDashboardUsers_IsAdmin DEFAULT ((0));
END;

IF OBJECT_ID(N'dbo.ElsaLoginAudit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ElsaLoginAudit (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ElsaLoginAudit PRIMARY KEY,
        UserId          INT               NULL,
        EmailOrUsername NVARCHAR(200)     NOT NULL,
        LoginTime       DATETIME          NOT NULL CONSTRAINT DF_ElsaLoginAudit_LoginTime DEFAULT (GETUTCDATE()),
        IPAddress       NVARCHAR(100)     NULL,
        UserAgent       NVARCHAR(400)     NULL,
        IsSuccessful    BIT               NOT NULL,
        FailureReason   NVARCHAR(500)     NULL,
        IsLogout        BIT               NULL,
        LogoutTime      DATETIME          NULL
    );
END";

            /// <summary>Seed a default dashboard admin only when the dashboard user table is empty.</summary>
            public const string EnsureDefaultAdminUser =
                @"
IF NOT EXISTS (SELECT 1 FROM dbo.ElsaDashboardUsers WITH (UPDLOCK, HOLDLOCK))
BEGIN
    INSERT INTO dbo.ElsaDashboardUsers
           (UserId, Username, Email, PasswordHash, Salt, Active, CreatedAt, IsAdmin)
    VALUES (@UserId, @Username, @Email, @PasswordHash, @Salt, 1, GETUTCDATE(), 1);
END";

            /// <summary>Fetch a single dashboard user by primary key.</summary>
            public const string GetUserById =
                "SELECT TOP 1 " +
                "       Id, UserId, Username, Email, Active, " +
                "       ISNULL(IsAdmin, 0) AS IsAdmin, " +
                "       CreatedAt, UpdatedAt " +
                FromDashboardUsers +
                WhereId;

            /// <summary>Return all dashboard users, ordered by username.</summary>
            public const string GetAllUsers =
                "SELECT Id, UserId, Username, Email, Active, " +
                "       ISNULL(IsAdmin, 0) AS IsAdmin, " +
                "       CreatedAt, UpdatedAt " +
                FromDashboardUsers +
                "ORDER BY Username";

            /// <summary>
            /// Insert a new dashboard user, then immediately SELECT the inserted row
            /// (two statements — execute with QueryFirstOrDefaultAsync).
            /// </summary>
            public const string CreateUser =
                "INSERT INTO " + TableDashboardUsers + " " +
                "       (UserId, Username, Email, PasswordHash, Salt, Active, IsAdmin, CreatedAt) " +
                "VALUES (@UserId, @Username, @Email, @PasswordHash, @Salt, 1, @IsAdmin, GETUTCDATE()); " +
                "SELECT TOP 1 " +
                "       Id, UserId, Username, Email, Active, " +
                "       ISNULL(IsAdmin, 0) AS IsAdmin, " +
                "       CreatedAt, UpdatedAt " +
                FromDashboardUsers +
                "WHERE  UserId = @UserId";

            /// <summary>Retrieve the stored password hash and salt for a user.</summary>
            public const string GetUserPasswordHash =
                "SELECT TOP 1 PasswordHash, Salt " +
                FromDashboardUsers +
                WhereId;

            /// <summary>Update the password hash and salt for a user (by ID).</summary>
            public const string UpdateUserPassword =
                UpdateDashboardUsers +
                "SET    PasswordHash = @PasswordHash, " +
                "       Salt         = @Salt, " +
                "       UpdatedAt    = GETUTCDATE() " +
                WhereId;

            /// <summary>Reset a user's password to the default (by username).</summary>
            public const string UpdateUserPasswordByUsername =
                UpdateDashboardUsers +
                "SET    PasswordHash = @PasswordHash, " +
                "       Salt         = @Salt, " +
                "       UpdatedAt    = GETUTCDATE() " +
                "WHERE  Username = @Username " +
                "  AND  Active   = 1";

            /// <summary>Activate or deactivate a dashboard user.</summary>
            public const string SetUserActive =
                UpdateDashboardUsers +
                "SET    Active    = @Active, " +
                "       UpdatedAt = GETUTCDATE() " +
                WhereId;

            /// <summary>Grant or revoke admin rights for a dashboard user.</summary>
            public const string SetUserAdmin =
                UpdateDashboardUsers +
                "SET    IsAdmin   = @IsAdmin, " +
                "       UpdatedAt = GETUTCDATE() " +
                WhereId;

            /// <summary>Permanently delete a dashboard user by primary key.</summary>
            public const string DeleteUser =
                "DELETE FROM " + TableDashboardUsers + " WHERE Id = @Id";

            // ── Roles / authorisation ────────────────────────────────────────

            /// <summary>Look up a user role by its whitespace-stripped policy name.</summary>
            public const string GetUserRoleByPolicyName =
                "SELECT Name, ApplicationId " +
                "FROM   UserRole " +
                "WHERE  REPLACE(Name, ' ', '') = @PolicyName " +
                "  AND  Active = 1";

            /// <summary>Return all users with their role assignment.</summary>
            public const string GetUsers =
                "SELECT Id, UserName, Email, UserRoleId FROM [User]";

            // ── Configuration ────────────────────────────────────────────────

            /// <summary>Retrieve a single configuration value by name.</summary>
            public const string GetConfigurationItem =
                "SELECT ConfigurationValue FROM Configuration WHERE ConfigurationName = @Name";

            // ── Notifications ────────────────────────────────────────────────

            /// <summary>Queue a new pending notification.</summary>
            public const string InsertPendingNotification =
                "INSERT INTO PendingNotifications (Id, UserId, Message, Subject, Mode, IsSent, CreatedAt) " +
                "VALUES (@Id, @UserId, @Message, @Subject, @Mode, 0, GETUTCDATE())";

            /// <summary>Retrieve unsent push notifications for a user.</summary>
            public const string GetPendingPushNotifications =
                "SELECT * FROM PendingNotifications " +
                "WHERE  UserId = @UserId AND Mode = 'Push' AND IsSent = 0";

            /// <summary>Mark a notification as sent.</summary>
            public const string MarkNotificationSent =
                "UPDATE PendingNotifications SET IsSent = 1 WHERE Id = @Id";
        }
    }
}
