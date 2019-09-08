using CorpinatorBot.VerificationModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CorpinatorBot.Services
{
    public interface IVerificationStorageService
    {
        Task<Verification> GetVerification(ulong guildId, ulong discordId);
        Task SaveVerification(Verification verification);
        Task<bool> RemoveVerification(ulong guildId, ulong discordId);
        Task<List<Verification>> GetAllVerificationsInGuild(ulong guildId);
    }
}