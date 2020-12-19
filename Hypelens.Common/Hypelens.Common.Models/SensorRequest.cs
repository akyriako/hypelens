using System;
using Newtonsoft.Json;

namespace Hypelens.Common.Models
{
    public class SensorRequest
    {
        [JsonProperty("sensorId")]
        public string SensorId { get; set; }

        [JsonProperty("tenantId")]
        public string TenantId { get; set; }

        [JsonProperty("hashtags")]
        public string[] Hashtags { get; set; }

        [JsonProperty("accounts")]
        public string[] Accounts { get; set; }
    }
}
