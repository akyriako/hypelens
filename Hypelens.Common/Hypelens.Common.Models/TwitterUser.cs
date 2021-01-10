using System;
using Newtonsoft.Json;

namespace Hypelens.Common.Models
{
    public class TwitterUser
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("screenName")]
        public string ScreenName { get; set; }

        [JsonProperty("imageUrl")]
        public string PublicImageUrl { get; set; }

        [JsonProperty("location")]
        public string Location { get; set; }

        [JsonProperty("followers")]
        public int FollowersCount { get; set; }

        [JsonProperty("followings")]
        public int FollowingsCount { get; set; }
    }
}
