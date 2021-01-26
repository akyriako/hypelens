using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Hypelens.Common.Models;
using Microsoft.Azure.WebJobs;
using Tweetinvi;
using Tweetinvi.Models;
using Tweetinvi.Streaming;

namespace Hypelens.Functions.SensorsOrchestrator
{
    public static class Extensions
    {
        public static string Sanitize(this string text)
        {
            return Regex.Replace(text, @"(@[A-Za-z0-9]+)|([^0-9A-Za-z \t])|(\w+:\/\/\S+)", " ").ToString();
        }

        public static async Task<Task> AddAsync<T>(this IAsyncCollector<T> asyncCollector, T item, int itemsQuota, int itemsIdx, bool pushToEventHub = false)
        {
            if ((itemsIdx < itemsQuota) && pushToEventHub)
            {
                return asyncCollector.AddAsync(item);
            }

            return Task.CompletedTask;
        }

        public static void TryStopFilteredStreamIfNotNull(this IFilteredStream filteredStream)
        {
            if (filteredStream != null)
            {
                filteredStream.Stop();
                filteredStream = null;
            }
        }

        public static void AddFollow(this IFilteredStream filteredStream, TwitterClient twitterClient, Sensor sensor)
        {
            if (sensor.Follow != null && sensor.Follow.Count() > 0)
            {
                var usersResponse = twitterClient.UsersV2.GetUsersByNameAsync(sensor.Follow).GetAwaiter().GetResult();

                if (usersResponse.Users != null && usersResponse.Users.Count() > 0)
                {
                    usersResponse.Users.ToList().ForEach(user =>
                    {
                        filteredStream.AddFollow(Convert.ToInt64(user.Id));
                    });
                }
            }
        }

        public static void AddTrack(this IFilteredStream filteredStream, Sensor sensor)
        {
            if (sensor.Hashtags != null && sensor.Hashtags.Count() > 0)
            {
                sensor.Hashtags.ToList().ForEach(hashtag =>
                {
                    filteredStream.AddTrack($"{hashtag}");
                });
            }
        }

        public static void AddLanguageFilter(this IFilteredStream filteredStream, Sensor sensor)
        {
            if (sensor.Languages != null && sensor.Languages.Count() > 0)
            {
                sensor.Languages.ToList().ForEach(language =>
                {
                    object languageFilter;
                    Enum.TryParse(typeof(LanguageFilter), language, out languageFilter);

                    filteredStream.AddLanguageFilter((LanguageFilter)languageFilter);
                });
            }
            else
            {
                filteredStream.AddLanguageFilter(LanguageFilter.English);
            }
        }
    }
}
