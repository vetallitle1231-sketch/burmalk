using System;
using System.Collections.Generic;

namespace Burmalk.Models
{
    public sealed class PassportSection
    {
        public string Title, Icon;
        public List<string> Lines = new List<string>();
    }
    public sealed class PcPassport
    {
        public DateTime CreatedLocal;
        public string Style = "Pulse";
        public string OwnerLabel = "ТВОЙ ПК";
        public List<PassportSection> Sections = new List<PassportSection>();
        public PcRankResult Rank = new PcRankResult();
        public DateTime? TestedLocal;
        public string Method = RankMethodLabel;
        public const string RankMethodLabel = "burmalk-relative-v1";
    }
}
