using CorpinatorBot.ConfigModels;
using CorpinatorBot.Extensions;
using CorpinatorBot.Services;
using CorpinatorBot.VerificationModels;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CorpinatorBot.Discord
{
    public class OngoingValidator : IHostedService
    {
        private readonly ILogger<OngoingValidator> _logger;
        private readonly IVerificationService _verificationService;
        private readonly IGuildConfigService _guildConfig;
        private readonly IDiscordClient _discord;
        private readonly IVerificationStorageService _verificationStorage;
        private readonly Timer _verificationTimer;

        public OngoingValidator(ILogger<OngoingValidator> logger, IVerificationService verificationService, IDiscordClient discord, 
            IVerificationStorageService verificationStorage, IGuildConfigService guildConfig)
        {
            _logger = logger;
            _verificationService = verificationService;
            _guildConfig = guildConfig;
            _discord = discord;
            _verificationStorage = verificationStorage;
            _verificationTimer = new Timer(async s => await CleanupUsers(s), null, Timeout.Infinite, Timeout.Infinite);
        }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _verificationTimer.Change(TimeSpan.FromMinutes(1), TimeSpan.FromDays(1));
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _verificationTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _verificationTimer.Dispose();

            return Task.CompletedTask;
        }

        private async Task CleanupUsers(object state)
        {
            _logger.LogInformation("Begin clean up of users");
            try
            {
                var guilds = await _guildConfig.GetAllConfigurations();

                foreach (var config in guilds)
                {
                    var guildUsers = await _verificationStorage.GetAllVerificationsInGuild(ulong.Parse(config.RowKey));

                    //todo: handle if the user is no longer in the guild
                    //todo: remove the role from the user
                    //todo: go through roles and remove users from verifications (with respect to manually added);
                    //todo: go through the list of people in the backend DB and anyone who isn't in the server anymore, 
                    //      clear them out of the DB (alt: hook the member left event and remove their verification)

                    foreach (var guildUser in guildUsers)
                    {
                        var exists = await _verificationService.VerifyUser(guildUser, config);

                        if (!exists)
                        {
                            _logger.LogWarning($"{guildUser.Alias} is either no longer with the company, or no longer reports to {config.Organization}, about to remove verification role and storage.");
                            var successful = await RemoveFromRole(guildUser.DiscordId, guildUser.GuildId, ulong.Parse(config.RoleId));
                            if(successful) 
                            {
                                _logger.LogInformation($"Removing verification tracking of {guildUser.Alias}");
                                // what-if mode for now
                                //await verificationsTable.ExecuteAsync(TableOperation.Delete(guildUser));
                                
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // to avoid crashing the whole thing because async void
                _logger.LogError(ex, $"Error while running the {nameof(CleanupUsers)} background job");
            }
            _logger.LogInformation("Done cleaning up users");
        }

        private async Task<bool> RemoveFromRole(ulong userId, ulong guildId, ulong roleId)
        {
            try
            {
                var guild = (await _discord.GetGuildAsync(guildId)) as SocketGuild;
                var user = (await _discord.GetUserAsync(userId)) as SocketGuildUser;

                if (guild == null || user == null)
                {
                    _logger.LogInformation($"User {userId} or guild {guildId} does not exist.");
                    return true;
                }

                var role = guild.Roles.SingleOrDefault(a => a.Id == roleId);

                if (role == null)
                {
                    _logger.LogInformation($"Role {roleId} does not exist in guild {guildId}");
                    return true;
                }

                await user.RemoveRoleAsync(role);
                _logger.LogInformation($"User {userId} was removed from role {roleId} in guild {guildId}");
                return true;
            }
            catch(Exception ex)
            {
                _logger.LogCritical(ex, $"Unable to remove user {userId} form role {roleId} in guild {guildId}");
                return false;
            }
        }
    }
}
