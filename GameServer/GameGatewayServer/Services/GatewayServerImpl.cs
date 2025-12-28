using GameProtocol;
using Grpc.Core;

namespace GameGatewayServer.Services;

public class GatewayServerImpl : GatewayServer.GatewayServerBase
{
    public override Task<PublishResponse> Publish(IAsyncStreamReader<PublishRequest> requestStream, ServerCallContext context)
    {
        return base.Publish(requestStream, context);
    }

    public override Task Subscribe(SubscribeRequest request, IServerStreamWriter<SubscribeResponse> responseStream, ServerCallContext context)
    {
        return base.Subscribe(request, responseStream, context);
    }
}