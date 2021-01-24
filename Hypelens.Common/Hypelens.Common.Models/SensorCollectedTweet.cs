using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Tweetinvi.Models;

namespace Hypelens.Common.Models
{
    public class SensorCollectedTweet
    {
        [JsonProperty("id")]
        public string SensorId { get; set; }

        [JsonProperty("tenantId")]
        public string TenantId { get; set; }

        [JsonProperty("tweetId")]
        public string TweetId { get; set; }

        [JsonProperty("hashtags")]
        public string[] Hashtags { get; set; }

        [JsonProperty("mentions")]
        public string[] Mentions { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("sentiment")]
        public Sentiment Sentiment { get; set; }

        [JsonProperty("favorites")]
        public int FavoritesCount { get; set; }

        [JsonProperty("retweets")]
        public int RetweetsCount { get; set; }

        [JsonProperty("originalTweetId")]
        public string OriginalTweetId { get; set; }

        [JsonProperty("location")]
        public string Location { get; set; }

        [JsonProperty("createdBy")]
        public TwitterUser CreatedBy { get; set; }

        public SensorCollectedTweet()
        {

        }

        public SensorCollectedTweet(Sensor sensor, ITweet tweet)
        {
            SensorId = sensor.Id;
            TenantId = sensor.TenantId;
            TweetId = tweet.IdStr;
            Text = tweet.Text;
            Url = tweet.Url;
            CreatedAt = tweet.CreatedAt.DateTime;
            FavoritesCount = tweet.FavoriteCount;
            RetweetsCount = tweet.RetweetCount;

            if (tweet.Hashtags != null || tweet.Hashtags.Count > 0)
            {
                List<string> hashtags = new List<string>();

                tweet.Hashtags.ForEach(hashtag =>
                {
                    hashtags.Add(hashtag.Text);
                });

                this.Hashtags = hashtags.ToArray();
            }

            if (tweet.UserMentions != null || tweet.UserMentions.Count > 0)
            {
                List<string> userMentions = new List<string>();

                tweet.UserMentions.ForEach(userMention =>
                {
                    userMentions.Add(userMention.Name);
                });

                this.Mentions = userMentions.ToArray();
            }

            string[] locationParts = tweet.CreatedBy.Location == null ? null : tweet.CreatedBy.Location.Split(",", StringSplitOptions.RemoveEmptyEntries);

            TwitterUser createdBy = new TwitterUser()
            {
                Id = tweet.CreatedBy.IdStr,
                Name = tweet.CreatedBy.Name,
                ScreenName = tweet.CreatedBy.ScreenName,
                PublicImageUrl = tweet.CreatedBy.ProfileImageUrl,
                Location = locationParts == null || locationParts.Length != 2 ? null : tweet.CreatedBy.Location,
                FollowersCount = tweet.CreatedBy.FollowersCount,
                FollowingsCount = tweet.CreatedBy.FriendsCount
            };

            this.CreatedBy = createdBy;

            if (tweet.Coordinates != null)
            {
                this.Location = $"{tweet.Coordinates.Latitude.ToString()}, {tweet.Coordinates.Longitude.ToString()}";
            }
            else
            {
                this.Location = this.CreatedBy.Location ?? null;
            }
        }
    }
}
