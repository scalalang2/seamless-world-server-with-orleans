using Godot;
using System;
using System.Text.RegularExpressions;
using GameClient.player;

public partial class Player : CharacterBody3D
{
	[Export] 
	public float MouseSensitivity = 1.5f;

	[ExportGroup("Movement")] 
	[Export] 
	public float MoveSpeed = 8.0f;

	[Export]
	public float Acceleration = 20.0f;

	[Export] 
	public float RotationSpeed = 12.0f;
	
	private Node3D _cameraNode;
	private SophiaSkin _sophiaSkin;
	
	private Vector2 cameraInputDirection = Vector2.Zero;
	private Vector3 lastMovementDirection = Vector3.Back;
	private float gravity = 30.0f;
	
	public override void _Ready()
	{
		this._cameraNode = GetNode<Node3D>("CameraPivot");
		this._sophiaSkin = GetNode<SophiaSkin>("SophiaSkin");
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("left_click"))
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
		}

		if (@event.IsActionPressed("ui_cancel"))
		{
			Input.MouseMode = Input.MouseModeEnum.Visible;
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventMouseMotion cameraMotion) return;
		var isCameraMotion = Input.GetMouseMode() == Input.MouseModeEnum.Captured;
		if (isCameraMotion)
		{
			this.cameraInputDirection = cameraMotion.Relative * this.MouseSensitivity;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (this._cameraNode == null) return;
		
		this._cameraNode.RotateY(Mathf.DegToRad(-this.cameraInputDirection.X * (float)delta));
		this.cameraInputDirection = Vector2.Zero;

		var rawInput = Input.GetVector("move_right", "move_left", "move_down", "move_up");
		var forward = this._cameraNode.GlobalBasis.Z;
		var right = _cameraNode.GlobalBasis.X;
		
		var moveDirection = (forward * rawInput.Y + right * rawInput.X).Normalized();
		moveDirection.Y = 0.0f;

		var yVelocity = Velocity.Y;
		Velocity = Velocity.MoveToward(moveDirection * this.MoveSpeed, (float)delta * this.Acceleration);
		if (!IsOnFloor())
		{
			yVelocity -= (float)delta * gravity;
		}
		else
		{
			yVelocity = -0.01f;
		}

		Velocity = new Vector3(Velocity.X, yVelocity, Velocity.Z);
		MoveAndSlide();
		
		if(moveDirection.Length() > 0.2f)
		{
			lastMovementDirection = moveDirection;
		}
		
		// rotate sophia skin by move direction
		var targetRotation = Mathf.Atan2(lastMovementDirection.X, lastMovementDirection.Z);
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
	}
}
