using Elsa.Activities.Http.Contracts;
using Elsa.Activities.Http.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace MozartWorkflows.Extensions
{
    public class JwtForApiAuthorizationHandler : IHttpEndpointAuthorizationHandler
    {
        private readonly IAuthenticationService _authenticationService;

        public JwtForApiAuthorizationHandler(IAuthenticationService authenticationService)
        {
            _authenticationService = authenticationService;
        }

        public async ValueTask<bool> AuthorizeAsync(AuthorizeHttpEndpointContext context)
        {
            var httpContext = context.HttpContext;
            var path = httpContext.Request.Path;

            // Only check authentication for API endpoints
            if (path.StartsWithSegments("/api"))
            {
                var authResult = await _authenticationService.AuthenticateAsync(httpContext, "Bearer");

                if (authResult.Succeeded)
                {
                    httpContext.User = authResult.Principal;
                    return true;
                }

                httpContext.Response.StatusCode = 401;
                return false;
            }

            // For all other endpoints, allow access
            // Cookie authentication middleware will handle UI authentication
            return true;
        }
    }
}