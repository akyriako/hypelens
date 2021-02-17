using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hypelens.Common.Models;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Azure.AI.TextAnalytics;
using Azure;
using Newtonsoft.Json;

namespace Hypelens.Functions.TextAnalyzer
{
    public static class TextAnalyzer
    {
        private static readonly AzureKeyCredential s_AzureKeyCredential = new AzureKeyCredential("39c231d6d0ed45f886045015ebd89ab5");
        private static readonly Uri s_CognitiveServicesEndpoint = new Uri(@"https://hypelens-cognsvc-01.cognitiveservices.azure.com/");

        [FunctionName("TextAnalyzer")]
        public static async Task Run(
            [EventHubTrigger("sensors-collected-items", Connection = "EventHub")] EventData[] inputEvents,
            [EventHub("sensors-analyzed-items", Connection = "EventHub")] IAsyncCollector<SensorCollectedTweet> outputEvents,
            ILogger log)
        {
            var exceptions = new List<Exception>();
            var textAnalyticsClient = new TextAnalyticsClient(s_CognitiveServicesEndpoint, s_AzureKeyCredential);

            foreach (EventData eventData in inputEvents)
            {
                try
                {
                    string messageBody = Encoding.UTF8.GetString(eventData.Body.Array, eventData.Body.Offset, eventData.Body.Count);
                    SensorCollectedTweet sensorCollectedTweet = JsonConvert.DeserializeObject<SensorCollectedTweet>(messageBody);

                    //// Replace these two lines with your processing logic.
                    //log.LogInformation($"C# Event Hub trigger function processed a message: {messageBody}");
                    //await Task.Yield();

                    DocumentSentiment documentSentiment = textAnalyticsClient.AnalyzeSentiment(sensorCollectedTweet.Text);
                    int sentiment = 0;

                    switch (documentSentiment.Sentiment.ToString())
                    {
                        case "Positive":
                            sentiment = 1;
                            break;
                        case "Neutral":
                            sentiment = 0;
                            break;
                        case "Negative":
                            sentiment = -1;
                            break;
                        default:
                            sentiment = 0;
                            break;
                    }

                    PolarityScores polarityScores = new PolarityScores()
                    {
                        Positive = documentSentiment.ConfidenceScores.Positive,
                        Neutral = documentSentiment.ConfidenceScores.Neutral,
                        Negative = documentSentiment.ConfidenceScores.Negative
                    };

                    sensorCollectedTweet.Sentiment = new Sentiment()
                    {
                        Polarity = sentiment,
                        Scores = polarityScores
                    };

                    await outputEvents.AddAsync(sensorCollectedTweet);
                }
                catch (Exception e)
                {
                    // We need to keep processing the rest of the batch - capture this exception and continue.
                    // Also, consider capturing details of the message that failed processing so it can be processed again later.
                    exceptions.Add(e);
                }
            }

            // Once processing of the batch is complete, if any messages in the batch failed processing throw an exception so that there is a record of the failure.

            if (exceptions.Count > 1)
            {
                throw new AggregateException(exceptions);
            }

            if (exceptions.Count == 1)
            {
                throw exceptions.Single();
            }
        }
    }
}
