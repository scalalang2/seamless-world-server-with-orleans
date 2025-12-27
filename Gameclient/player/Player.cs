using Godot;
using System;
using System.Text.RegularExpressions;

public partial class Player : CharacterBody3D
{
	[Export] 
	public float MouseSensitivity = 1.5f;

	private Node3D _cameraNode;
	private Vector2 cameraInputDirection = Vector2.Zero;

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

	public override void _Ready()
	{
		this._cameraNode = GetNode<Node3D>("CameraPivot");
	}

	public override void _PhysicsProcess(double delta)
	{
		if (this._cameraNode != null)
		{
			this._cameraNode.RotateY(Mathf.DegToRad(this.cameraInputDirection.X * (float)delta));
			this.cameraInputDirection = Vector2.Zero;
		}
	}
}
