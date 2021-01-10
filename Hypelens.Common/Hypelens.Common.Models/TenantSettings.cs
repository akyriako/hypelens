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

        public static TenantSettings GetDevelopmentSettings()
        {
            TenantSettings tenantSettings = new TenantSettings()
            {
                TenantId = "e489f125-ad73-4bad-ae5b-1377ad51492e",
                ConsumerKey = "O4B0C8Ph9Tg5Xa83TBSreicvf",
                ConsumerSecret = "Hl1yOqzMXudYTb4PcLpI15CgAwJpcbxIsDbe2R39ZYlMpRYefy",
                AccessToken = "1340260668977668096-fiRdO0HgKz1DIfpSd7wueQZGlPqoEU",
                AccessSecret = "S2BLCXJ2kFU1F7NssQ7XMmxgcNnPx3ZobwWdzbkqvRRn6"
            };

            return tenantSettings;
        }
    }

    
}
