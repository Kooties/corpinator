using CorpinatorBot.ConfigModels;
using CorpinatorBot.Extensions;
using CorpinatorBot.VerificationModels;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CorpinatorBot.Services
{
    public class TableStorageVerificationStorageService : IVerificationStorageService
    {
        private readonly CloudTableClient _tableClient;

        public TableStorageVerificationStorageService(BotSecretsConfig secrets)
        {
            var storageClient = CloudStorageAccount.Parse(secrets.TableStorageConnectionString);
            _tableClient = storageClient.CreateCloudTableClient();
        }

        public async Task<Verification> GetVerification(ulong guildId, ulong discordId)
        {
            var verificationsReference = _tableClient.GetTableReference("verifications");
            await verificationsReference.CreateIfNotExistsAsync();
            var verificationEntity = await verificationsReference.ExecuteAsync(TableOperation.Retrieve<VerificationEntity>(guildId.ToString(), discordId.ToString()));
            if (verificationEntity.HttpStatusCode == 200)
            {
                var entity = verificationEntity.Result as VerificationEntity;
                return new Verification
                {
                    DiscordId = ulong.Parse(entity.RowKey),
                    GuildId = ulong.Parse(entity.PartitionKey),
                    Alias = entity.Alias,
                    CorpUserId = entity.CorpUserId,
                    Department = entity.Department,
                    ValidatedOn = entity.ValidatedOn,
                    BypassOrgValidation = entity.BypassOrgValidation
                };
            }

            return null;
        }

        public async Task SaveVerification(Verification verification)
        {
            var verificationsReference = _tableClient.GetTableReference("verifications");
            var verificationEntity = await verificationsReference.ExecuteAsync(TableOperation.Retrieve<VerificationEntity>(verification.GuildId.ToString(), verification.DiscordId.ToString()));

            var entity = verificationEntity.Result as VerificationEntity;
            if (verificationEntity.HttpStatusCode != 200)
            {
                entity = new VerificationEntity
                {
                    RowKey = verification.DiscordId.ToString(),
                    PartitionKey = verification.GuildId.ToString()
                };
            }

            entity.Alias = verification.Alias;
            entity.CorpUserId = verification.CorpUserId;
            entity.Department = verification.Department;
            entity.ValidatedOn = verification.ValidatedOn;
            entity.BypassOrgValidation = verification.BypassOrgValidation;

            await verificationsReference.CreateIfNotExistsAsync();
            await verificationsReference.ExecuteAsync(TableOperation.InsertOrMerge(entity));
        }

        public async Task<bool> RemoveVerification(ulong guildId, ulong discordId)
        {
            var verificationsReference = _tableClient.GetTableReference("verifications");
            var verificationEntity = await verificationsReference.ExecuteAsync(TableOperation.Retrieve<VerificationEntity>(guildId.ToString(), discordId.ToString()));

            if(verificationEntity.HttpStatusCode == 404)
            {
                return false;
            }

            var deleteResult = await verificationsReference.ExecuteAsync(TableOperation.Delete(verificationEntity.Result as VerificationEntity));
            return deleteResult.HttpStatusCode == 204;
        }

        public async Task<List<Verification>> GetAllVerificationsInGuild(ulong guildId)
        {
            var verificationsReference = _tableClient.GetTableReference("verifications");
            var records = await verificationsReference.GetAllRecords<VerificationEntity>();

            return records.Where(a => ulong.Parse(a.PartitionKey) == guildId).Select(a => new Verification
            {
                DiscordId = ulong.Parse(a.RowKey),
                GuildId = ulong.Parse(a.PartitionKey),
                Alias = a.Alias,
                CorpUserId = a.CorpUserId,
                Department = a.Department,
                ValidatedOn = a.ValidatedOn,
                BypassOrgValidation = a.BypassOrgValidation
            }).ToList();
        }
    }
}
