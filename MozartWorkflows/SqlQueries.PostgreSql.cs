namespace MozartWorkflows
{
    public static partial class SqlQueries
    {
        /// <summary>PostgreSQL query strings (Npgsql — uses @ named parameter markers).</summary>
        public static class PostgreSql
        {
            // ── Auth ─────────────────────────────────────────────────────────

            /// <summary>Authenticate a dashboard user by username or e-mail.</summary>
            public const string LoginUser =
                "SELECT id, username, email, passwordhash, salt, COALESCE(IsAdmin, false) AS IsAdmin " +
                "FROM   elsadashboardusers " +
                "WHERE  active = TRUE " +
                "  AND  (username = @Input OR email = @Input) " +
                "LIMIT  1";

            /// <summary>Record a login attempt in the audit log.</summary>
            public const string InsertLoginAudit =
                "INSERT INTO elsaloginaudit " +
                "       (userid, emailorusername, logintime, ipaddress, useragent, issuccessful, failurereason) " +
                "VALUES (@UserId, @Input, (NOW() AT TIME ZONE 'UTC'), @IP, @Agent, @Success, @Reason)";
        }
    }
}
