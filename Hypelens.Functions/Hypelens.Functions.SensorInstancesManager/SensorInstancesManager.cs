using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Hypelens.Functions.SensorInstancesManager
{
    public static class SensorInstancesManager
    {
        [FunctionName("SensorPipeline")]
        public static async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var outputs = new List<string>();

            string tenantId = context.GetInput<string>();

            // Replace "hello" with the name of your Durable Activity Function.
            outputs.Add(await context.CallActivityAsync<string>("SensorInstancesManager_Hello", $"Tokyo-{tenantId}"));
            outputs.Add(await context.CallActivityAsync<string>("SensorInstancesManager_Hello", $"Seattle-{tenantId}"));
            outputs.Add(await context.CallActivityAsync<string>("SensorInstancesManager_Hello", $"London-{tenantId}"));

            // returns ["Hello Tokyo!", "Hello Seattle!", "Hello London!"]
            return outputs;
        }

        [FunctionName("SensorInstancesManager_Hello")]
        public static string SayHello([ActivityTrigger] string name, ILogger log)
        {
            log.LogInformation($"Saying hello to {name}.");
            return $"Hello {name}!";
        }

        [FunctionName("StartSensorInstance")]
        public static async Task StartSensor(
            [ServiceBusTrigger("start-sensors-queue", Connection = "AzureServiceBus")] string inMessage,
            [DurableClient] IDurableOrchestrationClient client,
            ILogger log)
        {
            dynamic sensorRequest = JsonConvert.DeserializeObject(inMessage);
            string tenantId = sensorRequest?.tenantId;

            string instanceId = await client.StartNewAsync<string>("SensorPipeline", tenantId);
            log.LogInformation($"Started orchestration '{instanceId}' for tenant '{tenantId}'.");
        }

        [FunctionName("TerminateSensorInstance")]
        public static void TerminateSensor(
            [ServiceBusTrigger("terminate-sensors-queue", Connection = "AzureServiceBus")] string queueItem,
            [DurableClient] IDurableOrchestrationClient client,
            ILogger log)
        {
            log.LogInformation($"C# Queue trigger function processed: {queueItem}");
        }

        [FunctionName("QuerySensor")]
        public static async Task<IActionResult> QuerySensor(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "Sensors/Status/{instanceId}")] HttpRequestMessage httpRequestMessage,
            [DurableClient] IDurableOrchestrationClient client,
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