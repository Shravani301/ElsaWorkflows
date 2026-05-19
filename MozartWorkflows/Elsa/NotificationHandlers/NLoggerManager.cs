using Elsa.Scripting.JavaScript.Messages;
using MediatR;
using NLog;
using ILogger = NLog.ILogger;

namespace MozartWorkflows.Elsa.NotificationHandlers
{
    public class NLoggerManager: INotificationHandler<EvaluatingJavaScriptExpression>
    {
        private readonly ILogger _logger;

        public NLoggerManager()
        {
            _logger =LogManager.GetCurrentClassLogger();
        }
        public Task Handle(EvaluatingJavaScriptExpression notification, CancellationToken cancellationToken)
        {
            var engine = notification.Engine;
            engine.SetValue("setLogMessage", (Action<string,string>)(SetLoggger));
            return Task.CompletedTask;
        }
        public void SetLoggger(string logMessage,string logLevel)
        {
            switch (logLevel)
            {
                case "Debug":
                    _logger.Debug(logMessage);
                    break;
                case "Info":
                    _logger.Info(logMessage);
                    break;
                case "Warning":
                    _logger.Warn(logMessage);
                    break;
                case "Error":
                    _logger.Error(logMessage);
                    break;
                default:
                    _logger.Error("No log Message");
                    break;
            }
        }

    }
}
