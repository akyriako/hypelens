using System;
using Newtonsoft.Json;

namespace Hypelens.Common.Models
{
    public class Sensor
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("instanceId")]
        public string InstanceId { get; set; }

        [JsonProperty("tenantId")]
        public string TenantId { get; set; }

        [JsonProperty("hashtags")]
        public string[] Hashtags { get; set; }

        [JsonProperty("accounts")]
        public string[] Accounts { get; set; }
    }
}
