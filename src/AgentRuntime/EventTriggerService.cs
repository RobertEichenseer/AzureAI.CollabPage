using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FTA.AI.Agents.CollabPage.AgentTooling;
using Azure.Messaging.ServiceBus;
using System.Text.Json;  
using System.Reflection;

public class EventTriggerService : BackgroundService
{
    ILogger<EventTriggerService> _logger;

    Configuration _configuration;
    IClusterClient _clusterClient;
    IClusterManifestProvider _clusterManifestProvider;
    public StorageTooling _storageTooling; 
    public EventTriggerService( Configuration configuration,
                                IClusterClient clusterClient,
                                IClusterManifestProvider clusterManifestProvider,
                                ILogger<EventTriggerService> logger 
    )
    {
        _configuration = configuration;
        _clusterClient = clusterClient;
        _clusterManifestProvider = clusterManifestProvider;
        _logger = logger;

        _storageTooling = new StorageTooling(_configuration);
    }
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {

        await using ServiceBusClient serviceBusClient = new ServiceBusClient(_configuration.ServiceBusConnectionString);
        ServiceBusProcessorOptions serviceBusProcessorOptions = new ServiceBusProcessorOptions(){
            ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete //no message confirmation needed to remove msg from subscription
        };

        ServiceBusProcessor serviceBusProcessor = serviceBusClient.CreateProcessor(
            _configuration.ServiceBusTopicName, 
            _configuration.ServiceBusSubscriptionName,
            serviceBusProcessorOptions
        );
        serviceBusProcessor.ProcessMessageAsync += TopicMessageHandler;
        serviceBusProcessor.ProcessErrorAsync += TopicErrorHandler;
        await serviceBusProcessor.StartProcessingAsync(cancellationToken);
        
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(1000, cancellationToken);
        }

        await serviceBusProcessor.StopProcessingAsync(cancellationToken);
    }

    private async Task TopicMessageHandler(ProcessMessageEventArgs args)
    {
        try
        {
            // Handle the message here
            string messageBody = args.Message.Body.ToString();
            CollabPageEvent collabPageEvent = 
                System.Text.Json.JsonSerializer.Deserialize<CollabPageEvent>(messageBody)
                ??
                new CollabPageEvent();

            // Check if message is from the collab container
            if (collabPageEvent.Data.Url.Contains(_configuration.StorageCollabContainer)) {

                // Get message meta data
                CollabPageFileMetaData collabPageFileMetaData = 
                    await _storageTooling.GetMetadata(collabPageEvent.InputFileName, collabPageEvent.InstanceId);
                
                // Add additional file meta data to the event
                collabPageEvent.AgentName = collabPageFileMetaData.AgentName;
                collabPageEvent.IsAgentCreated = collabPageFileMetaData.IsAgentCreated;
                collabPageEvent.InputFiles = collabPageFileMetaData.InputFiles;
                collabPageEvent.AdditionalOutputFiles = collabPageFileMetaData.AdditionalOutputFiles;
                collabPageEvent.ExpectedProcessingOutput = collabPageFileMetaData.ExpectedProcessingOutput;

                await CallAgentEventHandlers(collabPageEvent); 
            }

        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception: {ex.Message}");
        }
    }

    private Task TopicErrorHandler(ProcessErrorEventArgs args)
    {
        _logger.LogError($"Exception: {args.Exception.Message}");
        return Task.CompletedTask;
    }

    private async Task CallAgentEventHandlers(CollabPageEvent collabPageEvent)
    {
        // Check if Orleans cluster is ready (Wait if necessary)
        await CheckOrleansClusterReadiness();

        // File not uploaded to collab container
        if (String.IsNullOrEmpty(collabPageEvent.InstanceId))
            return;

        //Get all Agents (classes attributed with AgentAttribute)
        Type[] agentTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(
                    type => type.GetCustomAttributes(typeof(AgentClass), true).Length > 0
                    &&
                    type.IsClass && !type.IsAbstract
            )
            .ToArray();

        //Call PageEvent on all agents
        foreach(Type agentType in agentTypes) {

            var grainInterface = agentType.GetInterfaces()
                .FirstOrDefault(i => i.GetCustomAttributes(typeof(AgentInterface), true).Length >0 );

            if (grainInterface != null)
            {
                
                try {
                    IGrain grain = _clusterClient.GetGrain(grainInterface, collabPageEvent.InstanceId);
                    List<MethodInfo> methodInfos = grainInterface.GetMethods()
                        .Where(method => method.GetCustomAttributes(typeof(AgentEventHandler), true).Length > 0).ToArray()
                        .ToList<MethodInfo>();

                    foreach(MethodInfo methodInfo in methodInfos) {
                        try {
                            
                            object? result = methodInfo.Invoke(
                                grain, 
                                new object[] {
                                    JsonSerializer.Serialize<CollabPageEvent>(collabPageEvent)
                                }
                            );
                            if (result is Task task) {
                                await task; 
                                object? taskResult = task.GetType().GetProperty("Result")?.GetValue(task);
                            }
                        } catch (Exception e) {
                            _logger.LogError(e.Message);
                        }
                    }
                } catch (Exception e) {
                    _logger.LogError(e.Message);
                }
            }
        }
    }

    private async Task CheckOrleansClusterReadiness()
    {
        int counter = 0; 
        while (counter < 10) {
            try
            {
                counter ++;
                IManagementGrain managementGrain = _clusterClient.GetGrain<IManagementGrain>(0);
                Dictionary<SiloAddress, SiloStatus> clusterHosts = await managementGrain.GetHosts();
                break; 
            }
            catch (NullReferenceException)
            {
                await Task.Delay(1000);
            }
        }
    }
}
