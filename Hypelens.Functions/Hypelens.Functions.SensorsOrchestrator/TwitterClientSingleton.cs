using System;
using Tweetinvi;

namespace Hypelens.Functions.SensorsOrchestrator
{
    public class TwitterClientSingleton
    {
        private static TwitterClientSingleton s_Instance = null;

        public TwitterClient TwitterClient { get; }

        public static TwitterClientSingleton Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = new TwitterClientSingleton();
                }

                return s_Instance;
            }
        }

        private TwitterClientSingleton()
        {
            string consumerKey = "O4B0C8Ph9Tg5Xa83TBSreicvf";
            string consumerSecret = "Hl1yOqzMXudYTb4PcLpI15CgAwJpcbxIsDbe2R39ZYlMpRYefy";
            string accessToken = "1340260668977668096-fiRdO0HgKz1DIfpSd7wueQZGlPqoEU";
            string accessSecret = "S2BLCXJ2kFU1F7NssQ7XMmxgcNnPx3ZobwWdzbkqvRRn6";

            TwitterClient = new TwitterClient(consumerKey, consumerSecret, accessToken, accessSecret);
        }
    }
}
