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
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger log)
        {
            var sensorBootstrap = context.GetInput<Tuple<TenantSettings, Sensor>>();
            var outputs = new List<string>();

            await context.CallActivityAsync<string>("RescheduleSensor", sensorBootstrap);

            TimeSpan timeout = TimeSpan.FromSeconds((Constants.SensorTimeoutInMinutes + 0.5) * 60);
            DateTime deadline = context.CurrentUtcDateTime.Add(timeout);

            using (var cancellationTokenSource = new CancellationTokenSource())
            {
                Task consumerSensorTask = context.CallActivityAsync<string>("ConsumeSensor", sensorBootstrap);
                Task timeoutTask = context.CreateTimer(deadline, cancellationTokenSource.Token);

                Task winner = await Task.WhenAny(consumerSensorTask, timeoutTask);

                if (winner == consumerSensorTask)
                {
                    log.LogInformation($"Orchestration completed for Sensor {sensorBootstrap.Item2.TenantId}.");
                    cancellationTokenSource.Cancel();
                }
                else
                {
                    log.LogInformation($"Orchestration timed out for Sensor {sensorBootstrap.Item2.TenantId}.");
                }
            }

            return outputs;
        }

        [FunctionName("RescheduleSensor")]
        public static async Task RescheduleSensorAsync(
            [ActivityTrigger] Tuple<TenantSettings, Sensor> sensorBootstrap,
            [CosmosDB(databaseName: "sensors", collectionName: "pipelines", ConnectionStringSetting = "CosmosDb")] Microsoft.Azure.Documents.IDocumentClient documentClient,
            [ServiceBus("start-sensors-queue", Connection = "AzureServiceBus", EntityType = Microsoft.Azure.WebJobs.ServiceBus.EntityType.Queue)] IAsyncCollector<Message> scheduledSensors,
            ILogger log)
        {
            var sensor = sensorBootstrap.Item2;

            var documentResponse = await documentClient.ReadDocumentAsync<Sensor>(
            UriFactory.CreateDocumentUri("sensors", "pipelines", sensor.Id),
            new RequestOptions { PartitionKey = new PartitionKey(sensor.TenantId) });

            if (documentResponse != null && documentResponse.Document != null && documentResponse.Document.Active == true)
            {
                DateTime scheduledEnqueueTimeUtc = DateTime.UtcNow.AddMinutes(Constants.SensorTimeoutInMinutes + 1);

                Message scheduledSensor = new Message()
                {
                    Body = Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(sensor).ToString()),
                    ScheduledEnqueueTimeUtc = scheduledEnqueueTimeUtc,
                    ContentType = "application/json"
                };

                await scheduledSensors.AddAsync(scheduledSensor);
            }
        }

        [FunctionName("ConsumeSensor")]
        public static async Task<string> ConsumeSensorAsync(
            [ActivityTrigger] Tuple<TenantSettings, Sensor> sensorBootstrap,
            [CosmosDB(databaseName: "sensors", collectionName: "pipelines", ConnectionStringSetting = "CosmosDb")] Microsoft.Azure.Documents.IDocumentClient documentClient,
            [EventHub("sensors-collected-items", Connection = "EventHub")] IAsyncCollector<SensorCollectedTweet> outputEvents,
            ILogger log)
        {
            var settings = sensorBootstrap.Item1;
            var sensor = sensorBootstrap.Item2;

            int matchingTweetsQuota = 500;
            int matchingTweetsIndex = 0;
            int buffedtoEventHubIdx = 0;

            TimeSpan timeout = new TimeSpan(0, Constants.SensorTimeoutInMinutes, 0);
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(timeout);
            Dictionary<string, SensorCollectedTweet> sensorCollectedItems = new Dictionary<string, SensorCollectedTweet>();

            var semaphore = new SemaphoreSlim(1);

            try
            {
                var documentResponse = await documentClient.ReadDocumentAsync<Sensor>(
                UriFactory.CreateDocumentUri("sensors", "pipelines", sensor.Id),
                new RequestOptions { PartitionKey = new PartitionKey(sensor.TenantId) });

                if (documentResponse != null && documentResponse.Document != null && documentResponse.Document.Active == true)
                {
                    log.LogInformation($"Start consuming tweets from Sensor {sensor.TenantId}.");

                    var userClient = new TwitterClient(settings.ConsumerKey, settings.ConsumerSecret, settings.AccessToken, settings.AccessSecret);

                    var filteredStream = userClient.Streams.CreateFilteredStream();
                    filteredStream.TweetMode = TweetMode.Extended;
                    filteredStream.StallWarnings = false;

                    filteredStream.AddTrack(sensor);
                    filteredStream.AddFollow(userClient, sensor);
                    filteredStream.AddLanguageFilter(sensor);

                    var cancellationToken = cancellationTokenSource.Token;

                    filteredStream.MatchingTweetReceived += async (sender, args) =>
                    {
                        if (matchingTweetsIndex < matchingTweetsQuota)
                        {
                            if (!cancellationToken.IsCancellationRequested)
                            {
                                try
                                {
                                    bool process = sensor.Process;
                                    bool added = false;

                                    SensorCollectedTweet tweet = new SensorCollectedTweet(sensor, args.Tweet);

                                    if (args.Tweet.QuotedTweet != null)
                                    {
                                        SensorCollectedTweet quotedTweet = new SensorCollectedTweet(sensor, args.Tweet.QuotedTweet);
                                        tweet.OriginalTweetId = quotedTweet.TweetId;
                                        added = sensorCollectedItems.TryAdd(quotedTweet.TweetId, quotedTweet);

                                        if (added)
                                        {
                                            Interlocked.Increment(ref matchingTweetsIndex);

                                            WriteCollectedItemToConsole(quotedTweet, matchingTweetsIndex);

                                            await outputEvents.AddAsync(quotedTweet, matchingTweetsQuota, matchingTweetsIndex, process);
                                            Interlocked.Increment(ref buffedtoEventHubIdx);
                                        }
                                    }
                                    else if (args.Tweet.RetweetedTweet != null && args.Tweet.QuotedTweet == null)
                                    {
                                        SensorCollectedTweet retweetedTweet = new SensorCollectedTweet(sensor, args.Tweet.RetweetedTweet);
                                        tweet.OriginalTweetId = retweetedTweet.TweetId;
                                        added = sensorCollectedItems.TryAdd(retweetedTweet.TweetId, retweetedTweet);

                                        if (added)
                                        {
                                            Interlocked.Increment(ref matchingTweetsIndex);

                                            WriteCollectedItemToConsole(retweetedTweet, matchingTweetsIndex);

                                            await outputEvents.AddAsync(retweetedTweet, matchingTweetsQuota, matchingTweetsIndex, process);
                                            Interlocked.Increment(ref buffedtoEventHubIdx);
                                        }
                                    }

                                    added = sensorCollectedItems.TryAdd(tweet.TweetId, tweet);

                                    if (added)
                                    {
                                        Interlocked.Increment(ref matchingTweetsIndex);

                                        WriteCollectedItemToConsole(tweet, matchingTweetsIndex);

                                        await outputEvents.AddAsync(tweet, matchingTweetsQuota, matchingTweetsIndex, process);
                                        Interlocked.Increment(ref buffedtoEventHubIdx);

                                        await semaphore.WaitAsync();

                                        if (buffedtoEventHubIdx >= 32)
                                        {
                                            using (var flushCancellationTokenSource = new CancellationTokenSource(new TimeSpan(0, 0, 15)))
                                            {
                                                await outputEvents.FlushAsync(flushCancellationTokenSource.Token);
                                                Interlocked.Exchange(ref buffedtoEventHubIdx, 0);

                                                System.Console.WriteLine("##### FLUSH #####");
                                            }
                                        }

                                        semaphore.Release();
                                    }
                                }
                                catch (Exception matchingTweetReceivedException)
                                {
                                    Console.WriteLine(matchingTweetReceivedException.Message);
                                }
                            }
                        }
                        else
                        {
                            System.Console.WriteLine("##### REACHED TWEETS CAP #####");
                            log.LogInformation($"Tweets cap for Sensor {sensor.Id} reached, activity will be terminated.");

                            try
                            {
                                if (cancellationTokenSource != null)
                                {
                                    cancellationTokenSource.Cancel(true);
                                }
                            }
                            catch (Exception)
                            {
                            }

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
                        Task.WaitAny(new Task[] { twitterStartMatchingAnyConditionTask }, cancellationToken);
                    }
                    catch (OperationCanceledException operationCancelledException)
                    {
                        Console.WriteLine(operationCancelledException.Message);
                        System.Console.WriteLine("##### CANCELLED #####");

                        log.LogInformation($"Consuming tweets from Sensor {sensor.TenantId} timed out or cancelled");
                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine(exception.Message);
                        System.Console.WriteLine("##### FAILED #####");

                        log.LogInformation($"Consuming tweets from Sensor {sensor.TenantId} failed.{exception.Message}");
                    }
                    finally
                    {
                        if (cancellationTokenSource != null)
                        {
                            cancellationTokenSource.Dispose();
                            cancellationTokenSource = null;
                        }

                        filteredStream.TryStopFilteredStreamIfNotNull();

                        System.Console.WriteLine("##### FINISHED #####");
                    }

                    log.LogInformation($"Finished consuming tweets from Sensor {sensor.TenantId}.");
                }
            }
            catch (DocumentClientException documentClientException)
            {
                if (documentClientException.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    string abortMessage = $"Aborted consuming tweets from Sensor { sensor.TenantId}. Sensor was not found.";
                    log.LogError(documentClientException, abortMessage);

                    return abortMessage;
                }
            }
            catch (Exception exception)
            {
                log.LogError(exception, $"Consuming tweets for Sensor '{sensor.Id}' for tenant '{sensor.TenantId}' failed.");
            }

            return $"Finished consuming tweets from Sensor {sensor.Id}.";

        }

        private static void WriteCollectedItemToConsole(SensorCollectedTweet sensorCollectedItem, int idx)
        {
            string tweetText = String.Empty;

            if (sensorCollectedItem.Text.Length >= 145)
            {
                tweetText = sensorCollectedItem.Text.Substring(0, 144);
            }
            else
            {
                tweetText = sensorCollectedItem.Text;
            }

            System.Console.WriteLine($"{idx.ToString().PadLeft(5, ' ')}\t{DateTime.Now.ToLongTimeString()}\t{sensorCollectedItem.TweetId}\t{sensorCollectedItem.FavoritesCount.ToString()}\t{sensorCollectedItem.RetweetsCount.ToString()}\t{tweetText}");
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

                try
                {
                    var documentResponse = await documentClient.ReadDocumentAsync<TenantSettings>(
                    UriFactory.CreateDocumentUri("settings", "tenants", sensor.TenantId),
                    new RequestOptions { PartitionKey = new PartitionKey(Constants.AppId) });

                    if (documentResponse != null && documentResponse.Document != null)
                    {
                        string instanceId = sensor.TenantId;

                        Tuple<TenantSettings, Sensor> sensorBootstrap = new Tuple<TenantSettings, Sensor>(documentResponse.Document, sensor);

                        await client.StartNewAsync<Tuple<TenantSettings, Sensor>>("SensorsOrchestrator", instanceId, sensorBootstrap);
                        //await sensors.AddAsync(sensor);
                    }
                }
                catch (DocumentClientException documentClientException)
                {
                    log.LogWarning($"Failed to retrieve information for Tenant '{sensor.TenantId}'.");
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

            log.LogInformation($"Started orchestration for Sensor '{sensor.Id}' for Tenant '{sensor.TenantId}'.");
        }

        [FunctionName("StopSensorInstance")]
        public static async Task StopSensor(
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

        [FunctionName("TerminateSensor")]
        public static async Task<IActionResult> TerminateSensor(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Tenants/{tenantId}/Sensors/{sensorId}/Terminate")] HttpRequestMessage httpRequestMessage,
            [DurableClient] IDurableOrchestrationClient client,
            [CosmosDB(databaseName: "sensors", collectionName: "pipelines", ConnectionStringSetting = "CosmosDb")] Microsoft.Azure.Documents.IDocumentClient documentClient,
            [ServiceBus("terminate-sensors-queue", Connection = "AzureServiceBus", EntityType = Microsoft.Azure.WebJobs.ServiceBus.EntityType.Queue)] IAsyncCollector<Sensor> terminateSensors,
            string sensorId,
            string tenantId,
            ILogger log)
        {
            if (string.IsNullOrEmpty(sensorId))
            {
                return new BadRequestObjectResult($"{sensorId} is required");
            }

            if (string.IsNullOrEmpty(tenantId))
            {
                return new BadRequestObjectResult($"{tenantId} is required");
            }

            try
            {
                var documentResponse = await documentClient.ReadDocumentAsync<Sensor>(
                UriFactory.CreateDocumentUri("sensors", "pipelines", sensorId),
                new RequestOptions { PartitionKey = new PartitionKey(tenantId) });

                if (documentResponse != null && documentResponse.Document != null)
                {
                    if (documentResponse.Document.Active == true)
                    {
                        Sensor sensor = new Sensor()
                        {
                            Id = sensorId,
                            TenantId = tenantId,
                        };

                        await terminateSensors.AddAsync(sensor);
                    }
                }
            }
            catch (DocumentClientException documentClientException)
            {
                log.LogError(documentClientException, $"Sensor '{sensorId}' for tenant '{tenantId}' was not found.");
                return new NotFoundResult();
            }
            catch (Exception exception)
            {
                log.LogError(exception, $"Terminating Sensor '{sensorId}' for tenant '{tenantId}' failed.");
                return new BadRequestObjectResult($"Terminating Sensor '{sensorId}' for tenant '{tenantId}' failed. {exception.Message}");
            }

            return new OkResult();
        }

        [FunctionName("ScheduleSensor")]
        public static async Task<IActionResult> ScheduleSensor(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Tenants/{tenantId}/Sensors/Schedule")] HttpRequestMessage httpRequestMessage,
            [DurableClient] IDurableOrchestrationClient client,
            [CosmosDB(databaseName: "sensors", collectionName: "pipelines", ConnectionStringSetting = "CosmosDb")] Microsoft.Azure.Documents.IDocumentClient documentClient,
            [ServiceBus("start-sensors-queue", Connection = "AzureServiceBus", EntityType = Microsoft.Azure.WebJobs.ServiceBus.EntityType.Queue)] IAsyncCollector<Message> scheduledSensors,
            [CosmosDB(databaseName: "sensors", collectionName: "pipelines", ConnectionStringSetting = "CosmosDb")] IAsyncCollector<Sensor> pipelines,
            string tenantId,
            ILogger log)
        {
            if (string.IsNullOrEmpty(tenantId))
            {
                return new BadRequestObjectResult($"{tenantId} is required");
            }

            Message scheduledSensor = null;
            Sensor sensor = await httpRequestMessage.Content.ReadAsAsync<Sensor>();
            uint scheduledEnqueuedWaitTime =
                        !sensor.ScheduledEnqueuedWaitTime.HasValue || (sensor.ScheduledEnqueuedWaitTime.HasValue && sensor.ScheduledEnqueuedWaitTime.Value <= 0) ?
                        Constants.ScheduledEnqueuedTimeInMinutes :
                        sensor.ScheduledEnqueuedWaitTime.Value;

            DateTime scheduledEnqueueTimeUtc = DateTime.UtcNow.AddMinutes(scheduledEnqueuedWaitTime);

#if DEBUG

            if (scheduledEnqueuedWaitTime == 1)
            {
                scheduledEnqueueTimeUtc = DateTime.UtcNow.AddSeconds(15);
            }

#endif

            if (sensor == null || sensor.TenantId != tenantId)
            {
                return new BadRequestObjectResult("Tenant Ids do not match");
            }
            else
            {
                try
                {
                    var documentResponse = await documentClient.ReadDocumentAsync<Sensor>(
                    UriFactory.CreateDocumentUri("sensors", "pipelines", sensor.Id),
                    new RequestOptions { PartitionKey = new PartitionKey(sensor.TenantId) });

                    if (documentResponse != null && documentResponse.Document != null)
                    {
                        if (documentResponse.Document.Active == false)
                        {
                            sensor.Active = true;

                            scheduledSensor = new Message()
                            {
                                Body = Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(sensor).ToString()),
                                ScheduledEnqueueTimeUtc = scheduledEnqueueTimeUtc,
                                ContentType = "application/json"
                            };
                        }
                    }
                }
                catch (DocumentClientException documentClientException)
                {
                    if (documentClientException.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        sensor.Active = true;

                        scheduledSensor = new Message()
                        {
                            Body = Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(sensor).ToString()),
                            ScheduledEnqueueTimeUtc = scheduledEnqueueTimeUtc,
                            ContentType = "application/json"
                        };
                    }
                }
                catch (Exception exception)
                {
                    log.LogError(exception, $"Scheduling Sensor '{sensor.Id}' for tenant '{sensor.TenantId}' failed.");
                    return new BadRequestObjectResult($"Scheduling Sensor '{sensor.Id}' for tenant '{sensor.TenantId}' failed. {exception.Message}");
                }

                if (scheduledSensor != null)
                {
                    await pipelines.AddAsync(sensor);
                    await scheduledSensors.AddAsync(scheduledSensor);
                }

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