using CorpinatorBot.ConfigModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CorpinatorBot.Services
{
    public interface IGuildConfigService
    {
        Task SaveConfiguration(GuildConfiguration config);
        Task<GuildConfiguration> GetGuildConfiguration(ulong guildId);
        Task<List<GuildConfiguration>> GetAllConfigurations();
    }
}