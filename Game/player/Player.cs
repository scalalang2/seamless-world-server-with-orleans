using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameClient.player;
using GameProtocol;
using GameProtocol.Grains;
using Grpc.Core;
using Grpc.Net.Client;

public partial class Player : CharacterBody3D
{
	// Flag to distinguish between the local player and remote representations.
	public bool IsRemote = false;
	private Transform3D _remoteTargetTransform;

	[Export] public float MouseSensitivity = 1.5f;
	[ExportGroup("Movement")]
	[Export] public float MoveSpeed = 8.0f;
	[Export] public float Acceleration = 20.0f;
	[Export] public float RotationSpeed = 12.0f;

	private Node3D _cameraNode;
	private SophiaSkin _sophiaSkin;
	
	private Vector2 _cameraInputDirection = Vector2.Zero;
	private Vector3 _lastMovementDirection = Vector3.Back;
	private float _gravity = 30.0f;
	
	// Networking
	private string _playerId;
	private GatewayServer.GatewayServerClient _grpcClient;
	private CancellationTokenSource _cancellationTokenSource;
	private AsyncDuplexStreamingCall<ClientConnectionRequest, ServerConnectionResponse> _commsCall;
	private class RemotePlayer
	{
		public Node3D Node { get; set; }
		public double StaleSince { get; set; } = 0; // 0 = not stale
	}
	private readonly Dictionary<string, RemotePlayer> _otherPlayers = new();
	private PackedScene _otherPlayerScene;
	private double _timeSinceLastPublish = 0.0;
	private const double PublishInterval = 0.1; // 100ms

	public override void _Ready()
	{
		this._cameraNode = GetNode<Node3D>("CameraPivot");
		this._sophiaSkin = GetNode<SophiaSkin>("SophiaSkin");

		if (IsRemote)
		{
			_remoteTargetTransform = GlobalTransform;
			if(_cameraNode != null) _cameraNode.QueueFree(); // 다음 프레임에서 카메라 노드 제거
			
			var nameplate = new Label3D
			{
				Text = this.Name,
				Position = new Vector3(0, 2.5f, 0),
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
				FontSize = 80,
				PixelSize = 0.005f,
				Modulate = Colors.Black,
				OutlineModulate = Colors.White,
				OutlineSize = 25
			};
			AddChild(nameplate);
			
			return;
		}
		
		_playerId = Guid.NewGuid().ToString();
		_otherPlayerScene = GD.Load<PackedScene>("res://player.tscn");

		// 서버 연결 시작
		_cancellationTokenSource = new CancellationTokenSource();
		Task.Run(() => ConnectAndStartNetworking(_cancellationTokenSource.Token));
	}
	
	private async Task ConnectAndStartNetworking(CancellationToken cancellationToken)
	{
		try
		{
			var channel = GrpcChannel.ForAddress("http://localhost:5001");
			_grpcClient = new GatewayServer.GatewayServerClient(channel);

			GD.Print($"[{_playerId}] Connecting and logging in...");
			await _grpcClient.LoginAsync(new LoginRequest { PlayerId = _playerId }, cancellationToken: cancellationToken);
			
			GD.Print($"[{_playerId}] Starting communication stream...");
			_commsCall = _grpcClient.Connect(cancellationToken: cancellationToken);
			
			_ = Task.Run(async () =>
			{
				try
				{
                    await foreach (var serverMsg in _commsCall.ResponseStream.ReadAllAsync(cancellationToken))
                    {
                        switch (serverMsg.MessageCase)
                        {
                            case ServerConnectionResponse.MessageOneofCase.WorldUpdate:
                                Callable.From(() => HandleSubscriptionUpdate(serverMsg.WorldUpdate)).CallDeferred();
                                break;
                            case ServerConnectionResponse.MessageOneofCase.PlayerLeft:
                                Callable.From(() => HandlePlayerLeft(serverMsg.PlayerLeft)).CallDeferred();
                                break;
                        }
                    }
				}
				catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
				{
					GD.Print($"[{_playerId}] Subscription stream cancelled.");
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[{_playerId}] Error reading from server stream: {ex.Message}");
				}
			}, cancellationToken);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[{_playerId}] Networking error: {ex.Message}");
		}
	}

	private void HandleSubscriptionUpdate(SubscribeResponse update)
	{
		// 이 객체가 이미 disposed 되었다면 아무것도 하지 않는다.
		if (!GodotObject.IsInstanceValid(this) || GetParent() == null || !GodotObject.IsInstanceValid(GetParent()))
		{
			return;
		}
		
		var receivedPlayerIds = new HashSet<string>();

		foreach (var playerPos in update.PlayerPositionList)
		{
			if (playerPos.PlayerId == _playerId) continue;
			receivedPlayerIds.Add(playerPos.PlayerId);
			
			var isNewPlayer = false;
			Node3D playerNode;
			if (!_otherPlayers.TryGetValue(playerPos.PlayerId, out var remotePlayer))
			{
				isNewPlayer = true;
				
				// 새로운 원격 플레이어 객체를 생성한다.
				var newRemotePlayer = _otherPlayerScene.Instantiate<Player>();
				newRemotePlayer.Name = playerPos.PlayerId;
				newRemotePlayer.IsRemote = true;
				
				// 부모 노드의 자식으로 노드를 추가함
				_otherPlayers[playerPos.PlayerId] = new RemotePlayer { Node = newRemotePlayer };
				GD.Print($"[{_playerId}] Found new player: {playerPos.PlayerId}");
				playerNode = newRemotePlayer;
			}
			else
			{
				// 기존 플레이어의 경우, stale 플래그를 해제한다
				remotePlayer.StaleSince = 0;
				playerNode = remotePlayer.Node;
			}
			
			// 위치 정보 업데이트
			var newTransform = new Transform3D(
				Basis.FromEuler(new Vector3(0, (float)playerPos.Yaw, 0)),
				new Vector3((float)playerPos.X, (float)playerPos.Y, (float)playerPos.Z)
			);
			
			if (isNewPlayer)
			{
				playerNode.GlobalTransform = newTransform;
				GetParent().AddChild(playerNode);
			}
			(playerNode as Player)?.SetRemoteTargetTransform(newTransform);
		}
	}
	
	private void HandlePlayerLeft(PlayerLeft playerLeft)
	{
		if (_otherPlayers.TryGetValue(playerLeft.PlayerId, out var remotePlayer))
		{
			remotePlayer.StaleSince = Time.GetTicksMsec() / 1000.0;
			GD.Print($"[{_playerId}] Player {playerLeft.PlayerId} marked as stale.");
		}
	}

	public override async void _ExitTree()
	{
		if (IsRemote) return;

		_cancellationTokenSource?.Cancel();
		
		if (_commsCall != null)
		{
			await _commsCall.RequestStream.CompleteAsync();
		}

		if (_grpcClient != null)
		{
			try
			{
				_grpcClient.LogoutAsync(new LogoutRequest { PlayerId = _playerId });
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Logout failed: {ex.Message}");
			}
		}
		_cancellationTokenSource?.Dispose();
	}

	public override void _Input(InputEvent @event)
	{
		if (IsRemote) return;
		if (@event.IsActionPressed("left_click")) Input.MouseMode = Input.MouseModeEnum.Captured;
		if (@event.IsActionPressed("ui_cancel")) Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (IsRemote) return;
		if (@event is not InputEventMouseMotion cameraMotion) return;
		if (Input.GetMouseMode() == Input.MouseModeEnum.Captured)
		{
			this._cameraInputDirection = cameraMotion.Relative * this.MouseSensitivity;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (IsRemote)
		{
			GlobalTransform = GlobalTransform.InterpolateWith(_remoteTargetTransform, 0.5f * (float)delta);
			_sophiaSkin.Move();
			return;
		}
		
		if (this._cameraNode == null) return;
		
		this._cameraNode.RotateY(Mathf.DegToRad(-this._cameraInputDirection.X * (float)delta));
		this._cameraInputDirection = Vector2.Zero;

		var rawInput = Input.GetVector("move_right", "move_left", "move_down", "move_up");
		var forward = this._cameraNode.GlobalBasis.Z;
		var right = _cameraNode.GlobalBasis.X;
		
		var moveDirection = (forward * rawInput.Y + right * rawInput.X).Normalized();
		moveDirection.Y = 0.0f;

		var yVelocity = Velocity.Y;
		Velocity = Velocity.MoveToward(moveDirection * this.MoveSpeed, (float)delta * this.Acceleration);
		
		if (!IsOnFloor())
		{
			yVelocity -= (float)delta * _gravity;
		}
		else
		{
			yVelocity = -0.01f;
		}

		Velocity = new Vector3(Velocity.X, yVelocity, Velocity.Z);
		MoveAndSlide();
		
		if(moveDirection.Length() > 0.2f)
		{
			_lastMovementDirection = moveDirection;
		}
		
		// rotate sophia skin by move direction
		var targetRotation = Mathf.Atan2(_lastMovementDirection.X, _lastMovementDirection.Z);
		var currentRotation = Mathf.Atan2(_sophiaSkin.GlobalTransform.Basis.Z.X, _sophiaSkin.GlobalTransform.Basis.Z.Z);
		var rotationDelta = Mathf.Wrap(targetRotation - currentRotation, -Mathf.Pi, Mathf.Pi);
		_sophiaSkin.RotateY(rotationDelta * (float)delta * RotationSpeed);
		
		// move
		_sophiaSkin.RunTilt = Mathf.Lerp(_sophiaSkin.RunTilt, -rawInput.X, (float)delta * 10.0f);
		if (Velocity.Length() > 0.1f)
		{
			_sophiaSkin.Move();
		}
		else
		{
			_sophiaSkin.Idle();
		}

		// Stale player cleanup
		if (!IsRemote)
		{
			const double staleTimeout = 0.5; // 500ms
			var currentTime = Time.GetTicksMsec() / 1000.0;
			var playersToRemove = new List<string>();

			foreach (var (playerId, remotePlayer) in _otherPlayers)
			{
				if (remotePlayer.StaleSince > 0 && (currentTime - remotePlayer.StaleSince > staleTimeout))
				{
					playersToRemove.Add(playerId);
				}
			}

			foreach (var playerId in playersToRemove)
			{
				if (_otherPlayers.TryGetValue(playerId, out var remotePlayer))
				{
					remotePlayer.Node.QueueFree();
					_otherPlayers.Remove(playerId);
					GD.Print($"[{_playerId}] Removed stale player {playerId}.");
				}
			}
		}

		_timeSinceLastPublish += delta;
		if (_commsCall != null && _timeSinceLastPublish >= PublishInterval)
		{
			_timeSinceLastPublish = 0.0;
			var currentTransform = this.GlobalTransform;
			var euler = currentTransform.Basis.GetEuler();

			var positionUpdate = new PlayerPosition
			{
				PlayerId = _playerId,
				X = currentTransform.Origin.X,
				Y = currentTransform.Origin.Y,
				Z = currentTransform.Origin.Z,
				Pitch = euler.X,
				Yaw = euler.Y,
				Roll = euler.Z
			};

			try
			{
				var message = new ClientConnectionRequest() { PositionUpdate = positionUpdate };
				_ = _commsCall.RequestStream.WriteAsync(message);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[{_playerId}] Failed to publish: {ex.Message}");
			}
		}
	}
	
	public void SetRemoteTargetTransform(Transform3D newTransform)
	{
		_remoteTargetTransform = newTransform;
	}
}