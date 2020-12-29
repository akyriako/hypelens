using System;
using Newtonsoft.Json;

namespace Hypelens.Common.Models
{
    public class SensorCollectedItem
    {
        [JsonProperty("id")]
        public string SensorId { get; set; }

        [JsonProperty("tenantId")]
        public string TenantId { get; set; }

        [JsonProperty("hashtags")]
        public string[] Hashtags { get; set; }

        [JsonProperty("mentions")]
        public string[] Mentions { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }
    }
}
