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
            [EventHub("sensors-collected-items", Connection = "EventHub")] IAsyncCollector<SensorCollectedItem> outputEvents,
            ILogger log)
        {
            var settings = sensorBootstrap.Item1;
            var sensor = sensorBootstrap.Item2;

            log.LogInformation($"Saying hello from sensor {sensor.TenantId}.");

            var userClient = new TwitterClient(settings.ConsumerKey, settings.ConsumerSecret, settings.AccessToken, settings.AccessSecret);

            var twitterStream = userClient.Streams.CreateFilteredStream();
            twitterStream.TweetMode = TweetMode.Extended;
            twitterStream.StallWarnings = false;

            //twitterStream.AddLanguageFilter(LanguageFilter.German);
            twitterStream.AddLanguageFilter(LanguageFilter.English);

            sensor.Hashtags.ToList().ForEach(hashtag =>
            {
                twitterStream.AddTrack($"{hashtag}");
            });

            int sampleStreamIdx = 0;

            twitterStream.MatchingTweetReceived += async (sender, args) =>
            {
                if (sampleStreamIdx < 100)
                {
                    try
                    {
                        System.Console.WriteLine($"{args.Tweet.Id.ToString()}\t{args.Tweet.Text}");
                        sampleStreamIdx++;

                        if (sensor.Process)
                        {
                            SensorCollectedItem sensorCollectedItem = new SensorCollectedItem()
                            {
                                SensorId = sensor.Id,
                                TenantId = sensor.TenantId,
                                Text = args.Tweet.Text,
                                Url = args.Tweet.Url

                            };

                            if (args.Tweet.Hashtags != null || args.Tweet.Hashtags.Count > 0)
                            {
                                List<string> hashtags = new List<string>();

                                args.Tweet.Hashtags.ForEach(hashtag =>
                                {
                                    hashtags.Add(hashtag.Text);
                                });

                                sensorCollectedItem.Hashtags = hashtags.ToArray();
                            }

                            if (args.Tweet.UserMentions != null || args.Tweet.UserMentions.Count > 0)
                            {
                                List<string> userMentions = new List<string>();

                                args.Tweet.UserMentions.ForEach(userMention =>
                                {
                                    userMentions.Add(userMention.Name);
                                });

                                sensorCollectedItem.Mentions = userMentions.ToArray();
                            }

                            await outputEvents.AddAsync(sensorCollectedItem);
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
                    twitterStream.Stop();
                    twitterStream = null;
                }
            };

            twitterStream.NonMatchingTweetReceived += (sender, args) =>
            {
                System.Console.WriteLine($"NOT MATCHING: {args.Tweet.Text}");
            };

            twitterStream.WarningFallingBehindDetected += TwitterStream_WarningFallingBehindDetected;
            twitterStream.LimitReached += TwitterStream_LimitReached;

            try
            {
                await twitterStream.StartMatchingAnyConditionAsync();
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
                System.Console.WriteLine("##### FAILED #####");
            }
            finally
            {
                if (twitterStream != null)
                {
                    twitterStream.Stop();
                    twitterStream = null;
                }
            }

            return $"Hello {sensor.TenantId}!";
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
                var documentResponse = await documentClient.ReadDocumentAsync<TenantSettings>(
                UriFactory.CreateDocumentUri("settings", "tenants", sensor.TenantId),
                new RequestOptions { PartitionKey = new PartitionKey("43171124-3995-4924-83b2-f674b109306e") });

                if (documentResponse != null && documentResponse.Document != null)
                {
                    string instanceId = sensor.TenantId;
                    Tuple<TenantSettings, Sensor> sensorBootstrap = new Tuple<TenantSettings, Sensor>(documentResponse.Document, sensor);

                    await client.StartNewAsync<Tuple<TenantSettings, Sensor>>("SensorsOrchestrator", instanceId, sensorBootstrap);
                    await sensors.AddAsync(sensor);
                }
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