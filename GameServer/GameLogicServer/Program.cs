using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client;
using Orleans.Configuration;
using Orleans.Providers;

await Host.CreateDefaultBuilder(args)
    .UseOrleans((context, siloBuilder) =>
    {
        var orleansConfig = context.Configuration.GetSection("Application");
        siloBuilder.Configure<ClusterOptions>(orleansConfig);
        
        // Clustering
        var clusteringConfig = orleansConfig.GetSection("Clustering");
        var clusteringProvider = clusteringConfig.GetValue<string>("Provider"); 
        switch (clusteringProvider)
        {
            case "Localhost":
                siloBuilder.UseLocalhostClustering();
                break;
            case "Kubernetes":
                siloBuilder.UseKubernetesHosting();
                break;
            default:
                throw new Exception($"Unknown provider : {clusteringProvider}");
        }

        // Persistence
        var persistenceConfig = orleansConfig.GetSection("Persistence");
        var persistenceProvider = persistenceConfig.GetValue<string>("Provider");
        switch (persistenceProvider)
        {
            case "DynamoDB":
                siloBuilder.AddDynamoDBGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, options =>
                {
                    var ddbSection = persistenceConfig.GetSection("DynamoDB");
                    options.TableName = ddbSection.GetValue<string>("TableName");
                    options.Service = ddbSection.GetValue<string>("Service");
                    options.ServiceId = ddbSection.GetValue<string>("ServiceId");
                })
                .UseTransactions();
                break;
            case "Memory":
                siloBuilder.AddMemoryGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME);
                break;
            default:
                throw new Exception("Unknown persistence provider");
        }
    })
    .ConfigureServices(services =>
    {
        // NATS Connection을 싱글톤으로 등록
        services.AddSingleton<IConnection>(sp =>
        {
            var factory = new ConnectionFactory();
            // NATS 서버 주소. 설정 파일에서 가져오는 것을 권장.
            var connection = factory.CreateConnection("nats://localhost:4222"); 
            return connection;
        });
    })
    .ConfigureLogging(logging =>
    {
        logging.AddConsole();
    })
    .RunConsoleAsync();