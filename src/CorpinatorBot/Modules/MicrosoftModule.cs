using CorpinatorBot.Discord;
using CorpinatorBot.Services;
using CorpinatorBot.VerificationModels;
using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CorpinatorBot.Modules
{
    [Group("microsoft"), Alias("teamxbox")]
    public class MicrosoftModule : ModuleBase<GuildConfigSocketCommandContext>
    {
        private readonly IVerificationStorageService _verificationStorage;
        private readonly IGuildConfigService _guildConfigService;
        private readonly ILogger<MicrosoftModule> _logger;
        private readonly IVerificationService _verificationService;

        public MicrosoftModule(ILogger<MicrosoftModule> logger, IVerificationService verificationService, IGuildConfigService guildConfigService, IVerificationStorageService verificationStorage)
        {
            _verificationStorage = verificationStorage;
            _guildConfigService = guildConfigService;
            _logger = logger;
            _verificationService = verificationService;
        }

        [Command("verify", RunMode = RunMode.Async)]
        public async Task Verify()
        {
            if (!(Context.User is SocketGuildUser guildUser))
            {
                return;
            }
            
            var discordId = guildUser.Id;
            var guildId = Context.Guild.Id;
            var verificationResult = await _verificationStorage.GetVerification(guildId, discordId);
            
            if (verificationResult != null)
            {
                await ReplyAsync("You are already verified in this server.");
                return;
            }

            var dmChannel = await Context.User.GetOrCreateDMChannelAsync();
            var verification = new Verification { GuildId = guildId, DiscordId = discordId };
            
            try
            {
                await _verificationService.VerifyCode(async message =>
                {
                    try
                    {
                        await dmChannel.SendMessageAsync("After you authenticate with your corp account, we will collect and store your department, alias, " +
                            "and your corp user id. This data will only be used to validate your current status for the purpose of managing the verified role on this server.");
                        await dmChannel.SendMessageAsync(message);
                        await ReplyAsync($"{Context.User.Mention}, check your DMs for instructions.");
                    }
                    catch (HttpException ex) when (ex.DiscordCode == 50007)
                    {
                        await ReplyAsync($"{Context.User.Mention}, Please temporarily allow DMs from this server and try again.");
                        return;
                    }
                });
                await _verificationService.LoadUserDetails(Context.Configuration.Organization);

                if (_verificationService.Alias.Contains("#EXT#"))
                {
                    await dmChannel.SendMessageAsync("This account is external to Microsoft and is not eligible for validation.");
                    return;
                }

                if (Context.Configuration.RequiresOrganization && !_verificationService.Organization.Equals(Context.Configuration.Organization))
                {
                    await dmChannel.SendMessageAsync($"We see that you are a current Microsoft employee, however, this server requires that you be in a specific org in order to receive the validated status.");
                    return;
                }

                if (!Context.Configuration.AllowedUserTypesFlag.HasFlag(_verificationService.UserType))
                {
                    await dmChannel.SendMessageAsync($"Verification requires that you be one of the following: {Context.Configuration.AllowedUserTypesFlag}");
                    return;
                }

                var corpUserId = Guid.Parse(_verificationService.UserId);

                verification.CorpUserId = corpUserId;
                verification.Alias = _verificationService.Alias;
                verification.ValidatedOn = DateTimeOffset.UtcNow;
                verification.Department = _verificationService.Department;
                await _verificationStorage.SaveVerification(verification);
                await dmChannel.SendMessageAsync($"Thanks for validating your status with Microsoft. You can unlink your accounts at any time with the `{Context.Configuration.Prefix}microsoft leave` command.");
                var role = Context.Guild.Roles.SingleOrDefault(a => a.Id == ulong.Parse(Context.Configuration.RoleId));
                await guildUser.AddRoleAsync(role);
            }
            catch (VerificationException ex) when (ex.ErrorCode == "code_expired")
            {
                _logger.LogInformation($"Code expired for {Context.User.Username}#{Context.User.Discriminator}");
                await dmChannel.SendMessageAsync("Your code has expired.");
            }
            catch (Exception ex)
            {
                await dmChannel.SendMessageAsync("An error occurred saving your validation. Please try again later.");
                _logger.LogCritical(ex, ex.Message);
            }
        }

        [Command("leave")]
        public async Task Leave()
        {
            if (!(Context.User is SocketGuildUser guildUser))
            {
                return;
            }

            var userId = guildUser.Id;
            var guildId = Context.Guild.Id;
            var verificationResult = await _verificationStorage.GetVerification(guildId, userId);

            if (verificationResult != null)
            {
                var deleteResult = await _verificationStorage.RemoveVerification(guildId, userId);

                if (deleteResult)
                {
                    var role = Context.Guild.GetRole(ulong.Parse(Context.Configuration.RoleId));
                    await guildUser.RemoveRoleAsync(role);

                    await ReplyAsync("Your verified status has been removed.");
                }
                else
                {
                    await ReplyAsync($"Unable to remove your verification.");
                }
            }
            else
            {
                await ReplyAsync("You are not currently verified.");
            }
        }

        [Command("enablecleanup"), RequireOwner]
        public async Task EnableCleanup()
        {
            Context.Configuration.CleanupEnabled = true;
            await _guildConfigService.SaveConfiguration(Context.Configuration);
            await ReplyAsync($"This guild is now enabled for automatic cleanup of the <@&{Context.Configuration.RoleId}> role.");
        }

        [Command("disablecleanup"), RequireOwner]
        public async Task DisableCleanup()
        {
            Context.Configuration.CleanupEnabled = false;
            await _guildConfigService.SaveConfiguration(Context.Configuration);
            await ReplyAsync($"This guild is no longer enabled for automatic cleanup of the <@&{Context.Configuration.RoleId}> role.");
        }

        [Command("setusertypes"), RequireOwner]
        public async Task ConfigureUserTypes(params UserType[] userTypes)
        {
            var appliedUserTypes = UserType.None;
            foreach(var userType in userTypes)
            {
                appliedUserTypes |= userType;
            }

            Context.Configuration.AllowedUserTypesFlag = appliedUserTypes;
            await _guildConfigService.SaveConfiguration(Context.Configuration);
            await ReplyAsync($"allowed user types are now set to {Context.Configuration.AllowedUserTypesFlag}");
        }

        [Command("setchannel"), RequireOwner]
        public async Task SetChannel(ITextChannel channel)
        {
            Context.Configuration.ChannelId = channel.Id.ToString();
            await _guildConfigService.SaveConfiguration(Context.Configuration);
            await ReplyAsync($"Channel is now set to {channel.Mention}");
        }

        [Command("clearchannel"), RequireOwner]
        public async Task ClearChannel()
        {
            Context.Configuration.ChannelId = null;
            await _guildConfigService.SaveConfiguration(Context.Configuration);
            await ReplyAsync($"Channel requirement cleared");
        }

        [Command("setrole"), RequireOwner]
        public async Task ConfigureRole(IRole role)
        {
            var guildRole = Context.Guild.Roles.SingleOrDefault(a => a.Id == role.Id);

            if (guildRole == null)
            {
                await ReplyAsync("That role does not exist on this server.");
                return;
            }

            Context.Configuration.RoleId = guildRole.Id.ToString();

            await _guildConfigService.SaveConfiguration(Context.Configuration);
            await ReplyAsync($"Role is now set to <@&{Context.Configuration.RoleId}>.");
        }

        [Command("setprefix"), RequireOwner]
        public async Task SetPrefix(string prefix)
        {
            Context.Configuration.Prefix = prefix;

            await _guildConfigService.SaveConfiguration(Context.Configuration);
            await ReplyAsync($"Prefix is now set to {prefix}.");
        }

        [Command("requireorg"), RequireOwner]
        public async Task RequireOrg(bool require)
        {
            Context.Configuration.RequiresOrganization = require;

            await _guildConfigService.SaveConfiguration(Context.Configuration);
            await ReplyAsync($"RequireOrganization now set to {require}.");
        }

        [Command("setorg"), RequireOwner]
        public async Task SetOrg(string alias)
        {
            Context.Configuration.Organization = alias;

            await _guildConfigService.SaveConfiguration(Context.Configuration);
            await ReplyAsync($"Organization now set to {alias}.");
        }

        [Command("settings"), RequireOwner]
        public async Task Settings()
        {
            var settings = JsonConvert.SerializeObject(Context.Configuration, Formatting.Indented);
            await ReplyAsync(Format.Code(settings));
        }

        [Command("query"), RequireOwner]
        public async Task List(IUser user)
        {
            var userId = user.Id;
            var guildId = Context.Guild.Id;
            var verificationResult = await _verificationStorage.GetVerification(guildId, userId);

            if (verificationResult != null )
            {
                await ReplyAsync($"{user.Username}#{user.Discriminator} is a verified user");
            }
            else
            {
                await ReplyAsync($"{user.Username}#{user.DiscriminatorValue} is not a validated user.");
            }
        }

        [Command("remove"), RequireUserPermission(GuildPermission.ManageRoles)]
        public async Task Remove(IUser user)
        {
            if (!(user is SocketGuildUser guildUser))
            {
                await ReplyAsync($"{user.Username}#{user.DiscriminatorValue} is not a valid user.");
                return;
            }

            var userId = guildUser.Id;
            var guildId = Context.Guild.Id;
            var verificationResult = await _verificationStorage.GetVerification(guildId, userId);

            if (verificationResult != null)
            {
                var deleteResult = await _verificationStorage.RemoveVerification(Context.Guild.Id, guildUser.Id);
                if (deleteResult)
                {
                    var role = Context.Guild.GetRole(uint.Parse(Context.Configuration.RoleId));
                    await guildUser.RemoveRoleAsync(role);
                    await ReplyAsync($"{user.Username}#{user.DiscriminatorValue} removed from validation.");
                }
                else
                {
                    await ReplyAsync($"Unable to remove verification for {user.Username}#{user.Discriminator}.");
                }
            }
            else
            {
                await ReplyAsync($"{user.Username}#{user.DiscriminatorValue} is not a validated user.");
            }

        }
    }
}
