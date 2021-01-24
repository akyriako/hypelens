using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Hypelens.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.ServiceBus;
using Newtonsoft.Json;
using Tweetinvi;
using Tweetinvi.Models;
using System.Text;
using VaderSharp;
using System.Threading;

namespace Hypelens.Functions.SensorsOrchestrator
{
    public static class SensorsOrchestrator
    {
        [FunctionName("SensorsOrchestrator")]
        public static async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var sensorBootstrap = context.GetInput<Tuple<TenantSettings, Sensor>>();

            var outputs = new List<string>();
            outputs.Add(await context.CallActivityAsync<string>("ActivateSensor", sensorBootstrap));
            
            return outputs;
        }

        [FunctionName("ActivateSensor")]
        public static async Task<string> ActivateSensorAsync(
            [ActivityTrigger] Tuple<TenantSettings, Sensor> sensorBootstrap,
            [EventHub("sensors-collected-items", Connection = "EventHub")] IAsyncCollector<SensorCollectedTweet> outputEvents,
            ILogger log)
        {
            var settings = sensorBootstrap.Item1;
            var sensor = sensorBootstrap.Item2;

            int matchingTweetsCnt = 0;
            int buffedtoEventHubIdx = 0;

            //SentimentIntensityAnalyzer sentimentIntensityAnalyzer = new SentimentIntensityAnalyzer();
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(new TimeSpan(0, 5, 0));
            Dictionary<string, SensorCollectedTweet> sensorCollectedItems = new Dictionary<string, SensorCollectedTweet>();

            var semaphore = new SemaphoreSlim(1);

            log.LogInformation($"Saying hello from sensor {sensor.TenantId}.");

            var userClient = new TwitterClient(settings.ConsumerKey, settings.ConsumerSecret, settings.AccessToken, settings.AccessSecret);

            var filteredStream = userClient.Streams.CreateFilteredStream();
            filteredStream.TweetMode = TweetMode.Extended;
            filteredStream.StallWarnings = false;

            filteredStream.AddTrack(sensor);
            filteredStream.AddFollow(userClient, sensor);
            filteredStream.AddLanguageFilter(sensor);

            filteredStream.MatchingTweetReceived += async (sender, args) =>
            {
                if (matchingTweetsCnt < 1500 && !cancellationTokenSource.IsCancellationRequested)
                {
                    try
                    {
                        bool added = false;
                        matchingTweetsCnt++;

                        if (sensor.Process)
                        {
                            //var tweetPolarityScores = sentimentIntensityAnalyzer.PolarityScores(args.Tweet.Text.Sanitize());
                            SensorCollectedTweet tweet = new SensorCollectedTweet(sensor, args.Tweet);

                            if (args.Tweet.QuotedTweet != null)
                            {
                                //var quotedTweetPolarityScores = sentimentIntensityAnalyzer.PolarityScores(args.Tweet.QuotedTweet.Text.Sanitize());
                                SensorCollectedTweet quotedTweet = new SensorCollectedTweet(sensor, args.Tweet.QuotedTweet);

                                tweet.OriginalTweetId = quotedTweet.TweetId;

                                added = sensorCollectedItems.TryAdd(quotedTweet.TweetId, quotedTweet);

                                if (added)
                                {
                                    WriteCollectedItemToConsole(quotedTweet);
                                }
                            }
                            else if (args.Tweet.RetweetedTweet != null && args.Tweet.QuotedTweet == null)
                            {
                                //var retweetedTweetPolarityScores = sentimentIntensityAnalyzer.PolarityScores(args.Tweet.RetweetedTweet.Text.Sanitize());
                                SensorCollectedTweet retweetedTweet = new SensorCollectedTweet(sensor, args.Tweet.RetweetedTweet);

                                tweet.OriginalTweetId = retweetedTweet.TweetId;

                                added = sensorCollectedItems.TryAdd(retweetedTweet.TweetId, retweetedTweet);

                                if (added)
                                {
                                    WriteCollectedItemToConsole(retweetedTweet);
                                }
                            }

                            added = sensorCollectedItems.TryAdd(tweet.TweetId, tweet);

                            if (added)
                            {
                                WriteCollectedItemToConsole(tweet);

                                await outputEvents.AddAsync(tweet);
                                Interlocked.Increment(ref buffedtoEventHubIdx);

                                await semaphore.WaitAsync();

                                if (buffedtoEventHubIdx >= 32)
                                {
                                    await outputEvents.FlushAsync(new CancellationTokenSource(new TimeSpan(0,0,15)).Token);
                                    Interlocked.Exchange(ref buffedtoEventHubIdx, 0);

                                    System.Console.WriteLine("##### FLUSH #####");
                                }

                                semaphore.Release();
                            }
                        }
                    }
                    catch (Exception matchingTweetReceivedException)
                    {
                        Console.WriteLine(matchingTweetReceivedException.Message);
                    }
                }
                else
                {
                    System.Console.WriteLine("##### END #####");

                    filteredStream.TryStopFilteredStreamIfNotNull();
                }
            };

            filteredStream.NonMatchingTweetReceived += (sender, args) =>
            {
                System.Console.WriteLine($"NOT MATCHING: {args.Tweet.Text}");
            };

            filteredStream.WarningFallingBehindDetected += TwitterStream_WarningFallingBehindDetected;
            filteredStream.LimitReached += TwitterStream_LimitReached;

            try
            {
                Task twitterStartMatchingAnyConditionTask = filteredStream.StartMatchingAnyConditionAsync();
                Task.WaitAny(new Task[] { twitterStartMatchingAnyConditionTask }, cancellationTokenSource.Token);
            }
            catch (OperationCanceledException operationCancelledException)
            {

            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
                System.Console.WriteLine("##### FAILED #####");
            }
            finally
            {
                filteredStream.TryStopFilteredStreamIfNotNull();
            }

            //sensorCollectedItems.ToList().ForEach(async pair =>
            //{
            //    await outputEvents.AddAsync(pair.Value);
            //});

            return $"Hello {sensor.TenantId}!";
        }

        private static void WriteCollectedItemToConsole(SensorCollectedTweet sensorCollectedItem)
        {
            System.Console.WriteLine($"{DateTime.Now.ToShortTimeString()}\t{sensorCollectedItem.TweetId}\t{sensorCollectedItem.FavoritesCount.ToString()}\t{sensorCollectedItem.RetweetsCount.ToString()}\t{sensorCollectedItem.Text}");
        }

        private static void TwitterStream_LimitReached(object sender, Tweetinvi.Events.LimitReachedEventArgs e)
        {
            System.Console.WriteLine($"TwitterStream_LimitReached: {e.NumberOfTweetsNotReceived.ToString()}");
        }

        private static void TwitterStream_WarningFallingBehindDetected(object sender, Tweetinvi.Events.WarningFallingBehindEventArgs e)
        {
            System.Console.WriteLine($"TwitterStream_WarningFallingBehindDetected: {e.WarningMessage.ToString()}");
        }

        [FunctionName("StartSensorInstance")]
        public static async Task StartSensor(
            [ServiceBusTrigger("start-sensors-queue", Connection = "AzureServiceBus")] string message,
            [CosmosDB(databaseName: "sensors", collectionName: "pipelines", ConnectionStringSetting = "CosmosDb")] IAsyncCollector<Sensor> sensors,
            [CosmosDB(databaseName: "settings", collectionName: "tenants", ConnectionStringSetting = "CosmosDb")] Microsoft.Azure.Documents.IDocumentClient documentClient,
            [ServiceBus("start-sensors-queue", Connection = "AzureServiceBus", EntityType = Microsoft.Azure.WebJobs.ServiceBus.EntityType.Queue)] IAsyncCollector<Message> scheduledSensors,
            [DurableClient] IDurableOrchestrationClient client,
            ILogger log)
        {
            var sensor = JsonConvert.DeserializeObject<Sensor>(message);
            DurableOrchestrationStatus orchestrationStatus = null;

            if (!String.IsNullOrEmpty(sensor.TenantId))
            {
                orchestrationStatus = await client.GetStatusAsync(sensor.TenantId);
            }

            if (orchestrationStatus == null ||
                orchestrationStatus.RuntimeStatus == OrchestrationRuntimeStatus.Canceled ||
                orchestrationStatus.RuntimeStatus == OrchestrationRuntimeStatus.Completed ||
                orchestrationStatus.RuntimeStatus == OrchestrationRuntimeStatus.Failed ||
                orchestrationStatus.RuntimeStatus == OrchestrationRuntimeStatus.Terminated)
            {
                //var documentResponse = await documentClient.ReadDocumentAsync<TenantSettings>(
                //UriFactory.CreateDocumentUri("settings", "tenants", sensor.TenantId),
                //new RequestOptions { PartitionKey = new PartitionKey("43171124-3995-4924-83b2-f674b109306e") });

                //if (documentResponse != null && documentResponse.Document != null)
                //{
                //    string instanceId = sensor.TenantId;
                //    Tuple<TenantSettings, Sensor> sensorBootstrap = new Tuple<TenantSettings, Sensor>(documentResponse.Document, sensor);

                //    await client.StartNewAsync<Tuple<TenantSettings, Sensor>>("SensorsOrchestrator", instanceId, sensorBootstrap);
                //    await sensors.AddAsync(sensor);
                //}

                string instanceId = sensor.TenantId;
                Tuple<TenantSettings, Sensor> sensorBootstrap = new Tuple<TenantSettings, Sensor>(TenantSettings.GetDevelopmentSettings(), sensor);

                await client.StartNewAsync<Tuple<TenantSettings, Sensor>>("SensorsOrchestrator", instanceId, sensorBootstrap);
                await sensors.AddAsync(sensor);
            }
            else
            {
                Message scheduledSensor = new Message()
                {
                    Body = Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(sensor).ToString()),
                    ScheduledEnqueueTimeUtc = DateTime.UtcNow.AddMinutes(Constants.ScheduledEnqueuedDelayTimeInMinutes),
                    ContentType = "application/json"
                };

                await scheduledSensors.AddAsync(scheduledSensor);
            }

            log.LogInformation($"Started orchestration '{sensor.TenantId}' for tenant '{sensor.TenantId}'.");
        }

        [FunctionName("TerminateSensorInstance")]
        public static async Task TerminateSensor(
            [ServiceBusTrigger("terminate-sensors-queue", Connection = "AzureServiceBus")] string stopSensorRequest,
            [DurableClient] IDurableOrchestrationClient client,
            ILogger log)
        {
            var sensor = JsonConvert.DeserializeObject<Sensor>(stopSensorRequest);
            await client.TerminateAsync(sensor.TenantId, "user_termination");

            log.LogInformation($"Terminated orchestration '{sensor.TenantId}' for tenant '{sensor.TenantId}'.");
        }

        [FunctionName("GetSensorStatus")]
        public static async Task<IActionResult> GetSensorStatus(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "Tenants/{tenantId}/Sensors/Instances/{instanceId}/Status")] HttpRequestMessage httpRequestMessage,
            [DurableClient] IDurableOrchestrationClient client,
            string tenantId,
            string instanceId,
            ILogger log)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                return new BadRequestResult();
            }
            else
            {
                DurableOrchestrationStatus orchestrationStatus = await client.GetStatusAsync(instanceId);

                if (orchestrationStatus != null)
                {
                    return new OkObjectResult(orchestrationStatus.RuntimeStatus.ToString());
                }
                else
                {
                    return new NotFoundResult();
                }
            }
        }

        [FunctionName("ScheduleSensor")]
        public static async Task<IActionResult> ScheduleSensor(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Tenants/{tenantId}/Sensors/Schedule")] HttpRequestMessage httpRequestMessage,
            [DurableClient] IDurableOrchestrationClient client,
            [ServiceBus("start-sensors-queue", Connection = "AzureServiceBus", EntityType = Microsoft.Azure.WebJobs.ServiceBus.EntityType.Queue)] IAsyncCollector<Message> scheduledSensors,
            string tenantId,
            ILogger log)
        {
            if (string.IsNullOrEmpty(tenantId))
            {
                return new BadRequestObjectResult($"{tenantId} is required");
            }

            Sensor sensor = await httpRequestMessage.Content.ReadAsAsync<Sensor>();

            if (sensor == null || sensor.TenantId != tenantId)
            {
                return new BadRequestObjectResult("Tenant Ids do not match");
            }
            else
            {
                uint scheduledEnqueuedWaitTime = sensor.ScheduledEnqueuedWaitTime.HasValue ? sensor.ScheduledEnqueuedWaitTime.Value : Constants.ScheduledEnqueuedTimeInMinutes;

                Message scheduledSensor = new Message()
                {
                    Body = Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(sensor).ToString()),
                    ScheduledEnqueueTimeUtc = DateTime.UtcNow.AddMinutes(scheduledEnqueuedWaitTime),
                    ContentType = "application/json"
                };

                await scheduledSensors.AddAsync(scheduledSensor);

                return new OkResult();
            }
        }

        [FunctionName("GetSensors")]
        public static async Task<IActionResult> GetSensors(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "Tenants/{tenantId}/Sensors/")] HttpRequestMessage httpRequestMessage,
            [CosmosDB(databaseName: "sensors", collectionName: "pipelines", ConnectionStringSetting = "CosmosDb", SqlQuery = "SELECT * FROM c WHERE c.tenantId={tenantId} ORDER BY c._ts DESC")] IEnumerable<Sensor> sensors,
            [DurableClient] IDurableOrchestrationClient client,
            string tenantId,
            ILogger log)
        {
            if (string.IsNullOrEmpty(tenantId))
            {
                return new BadRequestResult();
            }
            else
            {
                if (sensors != null)
                {
                    return new OkObjectResult(sensors);
                }
                else
                {
                    return new NotFoundResult();
                }
            }
        }

        [FunctionName("DeleteSensor")]
        public static async Task<IActionResult> DeleteSensor(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "Tenants/{tenantId}/Sensors/{id}")] HttpRequestMessage httpRequestMessage,
            [CosmosDB(databaseName: "sensors", collectionName: "pipelines", ConnectionStringSetting = "CosmosDb", Id = "{id}", PartitionKey = "{tenantId}")] Microsoft.Azure.Documents.Document sensor,
            [CosmosDB(databaseName: "sensors", collectionName: "pipelines", ConnectionStringSetting = "CosmosDb")] Microsoft.Azure.Documents.IDocumentClient documentClient,
            [DurableClient] IDurableOrchestrationClient durableOrchestrationClient,
            string tenantId,
            string id,
            ILogger log)
        {
            if (sensor == null || String.IsNullOrEmpty(id) || String.IsNullOrEmpty(tenantId))
            {
                return new BadRequestResult();
            }

            await documentClient.DeleteDocumentAsync(sensor.SelfLink, new Microsoft.Azure.Documents.Client.RequestOptions() { PartitionKey = new Microsoft.Azure.Documents.PartitionKey(tenantId) });

            return new OkResult();
        }

        //[FunctionName("SensorInstancesManager_HttpStart")]
        //public static async Task<HttpResponseMessage> HttpStart(
        //    [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "Sensors/Start")] HttpRequestMessage req,
        //    [DurableClient] IDurableOrchestrationClient starter,
        //    ILogger log)
        //{
        //    // Function input comes from the request content.
        //    string instanceId = await starter.StartNewAsync("SensorInstancesManager", null);

        //    log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

        //    return starter.CreateCheckStatusResponse(req, instanceId);
        //}
    }
}