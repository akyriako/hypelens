using System;
using Newtonsoft.Json;

namespace Hypelens.Common.Models
{
    public class Sentiment
    {
        [JsonProperty("polarity")]
        public int Polarity { get; set; }

        [JsonProperty("polarityScores")]
        public PolarityScores Scores { get; set; }
    }
}
