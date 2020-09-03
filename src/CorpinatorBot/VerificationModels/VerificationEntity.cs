using Microsoft.Azure.Cosmos.Table;
using System;

namespace CorpinatorBot.VerificationModels
{
    public class VerificationEntity : TableEntity
    {
        public Guid CorpUserId { get; set; }
        public string Alias { get; set; }
        public DateTimeOffset? ValidatedOn { get; set; }
        public string Department { get; set; }
        public bool BypassOrgValidation { get; set; } = false;
    }
}
