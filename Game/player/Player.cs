using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameClient.player;
using GameProtocol;
using Grpc.Core;
using Grpc.Net.Client;

public partial class Player : CharacterBody3D
{
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
	private AsyncClientStreamingCall<PublishRequest, PublishResponse> _publishStream;
	private readonly Dictionary<string, Node3D> _otherPlayers = new();
	private PackedScene _otherPlayerScene;
	private double _timeSinceLastPublish = 0.0;
	private const double PublishInterval = 0.1; // 100ms

	public override void _Ready()
	{
		this._cameraNode = GetNode<Node3D>("CameraPivot");
		this._sophiaSkin = GetNode<SophiaSkin>("SophiaSkin");
		
		_playerId = Guid.NewGuid().ToString(); // Generate a unique ID for this player instance
		_otherPlayerScene = GD.Load<PackedScene>("res://player.tscn");

		// Start networking
		_cancellationTokenSource = new CancellationTokenSource();
		Task.Run(() => ConnectAndStartNetworking(_cancellationTokenSource.Token));
	}
	
	private async Task ConnectAndStartNetworking(CancellationToken cancellationToken)
	{
		try
		{
			// Max OS에서 , allow insecure HTTP/2.
			// AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
			var channel = GrpcChannel.ForAddress("http://localhost:5001");
			_grpcClient = new GatewayServer.GatewayServerClient(channel);

			GD.Print($"[{_playerId}] Connecting and logging in...");
			await _grpcClient.LoginAsync(new LoginRequest { PlayerId = _playerId }, cancellationToken: cancellationToken);
			
			_publishStream = _grpcClient.Publish(cancellationToken: cancellationToken);
			await StartSubscribeStream(cancellationToken);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[{_playerId}] Networking error: {ex.Message}");
		}
	}

	private async Task StartSubscribeStream(CancellationToken cancellationToken)
	{
		GD.Print($"[{_playerId}] Starting to subscribe...");
		using var call = _grpcClient.Subscribe(new SubscribeRequest { PlayerId = _playerId }, cancellationToken: cancellationToken);
		try
		{
			await foreach (var update in call.ResponseStream.ReadAllAsync(cancellationToken))
			{
				GD.Print($"Received update: {update.PlayerPositionList.Count} players");
				Callable.From(() => HandleSubscriptionUpdate(update)).CallDeferred();
			}
		}
		catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
		{
			GD.Print($"[{_playerId}] Subscription stream cancelled.");
		}
	}
	
	private void HandleSubscriptionUpdate(SubscribeResponse update)
	{
		var receivedPlayerIds = new HashSet<string>();
		foreach (var playerPos in update.PlayerPositionList)
		{
			if (playerPos.PlayerId == _playerId) continue;

			receivedPlayerIds.Add(playerPos.PlayerId);

			if (!_otherPlayers.TryGetValue(playerPos.PlayerId, out var playerNode))
			{
				// Instantiate new player, but disable its script to avoid controlling it
				var newPlayerInstance = _otherPlayerScene.Instantiate<Player>();
				newPlayerInstance.Name = playerPos.PlayerId;
				newPlayerInstance.SetScript(new Variant()); // Detach script
				
				GetParent().AddChild(newPlayerInstance); 
				_otherPlayers[playerPos.PlayerId] = newPlayerInstance;
				GD.Print($"[{_playerId}] Found new player: {playerPos.PlayerId}");
				playerNode = newPlayerInstance;
			}

			// Update position and rotation
			var newTransform = new Transform3D(
				Basis.FromEuler(new Vector3((float)playerPos.Pitch, (float)playerPos.Yaw, (float)playerPos.Roll)),
				new Vector3((float)playerPos.X, (float)playerPos.Y, (float)playerPos.Z)
			);
			
			playerNode.GlobalTransform = playerNode.GlobalTransform.InterpolateWith(newTransform, 0.5f);
		}
		
		var playersToRemove = new List<string>();
		foreach (var existingPlayerId in _otherPlayers.Keys)
		{
			if (!receivedPlayerIds.Contains(existingPlayerId))
			{
				playersToRemove.Add(existingPlayerId);
			}
		}

		foreach (var oldPlayerId in playersToRemove)
		{
			if (_otherPlayers.TryGetValue(oldPlayerId, out var nodeToRemove))
			{
				nodeToRemove.QueueFree();
				_otherPlayers.Remove(oldPlayerId);
				GD.Print($"[{_playerId}] Removed stale player: {oldPlayerId}");
			}
		}
	}

	public override void _ExitTree()
	{
		_cancellationTokenSource?.Cancel();
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
		if (@event.IsActionPressed("left_click")) Input.MouseMode = Input.MouseModeEnum.Captured;
		if (@event.IsActionPressed("ui_cancel")) Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventMouseMotion cameraMotion) return;
		if (Input.GetMouseMode() == Input.MouseModeEnum.Captured)
		{
			this._cameraInputDirection = cameraMotion.Relative * this.MouseSensitivity;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
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

		// publishing my position
		_timeSinceLastPublish += delta;
		if (_publishStream != null && _timeSinceLastPublish >= PublishInterval)
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
				_ = _publishStream.RequestStream.WriteAsync(new PublishRequest { PlayerPosition = positionUpdate });
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[{_playerId}] Failed to publish: {ex.Message}");
			}
		}
	}
}