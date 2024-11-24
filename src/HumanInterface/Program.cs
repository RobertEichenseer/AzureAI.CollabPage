﻿using System; 
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using FTA.AI.Agents.CollabPage.AgentTooling;
using DotNetEnv;
using Azure.Messaging.ServiceBus;
using FTA.AI.Agents.CollabPage.BehaviorContract;

IHost consoleHost = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services => {
        services.AddTransient<Main>();
    })
    .Build();

Main main = consoleHost.Services.GetRequiredService<Main>();
await main.ExecuteAsync(args);

class Main
{
    string _configurationFile = "../../configuration/application.env";
    Configuration _configuration; 
    StorageTooling _storageTooling;
    string _instanceId = Guid.NewGuid().ToString();

    public Main()
    {

        Env.Load(_configurationFile);

        //Configuration
        Configuration configuration = new Configuration(){
            AOAIApiKey = Env.GetString("OA_AOAI_APIKEY"),
            AOAIEndPoint = Env.GetString("OA_AOAI_ENDPOINT"),
            AOAIChatCompletionDeploymentName = Env.GetString("OA_CHATCOMPLETION_DEPLOYMENTNAME"),
            StorageConnectionString = Env.GetString("OA_STORAGE_CONNECTIONSTRING"),
            StorageCollabContainer = Env.GetString("OA_STORAGE_COLLABPAGECONTAINER"),
            ServiceBusConnectionString = Env.GetString("OA_SERVICEBUS_CONNECTIONSTRING"),
            ServiceBusTopicName = Env.GetString("OA_SERVICEBUS_TOPIC"),
            ServiceBusSubscriptionName = Env.GetString("OA_SERVICEBUS_SUBSCRIPTION"),
            ServiceBusSubscriptionHumanName = Env.GetString("OA_SERVICEBUS_SUBSCRIPTIONHUMAN"),
            AgentResponseFormat = Env.GetString("OA_AGENT_RESPONSEFORMAT")
        };

        _configuration = configuration;
        _storageTooling = new StorageTooling(_configuration);

    }

    public async Task<int> ExecuteAsync(string[] args)
    {
        await PerformAgentCalls(); 
        await StartAgentProcessing();
        await StartEventListening();
        await ShowProcessingJournal();

        return -1;
    }

    private async Task PerformAgentCalls()
    {
        Console.WriteLine("#############################################");
        Console.WriteLine("### Direct Agent Calls");
        Console.WriteLine("#############################################");

        // Read input data
        string inputDataFile = "../../assets/CallContent_Caller.txt";
        string content = await File.ReadAllTextAsync(inputDataFile);

        //Connect to Actor Host
        using var host = new HostBuilder()
            .UseOrleansClient(clientBuilder => {
                clientBuilder.UseLocalhostClustering();
            })
            .Build();
        await host.StartAsync(); 

        //Get Agent Instances
        string agentInstance = Guid.NewGuid().ToString();
        
        IClusterClient clusterClient = host.Services.GetRequiredService<IClusterClient>();
        ISentimentDetectionAgent sentimentDetectionAgent = clusterClient.GetGrain<ISentimentDetectionAgent>(agentInstance);
        ILanguageDetectionAgent languageDetectionAgent = clusterClient.GetGrain<ILanguageDetectionAgent>(agentInstance);
        ISummarizerAgent summarizerAgent = clusterClient.GetGrain<ISummarizerAgent>(agentInstance);

        string sentiment = await sentimentDetectionAgent.DetectSentiment(content);
        Console.WriteLine($"Sentiment: {sentiment}");
        string language = await languageDetectionAgent.DetectLanguage(content);
        Console.WriteLine($"Language: {language}");
        string result = await summarizerAgent.CreateSummary(String.Concat(language, " ", sentiment));
        Console.WriteLine($"Result: {result}");
    }


    private async Task StartAgentProcessing()
    {
        Console.WriteLine("#############################################");
        Console.WriteLine("### Storage as Collaboration Page");
        Console.WriteLine("#############################################");
        Console.WriteLine("Putting input for agent processing...");
        string fileName = "../../assets/CallContent_Caller.txt"; 
        _instanceId = Guid.NewGuid().ToString();
        string content = await File.ReadAllTextAsync(fileName);

        CollabPageFileMetaData collabPageFileMetaData = new CollabPageFileMetaData(){
            ExpectedProcessingOutput = "JSON with detected language and sentiment"
        };
        await _storageTooling.PutInput("CallContent_Caller.txt", _instanceId, content, collabPageFileMetaData);
        
    }

    private async Task StartEventListening()
    {
        Console.WriteLine("Start listening for results...");
        
        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        CancellationToken cancellationToken = cancellationTokenSource.Token;

        await using ServiceBusClient serviceBusClient = new ServiceBusClient(_configuration.ServiceBusConnectionString);
        ServiceBusProcessorOptions serviceBusProcessorOptions = new ServiceBusProcessorOptions(){
            ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete 
        };

        ServiceBusProcessor serviceBusProcessor = serviceBusClient.CreateProcessor(
            _configuration.ServiceBusTopicName, 
            _configuration.ServiceBusSubscriptionHumanName,
            serviceBusProcessorOptions
        );
        serviceBusProcessor.ProcessMessageAsync += TopicMessageHandler;
        serviceBusProcessor.ProcessErrorAsync += TopicErrorHandler;
        await serviceBusProcessor.StartProcessingAsync(cancellationToken);

        Console.WriteLine("Press any key to stop listening to events and to show process summary...");        
        while (!cancellationToken.IsCancellationRequested)
        {
            if (Console.KeyAvailable)
            {
                Console.ReadKey(true);
                break;
            }
            await Task.Delay(1000, cancellationToken);
        }

        await serviceBusProcessor.StopProcessingAsync(cancellationToken);
        

    }

    private async Task TopicErrorHandler(ProcessErrorEventArgs args)
    {
        await Task.Run( () => 
            Console.WriteLine(args.Exception.ToString())
        );
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

                if (collabPageEvent.InputCreated) {
                    collabPageEvent = await AddCustomMetaInfo(collabPageEvent);
                    await ShowAgentResponse(collabPageEvent);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex.Message}");
        }
    }

    private async Task ShowProcessingJournal()
    {
        Console.WriteLine("#############################################");
        Console.WriteLine("### Processing Journal");
        Console.WriteLine("#############################################");
        
        List<string> journal = await _storageTooling.GetProcessingJournal(_instanceId);
        Console.WriteLine($"* Instance Journal: {_instanceId}");
        foreach(string journalEntry in journal){
            Console.WriteLine(journalEntry);
        }
    }

    private async Task ShowAgentResponse(CollabPageEvent collabPageEvent)
    {
        if (collabPageEvent.IsAgentCreated
            && 
            collabPageEvent.AgentName == "SummarizerAgentGrain"
        ){
            string agentResponse = await _storageTooling.GetInputContent(
                collabPageEvent.InputFileName, 
                collabPageEvent.InstanceId
            ); 
            Console.WriteLine($"* Instance Response: {collabPageEvent.InstanceId}");
            Console.WriteLine(agentResponse);
        }
    }

    private async Task<CollabPageEvent> AddCustomMetaInfo(CollabPageEvent collabPageEvent)
    {
        // Get message meta data
        CollabPageFileMetaData collabPageFileMetaData = 
            await _storageTooling.GetMetadata(collabPageEvent.InputFileName, collabPageEvent.InstanceId);
        
        // Add additional file meta data to the event
        collabPageEvent.AgentName = collabPageFileMetaData.AgentName;
        collabPageEvent.IsAgentCreated = collabPageFileMetaData.IsAgentCreated;
        collabPageEvent.InputFiles = collabPageFileMetaData.InputFiles;
        collabPageEvent.AdditionalOutputFiles = collabPageFileMetaData.AdditionalOutputFiles;
        
        return collabPageEvent;
    }
}