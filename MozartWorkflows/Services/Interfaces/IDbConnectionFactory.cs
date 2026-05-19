using System.Data;

namespace MozartWorkflows.Services.Interfaces
{
    public interface IDbConnectionFactory
    {
        IDbConnection CreateConnection();
    }
}
