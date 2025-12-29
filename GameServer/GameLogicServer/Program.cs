using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client;
using Orleans.Configuration;
using Orleans.Providers;
using Orleans.Serialization;

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
        
        // Directory
        var grainDirectoryConfig = orleansConfig.GetSection("GrainDirectory");
        var grainDirectoryProvider = grainDirectoryConfig.GetValue<string>("Provider");
        switch (grainDirectoryProvider)
        {
            case "Redis":
                var redisConfig = grainDirectoryConfig.GetSection("Redis");
                var redisEndpoint = redisConfig.GetValue<string>("ConnectionString");
                if (string.IsNullOrEmpty(redisEndpoint))
                {
                    throw new Exception("Redis:ConnectionString is not configured.");
                }
                
                siloBuilder.UseRedisGrainDirectoryAsDefault(options =>
                {
                    options.ConfigurationOptions = new StackExchange.Redis.ConfigurationOptions()
                    {
                        EndPoints = { redisEndpoint }
                    };
                });
                break;
            default:
                throw new Exception($"Unknown grain directory provider: {grainDirectoryProvider}");
        }
        
      
        // add protobuf serializer
        siloBuilder.Services.AddSerializer(serializerBuilder => serializerBuilder.AddProtobufSerializer());
    })
    .ConfigureServices(services =>
    {
        // NATS 커넥션 세팅
        services.AddSingleton<IConnection>(sp =>
        {
            var factory = new ConnectionFactory();
            
            // TODO: Configuration으로 이동
            var connection = factory.CreateConnection("nats://localhost:4222"); 
            return connection;
        });
    })
    .ConfigureLogging(logging =>
    {
        logging.AddConsole();
    })
    .RunConsoleAsync();