using MozartWorkflows.Dtos;

namespace MozartWorkflows.Services.Interfaces
{
    public interface IUserService
    {
        Task<IEnumerable<UserDto>> GetUsersAsync();
    }

}
