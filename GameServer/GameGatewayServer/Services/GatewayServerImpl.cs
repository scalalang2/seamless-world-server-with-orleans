using GameProtocol;
using Grpc.Core;

namespace GameGatewayServer.Services;

public class GatewayServerImpl : GatewayServer.GatewayServerBase
{
    public override Task<LoginResponse> Login(LoginRequest request, ServerCallContext context)
    {
        return base.Login(request, context);
    }

    public override Task<LogoutResponse> Logout(LogoutRequest request, ServerCallContext context)
    {
        return base.Logout(request, context);
    }

    public override Task<PublishResponse> Publish(IAsyncStreamReader<PublishRequest> requestStream, ServerCallContext context)
    {
        return base.Publish(requestStream, context);
    }

    public override Task Subscribe(SubscribeRequest request, IServerStreamWriter<SubscribeResponse> responseStream, ServerCallContext context)
    {
        return base.Subscribe(request, responseStream, context);
    }
}