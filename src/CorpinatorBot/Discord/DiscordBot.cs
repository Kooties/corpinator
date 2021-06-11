using CorpinatorBot.ConfigModels;
using CorpinatorBot.Services;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace CorpinatorBot.Discord
{
    public class DiscordBot : IHostedService
    {
        private readonly IHostApplicationLifetime _lifetime;
        private readonly IServiceProvider _serviceProvider;
        private readonly IVerificationStorageService _verificationStorage;
        private readonly ILogger<DiscordBot> _logger;
        private readonly IGuildConfigService _guildConfigService;
        private readonly BotSecretsConfig _connectionConfig;
        private readonly DiscordSocketConfig _botConfig;
        private readonly DiscordSocketClient _discordClient;


        private CommandService _commandService;
        private bool _exiting;

        public DiscordBot(IServiceProvider serviceProvider, ILogger<DiscordBot> logger, DiscordSocketConfig botConfig, BotSecretsConfig connectionConfig,
            IGuildConfigService guildConfigService, IVerificationStorageService verificationStorage, IHostApplicationLifetime lifetime)
        {
            _lifetime = lifetime;
            _serviceProvider = serviceProvider;
            _verificationStorage = verificationStorage;
            _logger = logger;
            _guildConfigService = guildConfigService;
            _connectionConfig = connectionConfig;
            _botConfig = botConfig;
            _discordClient = new DiscordSocketClient(botConfig);
        }

        public Task StartAsync(CancellationToken cancellationToken) => StartBotAsync();

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _exiting = true;

            return StopBotAsync();
        }

        public async Task StartBotAsync()
        {
            _discordClient.Ready += OnReady;
            _discordClient.Log += OnLog;
            _discordClient.Disconnected += OnDisconnected;

            try
            {
                await ConnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to connect.");
                throw;
            }
        }

        public async Task StopBotAsync()
        {
            _logger.LogInformation("Received stop signal, shutting down.");
            await _discordClient.StopAsync();
            await _discordClient.LogoutAsync();
        }

        private async Task ConnectAsync()
        {
            var maxAttempts = 10;
            var currentAttempt = 0;
            do
            {
                currentAttempt++;
                try
                {
                    await _discordClient.LoginAsync(TokenType.Bot, _connectionConfig.BotToken);
                    await _discordClient.StartAsync();
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to connect: {ex.Message}");
                    await Task.Delay(currentAttempt * 1000);
                }
            }
            while (currentAttempt < maxAttempts);
        }

        private Task OnDisconnected(Exception ex)
        {
            if (ex is GatewayReconnectException)
            {
                return Task.CompletedTask;
            }

            if (!_exiting)
            {
                _lifetime.StopApplication();
            }
            return Task.CompletedTask;
        }

        private Task OnReady()
        {
            _discordClient.MessageReceived += OnMessageReceived;
            _discordClient.LeftGuild += OnBotLeftGuild;
            _discordClient.UserLeft += OnMemberLeftGuild;
            _discordClient.UserBanned += OnMemberBanned;
            _commandService = new CommandService(new CommandServiceConfig
            {
                LogLevel = _botConfig.LogLevel,
                SeparatorChar = ' ',
                ThrowOnError = true
            });

            _commandService.AddModulesAsync(Assembly.GetExecutingAssembly(), _serviceProvider);
            return Task.CompletedTask;
        }

        private Task OnMemberBanned(SocketUser user, SocketGuild guild)
        {
            return _verificationStorage.RemoveVerification(user.Id, guild.Id);
        }

        private Task OnMemberLeftGuild(SocketGuildUser user)
        {
            return _verificationStorage.RemoveVerification(user.Guild.Id, user.Id);
        }

        private async Task OnBotLeftGuild(SocketGuild guild)
        {
            _logger.LogInformation($"Leaving guild {guild.Name}({guild.Id}); removing verifications for the users.");
            var verificationsForGuild = await _verificationStorage.GetAllVerificationsInGuild(guild.Id);
            foreach (var verification in verificationsForGuild)
            {
                try
                {
                    await _verificationStorage.RemoveVerification(verification.GuildId, verification.DiscordId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Unable to remove verification for {verification.Alias}({verification.DiscordId}) in guild {guild.Name}({guild.Id})");
                }
            }
        }

        private Task OnMessageReceived(SocketMessage message)
        {
            if (message.Author.IsBot)
            {
                return Task.CompletedTask;
            }

            if (!(message is SocketUserMessage userMessage))
            {
                return Task.CompletedTask;
            }

            if (!(message.Channel is SocketGuildChannel guildChannel))
            {
                return Task.CompletedTask;
            }

            _ = Task.Run(async () =>
              {
                  var argPos = 0;

                  var guildId = guildChannel.Guild.Id;
                  var config = await _guildConfigService.GetGuildConfiguration(guildId);
                  if (config.ChannelId != default && config.ChannelId != guildChannel.Id.ToString())
                  {
                      return;
                  }

                  if (!userMessage.HasStringPrefix(config.Prefix, ref argPos))
                  {
                      return;
                  }

                  var context = new GuildConfigSocketCommandContext(_discordClient, userMessage, config);

                  var result = await _commandService.ExecuteAsync(context, argPos, _serviceProvider, MultiMatchHandling.Best);

                  if (!result.IsSuccess && (result.Error != CommandError.UnknownCommand || result.Error != CommandError.BadArgCount))
                  {
                      _logger.LogError($"{result.Error}: {result.ErrorReason}");
                      await userMessage.AddReactionAsync(new Emoji("⚠"));
                  }
              });
            return Task.CompletedTask;
        }

        private Task OnLog(LogMessage arg)
        {
            var severity = MapToLogLevel(arg.Severity);
            _logger.Log(severity, 0, arg, arg.Exception, (state, ex) => state.ToString());
            return Task.CompletedTask;
        }

        private static LogLevel MapToLogLevel(LogSeverity severity)
        {
            return severity switch
            {
                LogSeverity.Critical => LogLevel.Critical,
                LogSeverity.Error => LogLevel.Error,
                LogSeverity.Warning => LogLevel.Warning,
                LogSeverity.Info => LogLevel.Information,
                LogSeverity.Verbose => LogLevel.Trace,
                LogSeverity.Debug => LogLevel.Debug,
                _ => LogLevel.Information,
            };
        }
    }
}
