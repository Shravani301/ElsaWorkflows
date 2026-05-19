using MozartWorkflows.Notifications.Interfaces;

namespace MozartWorkflows.Notifications.Services
{
    public class PushNotificationService : IPushNotificationService
    {
        private readonly ISignalRService _signalRService;
        private readonly INotificationRepository _repo;

        public PushNotificationService(ISignalRService signalRService, INotificationRepository repo)
        {
            _signalRService = signalRService;
            _repo = repo;
        }

        public async Task SendAsync(string userId, string message)
        {
            if (_signalRService.IsUserOnline(userId))
                await _signalRService.SendToUserAsync(userId, message);
            else
                await StoreForLaterAsync(userId, message);
        }

        public async Task StoreForLaterAsync(string userId, string message)
        {
            await _repo.SavePendingNotificationAsync(userId, message, string.Empty, "Push");
        }
    }

}
