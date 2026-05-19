using MozartWorkflows.Notifications.Interfaces;
using System.Collections.Concurrent;

namespace MozartWorkflows.Notifications.Services
{
    public class InMemoryUserTracker : IOnlineUserTracker
    {
        private readonly ConcurrentDictionary<string, bool> _onlineUsers = new();

        public void MarkOnline(string userId)
        {
            _onlineUsers[userId] = true;
        }

        public void MarkOffline(string userId)
        {
            _onlineUsers.TryRemove(userId, out _);
        }

        public bool IsOnline(string userId)
        {
            return _onlineUsers.ContainsKey(userId);
        }
    }

}
