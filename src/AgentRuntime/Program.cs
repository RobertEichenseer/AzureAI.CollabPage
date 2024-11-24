using DotNetEnv;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using FTA.AI.Agents.CollabPage.AgentTooling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

string configurationFile = "../../configuration/application.env";
Env.Load(configurationFile);

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

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<Configuration>(configuration);
builder.Services.AddSingleton<StorageTooling>(new StorageTooling(configuration));
builder.Services.AddSingleton<OpenAITooling>(new OpenAITooling(configuration));

builder.Services.AddControllers();
builder.Services.AddHostedService<EventTriggerService>();

builder.Host.UseOrleans(silo =>
{
    silo.UseLocalhostClustering()
        .ConfigureLogging(logging => logging.AddConsole());

    silo.AddAzureBlobGrainStorage(
        name: "agentStore",
        configureOptions: options =>
        {
            options.BlobServiceClient = new BlobServiceClient(configuration.StorageConnectionString);
        });
})
.UseConsoleLifetime();

var app = builder.Build();
await app.RunAsync();
