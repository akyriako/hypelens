using System;
using Newtonsoft.Json;

namespace Hypelens.Common.Models
{
    public class TenantSettings
    {
        [JsonProperty("id")]
        public string TenantId { get; set; }

        [JsonProperty("appId")]
        public string AppId { get; set; }

        [JsonProperty("language")]
        public string Language { get; set; }

        [JsonProperty("consumerKey")]
        public string ConsumerKey { get; set; }

        [JsonProperty("consumerSecret")]
        public string ConsumerSecret { get; set; }

        [JsonProperty("accessToken")]
        public string AccessToken { get; set; }

        [JsonProperty("accessSecret")]
        public string AccessSecret { get; set; }
    }
}
