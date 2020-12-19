using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Hypelens.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Hypelens.Functions.SensorsOrchestrator
{
    public static class SensorsOrchestrator
    {
        [FunctionName("SensorsOrchestrator")]
        public static async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var sensor = context.GetInput<Sensor>();

            var outputs = new List<string>();
            outputs.Add(await context.CallActivityAsync<string>("ActivateSensor", sensor));
            
            return outputs;
        }

        [FunctionName("ActivateSensor")]
        public static string ActivateSensor([ActivityTrigger] Sensor sensor, ILogger log)
        {
            log.LogInformation($"Saying hello from sensor {sensor.Id}.");
            return $"Hello {sensor.TenantId}!";
        }

        [FunctionName("StartSensorInstance")]
        public static async Task StartSensor(
            [ServiceBusTrigger("start-sensors-queue", Connection = "AzureServiceBus")] string startSensorRequest,
            [CosmosDB(databaseName: "sensors", collectionName: "pipelines", ConnectionStringSetting = "CosmosDb")] IAsyncCollector<Sensor> sensors,
            [DurableClient] IDurableOrchestrationClient client,
            ILogger log)
        {
            var sensor = JsonConvert.DeserializeObject<Sensor>(startSensorRequest);

            DurableOrchestrationStatus orchestrationStatus = null;

            if (!String.IsNullOrEmpty(sensor.InstanceId))
            {
                orchestrationStatus = await client.GetStatusAsync(sensor.InstanceId);
            }

            if (orchestrationStatus == null ||
                orchestrationStatus.RuntimeStatus == OrchestrationRuntimeStatus.Canceled ||
                orchestrationStatus.RuntimeStatus == OrchestrationRuntimeStatus.Completed ||
                orchestrationStatus.RuntimeStatus == OrchestrationRuntimeStatus.Failed ||
                orchestrationStatus.RuntimeStatus == OrchestrationRuntimeStatus.Terminated)
            {
            }
            else
            {
                await client.TerminateAsync(sensor.Id, "user_restart");
            }

            string instanceId = await client.StartNewAsync<Sensor>("SensorPipeline", sensor);
            sensor.InstanceId = instanceId;

            await sensors.AddAsync(sensor);

            log.LogInformation($"Started orchestration '{sensor.InstanceId}' for tenant '{sensor.TenantId}'.");
        }

        [FunctionName("TerminateSensorInstance")]
        public static async Task TerminateSensor(
            [ServiceBusTrigger("terminate-sensors-queue", Connection = "AzureServiceBus")] string stopSensorRequest,
            [DurableClient] IDurableOrchestrationClient client,
            ILogger log)
        {
            var sensor = JsonConvert.DeserializeObject<Sensor>(stopSensorRequest);
            await client.TerminateAsync(sensor.InstanceId, "user_termination");

            log.LogInformation($"Terminated orchestration '{sensor.InstanceId}' for tenant '{sensor.TenantId}'.");
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