using System;
using Newtonsoft.Json;

namespace Hypelens.Common.Models
{
    public class Sensor
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        //[JsonProperty("instanceId")]
        //public string InstanceId { get; set; }

        [JsonProperty("tenantId")]
        public string TenantId { get; set; }

        [JsonProperty("hashtags")]
        public string[] Hashtags { get; set; }

        [JsonProperty("follow")]
        public string[] Follow { get; set; }

        [JsonProperty("languages")]
        public string[] Languages { get; set; }

        [JsonProperty("process")]
        public bool Process { get; set; }

        [JsonProperty("active")]
        public bool Active { get; set; }

        [JsonProperty("scheduledEnqueuedWaitTime")]
        public uint? ScheduledEnqueuedWaitTime { get; set; }


    }
}
