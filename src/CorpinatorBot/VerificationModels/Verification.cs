using System;

namespace CorpinatorBot.VerificationModels
{
    public class Verification
    {
        public ulong GuildId { get; set; }
        public ulong DiscordId { get; set; }
        public Guid CorpUserId { get; set; }
        public string Alias { get; set; }
        public DateTimeOffset? ValidatedOn { get; set; }
        public string Department { get; set; }
        public bool BypassOrgValidation { get; set; }
    }
}
