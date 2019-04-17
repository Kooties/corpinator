using CorpinatorBot.ConfigModels;
using CorpinatorBot.Extensions;
using CorpinatorBot.Services;
using CorpinatorBot.VerificationModels;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace CorpinatorBot.Discord
{
    public class DiscordBot : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DiscordBot> _logger;
        private readonly BotSecretsConfig _connectionConfig;
        private readonly CloudTable _table;
        private readonly DiscordSocketConfig _botConfig;
        private readonly DiscordSocketClient _discordClient;


        private CommandService _commandService;
        private bool _exiting;

        public DiscordBot(IServiceProvider serviceProvider, ILogger<DiscordBot> logger, DiscordSocketConfig botConfig, BotSecretsConfig connectionConfig, CloudTableClient tableClient)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _connectionConfig = connectionConfig;
            _table = tableClient.GetTableReference("configuration");
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
                    _logger.LogError($"Fialed to connect: {ex.Message}");
                    await Task.Delay(currentAttempt * 1000);
                }
            }
            while (currentAttempt < maxAttempts);
        }

        private Task OnDisconnected(Exception arg)
        {
            if (!_exiting)
            {
                Environment.Exit(0);
            }
            return Task.CompletedTask;
        }

        private Task OnReady()
        {
            _discordClient.MessageReceived += OnMessageReceived;
            _commandService = new CommandService(new CommandServiceConfig
            {
                LogLevel = _botConfig.LogLevel,
                SeparatorChar = ' ',
                ThrowOnError = true
            });

            _commandService.AddModulesAsync(Assembly.GetExecutingAssembly(), _serviceProvider);
            return Task.CompletedTask;
        }

        private async Task OnMessageReceived(SocketMessage message)
        {
            if (message.Author.IsBot)
            {
                return;
            }

            if (!(message is SocketUserMessage userMessage))
            {
                return;
            }

            if (!(message.Channel is SocketGuildChannel guildChannel))
            {
                return;
            }

            _ = Task.Run(async () =>
              {
                  var argPos = 0;

                  var guildId = guildChannel.Guild.Id.ToString();

                  GuildConfiguration config;
                  var configResult = await _table.ExecuteAsync(TableOperation.Retrieve<GuildConfiguration>("config", guildId));
                  if (configResult.Result == null)
                  {
                      config = new GuildConfiguration
                      {
                          PartitionKey = "config",
                          RowKey = guildId,
                          Prefix = "!",
                          RequiresOrganization = false,
                          Organization = string.Empty,
                          RoleId = default
                      };
                  }
                  else
                  {
                      config = configResult.Result as GuildConfiguration;
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
            await Task.CompletedTask;
        }

        private Task OnLog(LogMessage arg)
        {
            var severity = MapToLogLevel(arg.Severity);
            _logger.Log(severity, 0, arg, arg.Exception, (state, ex) => state.ToString());
            return Task.CompletedTask;
        }

        private LogLevel MapToLogLevel(LogSeverity severity)
        {
            switch (severity)
            {
                case LogSeverity.Critical:
                    return LogLevel.Critical;
                case LogSeverity.Error:
                    return LogLevel.Error;
                case LogSeverity.Warning:
                    return LogLevel.Warning;
                case LogSeverity.Info:
                    return LogLevel.Information;
                case LogSeverity.Verbose:
                    return LogLevel.Trace;
                case LogSeverity.Debug:
                    return LogLevel.Debug;
                default:
                    return LogLevel.Information;
            }
        }
    }
}
