using Godot;
using System;

namespace GameClient.player;

public partial class SophiaSkin : Node3D
{
	private AnimationTree _animationTree;
	private AnimationNodeStateMachinePlayback _stateMachine;
	private string _moveTiltPath = "parameters/StateMachine/Move/tilt/add_amount";
	
	// Backing field for property
	private float _runTilt = 0.0f;
	private bool _blink = true;

	private Timer _blinkTimer;
	private Timer _closedEyesTimer;
	private StandardMaterial3D _eyeMat;

	// Use Export to expose to the Inspector
	[Export]
	public bool Blink
	{
		get => _blink;
		set => SetBlink(value);
	}

	public float RunTilt
	{
		get => _runTilt;
		set => SetRunTilt(value);
	}

	public override void _Ready()
	{
		// Get unique nodes using GetNode (comparable to % in GDScript if "Access as Unique Name" is on)
		// Note: You must ensure the path matches your scene structure.
		_animationTree = GetNode<AnimationTree>("%AnimationTree");
		_stateMachine = (AnimationNodeStateMachinePlayback)_animationTree.Get("parameters/StateMachine/playback");
		
		_blinkTimer = GetNode<Timer>("%BlinkTimer");
		_closedEyesTimer = GetNode<Timer>("%ClosedEyesTimer");

		// Accessing the mesh material override.
		// Adjust the path "sophia/rig/Skeleton3D/Sophia" if necessary.
		var meshInstance = GetNode<MeshInstance3D>("sophia/rig/Skeleton3D/Sophia");
		_eyeMat = (StandardMaterial3D)meshInstance.Get("surface_material_override/2");

		// Connecting signals using lambda expressions
		_blinkTimer.Timeout += () =>
		{
			_eyeMat.Uv1Offset = new Vector3(0.0f, 0.5f, 0.0f);
			_closedEyesTimer.Start(0.2);
		};

		_closedEyesTimer.Timeout += () =>
		{
			_eyeMat.Uv1Offset = Vector3.Zero;
			// GD.RandRange is static in C#
			_blinkTimer.Start(GD.RandRange(1.0f, 4.0f));
		};
	}

	private void SetBlink(bool state)
	{
		if (_blink == state) return;
		_blink = state;

		if (_blink)
		{
			_blinkTimer.Start(0.2);
		}
		else
		{
			_blinkTimer.Stop();
			_closedEyesTimer.Stop();
		}
	}

	private void SetRunTilt(float value)
	{
		_runTilt = Mathf.Clamp(value, -1.0f, 1.0f);
		_animationTree.Set(_moveTiltPath, _runTilt);
	}

	public void Idle()
	{
		_stateMachine.Travel("Idle");
	}

	public void Move()
	{
		_stateMachine.Travel("Move");
	}

	public void Fall()
	{
		_stateMachine.Travel("Fall");
	}

	public void Jump()
	{
		_stateMachine.Travel("Jump");
	}

	public void EdgeGrab()
	{
		_stateMachine.Travel("EdgeGrab");
	}

	public void WallSlide()
	{
		_stateMachine.Travel("WallSlide");
	}
}
