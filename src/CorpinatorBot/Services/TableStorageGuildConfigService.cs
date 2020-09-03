using CorpinatorBot.ConfigModels;
using CorpinatorBot.Extensions;
using Microsoft.Azure.Cosmos.Table;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CorpinatorBot.Services
{
    public class TableStorageGuildConfigService : IGuildConfigService
    {
        private readonly ConcurrentDictionary<ulong, GuildConfiguration> cachedConfig = new ConcurrentDictionary<ulong, GuildConfiguration>();
        private readonly CloudTableClient _tableClient;

        public TableStorageGuildConfigService(BotSecretsConfig secrets)
        {
            var storageClient = CloudStorageAccount.Parse(secrets.TableStorageConnectionString);
            _tableClient = storageClient.CreateCloudTableClient();
        }

        public async Task<GuildConfiguration> GetGuildConfiguration(ulong guildId)
        {
            if (cachedConfig.TryGetValue(guildId, out var config))
            {
                return config;
            }

            var table = _tableClient.GetTableReference("configuration");
            var configResult = await table.ExecuteAsync(TableOperation.Retrieve<GuildConfiguration>("config", guildId.ToString()));
            if (configResult.Result == null)
            {
                config = new GuildConfiguration
                {
                    PartitionKey = "config",
                    RowKey = guildId.ToString(),
                    Prefix = "%%",
                    RequiresOrganization = false,
                    Organization = string.Empty,
                    RoleId = default,
                    ChannelId = default
                };
            }
            else
            {
                config = configResult.Result as GuildConfiguration;
            }
            cachedConfig.AddOrUpdate(guildId, config, (key, value) => value);
            return config;
        }

        public async Task SaveConfiguration(GuildConfiguration config)
        {
            var table = _tableClient.GetTableReference("configuration");
            await table.ExecuteAsync(TableOperation.InsertOrReplace(config));
        }

        public async Task<List<GuildConfiguration>> GetAllConfigurations()
        {
            var table = _tableClient.GetTableReference("configuration");
            var configs = await table.GetAllRecords<GuildConfiguration>();

            return configs;
        }
    }
}
