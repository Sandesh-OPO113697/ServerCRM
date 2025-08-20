using ServerCRM.Models;
using ServerCRM.Models.LogIn;
using System.DirectoryServices.AccountManagement;
using System.Net.NetworkInformation;

namespace ServerCRM.Services
{
    public class AuthService
    {
        public async Task<bool> CheckCredentialsAsync(CheckCredentials request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            try
            {
                var properties = IPGlobalProperties.GetIPGlobalProperties();
                var domainName = properties.DomainName;

                using var ctx = new PrincipalContext(ContextType.Domain, domainName);
                var userName = GetUsername(request.empCode).Trim();

                var user = await Task.Run(() => UserPrincipal.FindByIdentity(ctx, userName))
                                     .ConfigureAwait(false);

                if (user == null)
                    return false;

                var isValid = await Task.Run(() => ctx.ValidateCredentials(userName, request.password))
                                        .ConfigureAwait(false);

                return isValid;
            }
            catch
            {
               
                return false;
            }
        }
        public static string GetUsername(string usernameDomain)
        {
            if (string.IsNullOrWhiteSpace(usernameDomain))
                throw new ArgumentException("Username cannot be null or empty.", nameof(usernameDomain));

            if (usernameDomain.Contains("\\"))
            {
                var index = usernameDomain.IndexOf("\\", StringComparison.Ordinal);
                return usernameDomain[(index + 1)..];
            }

            if (usernameDomain.Contains("@"))
            {
                var index = usernameDomain.IndexOf("@", StringComparison.Ordinal);
                return usernameDomain[..index];
            }

            return usernameDomain;
        }
    }
}
