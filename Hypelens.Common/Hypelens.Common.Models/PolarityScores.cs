using System;
using Newtonsoft.Json;

namespace Hypelens.Common.Models
{
    public class PolarityScores
    {
        [JsonProperty("positive")]
        public double Positive { get; set; }

        [JsonProperty("neutral")]
        public double Neutral { get; set; }

        [JsonProperty("negative")]
        public double Negative { get; set; }
    }
}
