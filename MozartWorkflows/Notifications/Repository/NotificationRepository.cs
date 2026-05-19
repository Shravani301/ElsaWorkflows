using Dapper;
using MozartWorkflows;
using MozartWorkflows.Notifications.Interfaces;
using MozartWorkflows.Notifications.Models;
using MozartWorkflows.Services.Interfaces;

namespace MozartWorkflows.Notifications.Repository
{
    public class NotificationRepository : INotificationRepository
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public NotificationRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task SavePendingNotificationAsync(string userId, string message, string subject, string mode)
        {
            using var conn = _dbConnectionFactory.CreateConnection();
            var query = SqlQueries.SqlServer.InsertPendingNotification;

            var parameters = new
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Message = message,
                Subject = subject,
                Mode = mode
            };

            await conn.ExecuteAsync(query, parameters);
        }

        public async Task<IEnumerable<PendingNotification>> GetUnsentPushNotificationsAsync(string userId)
        {
            using var conn = _dbConnectionFactory.CreateConnection();
            var query = SqlQueries.SqlServer.GetPendingPushNotifications;

            return await conn.QueryAsync<PendingNotification>(query, new { UserId = userId });
        }

        public async Task MarkAsSentAsync(Guid notificationId)
        {
            using var conn = _dbConnectionFactory.CreateConnection();
            var query = SqlQueries.SqlServer.MarkNotificationSent;

            await conn.ExecuteAsync(query, new { Id = notificationId });
        }
    }

}
