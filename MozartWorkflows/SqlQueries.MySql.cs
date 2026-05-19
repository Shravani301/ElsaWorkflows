namespace MozartWorkflows
{
    public static partial class SqlQueries
    {
        /// <summary>MySQL query strings (uses ? positional / named parameter markers).</summary>
        public static class MySql
        {
            // ── Auth ─────────────────────────────────────────────────────────

            /// <summary>Authenticate a dashboard user by username or e-mail.</summary>
            public const string LoginUser =
                "SELECT id, username, email, passwordhash, salt, IFNULL(IsAdmin, 0) AS IsAdmin " +
                "FROM   elsadashboardusers " +
                "WHERE  active = 1 " +
                "  AND  (username = ?Input OR email = ?Input) " +
                "LIMIT  1";

            /// <summary>Record a login attempt in the audit log.</summary>
            public const string InsertLoginAudit =
                "INSERT INTO elsaloginaudit " +
                "       (userid, emailorusername, logintime, ipaddress, useragent, issuccessful, failurereason) " +
                "VALUES (?UserId, ?Input, UTC_TIMESTAMP(), ?IP, ?Agent, ?Success, ?Reason)";
        }
    }
}
