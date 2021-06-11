using CorpinatorBot.ConfigModels;
using CorpinatorBot.VerificationModels;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using System;
using System.Threading.Tasks;

namespace CorpinatorBot.Services
{
    public class AzureVerificationService : IVerificationService
    {
        private readonly BotSecretsConfig _secretsConfig;
        
        private readonly IPublicClientApplication _pca;
        private readonly IConfidentialClientApplication _app;
        private AuthenticationResult _authResult = default;

        public string UserId { get; private set; }
        public string Alias { get; private set; }
        public string Organization { get; private set; }
        public string Department { get; private set; }
        public UserType UserType { get; private set; }

        public AzureVerificationService(BotSecretsConfig secretsConfig)
        {
            _secretsConfig = secretsConfig;
        
            _pca = PublicClientApplicationBuilder
                .Create(_secretsConfig.DeviceAuthAppId)
                .WithAuthority($"https://login.microsoftonline.com/{_secretsConfig.AadTenant}")
                .WithDefaultRedirectUri()
                .Build();

            _app = ConfidentialClientApplicationBuilder
                .Create(_secretsConfig.AkvClientId)
                .WithClientSecret(_secretsConfig.AkvSecret)
                .WithAuthority($"https://login.microsoftonline.com/{_secretsConfig.AadTenant}")
                .Build();
        }

        public async Task LoadUserDetails(string shouldReportTo)
        {
            if (_authResult == default)
            {
                throw new InvalidOperationException($"Auth result is unavailable. Make sure to call {nameof(VerifyCode)} before this method");
            }

            var graph = new GraphServiceClient("https://graph.microsoft.com/beta", new GraphAuthenticationProvider(_authResult));

            UserId = _authResult.UniqueId;

            var user = await graph.Me.Request().GetAsync();

            Department = user.Department;
            Alias = user.MailNickname;

            if (Alias.StartsWith("t-"))
            {
                UserType = UserType.Intern;
            }
            else if (Alias.Substring(1, 1) == "-")
            {
                UserType = UserType.Contractor;
            }
            else
            {
                UserType = UserType.FullTimeEmployee;
            }

            if (string.IsNullOrWhiteSpace(shouldReportTo))
            {
                return;
            }
            Organization = await GetOrg(UserId, shouldReportTo, graph);
        }

        public async Task VerifyCode(Func<string, Task> deviceCodeCallback)
        {
            try
            {
                _authResult = await _pca.AcquireTokenWithDeviceCode(new[] { "User.Read.All" }, result =>
                {
                    return deviceCodeCallback(result.Message);
                }).ExecuteAsync();
            }
            catch (MsalClientException ex)
            {
                throw new VerificationException(ex.Message, ex, ex.ErrorCode);
            }
        }

        public async Task<bool> VerifyUser(Verification verification, GuildConfiguration guild)
        {
            var botAuthResult = await _app.AcquireTokenForClient(new[] { "https://graph.microsoft.com/.default" }).ExecuteAsync();

            var graph = new GraphServiceClient("https://graph.microsoft.com/beta", new GraphAuthenticationProvider(botAuthResult));

            try
            {
                var user = await graph.Users[verification.CorpUserId.ToString()].Request().GetAsync();
                if (!user.AccountEnabled ?? false)
                {
                    return false;
                }

                if(user.MailNickname.Contains("#EXT#"))
                {
                    return false;
                }

                if (guild.RequiresOrganization)
                {
                    var org = GetOrg(verification.CorpUserId.ToString(), guild.Organization, graph);

                    if (org == null)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (ServiceException ex) when (ex.Error.Code == "Request_ResourceNotFound")
            {
                return false;
            }
        }

        private async Task<string> GetOrg(string userId, string shouldReportTo, GraphServiceClient graph)
        {
            var currentManager = userId;
            while (true)
            {
                DirectoryObject manager;
                try
                {
                    manager = await graph.Users[currentManager].Manager.Request().GetAsync();
                    currentManager = manager.Id;
                    var tlmUser = await graph.Users[currentManager].Request().GetAsync();

                    if (tlmUser.MailNickname.Equals(shouldReportTo, StringComparison.OrdinalIgnoreCase))
                    {
                        Organization = shouldReportTo;
                        return shouldReportTo;
                    }
                }
                catch (ServiceException ex) when (ex.Error.Code == "Request_ResourceNotFound" && ex.Error.Message.Contains("manager"))
                {
                    break;
                }
            }

            return currentManager == userId ? null : currentManager;
        }
    }
}
