using Godot;
using System;

public partial class PlayerCamera : Node3D
{
	[Export]
	public PlayerControls playerControls;
	[Export]
	public Camera3D ThirdPerson;
	[Export]
	public Camera3D ViewCamera;

	public Camera3D CurrentCamera;
	private Vector3 rotAngle = Vector3.Zero;
	public bool currentThirdPerson = false;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		CurrentCamera = ViewCamera;
		GameManager.Instance.SetViewPortToCamera(ViewCamera);
	}

	public override void _PhysicsProcess(double delta)
	{
		rotAngle.X = playerControls.viewDirection.X;

		RotationDegrees = rotAngle;
	}

	public void ChangeThirdPersonCamera(bool enable)
	{
		currentThirdPerson = enable;
		if (enable)
		{
			ThirdPerson.Visible = true;
			CurrentCamera = ThirdPerson;
			GameManager.Instance.SetViewPortToCamera(ThirdPerson);
			playerControls.playerThing.avatar.ChangeLayer(GameManager.AllPlayerViewMask);
			return;
		}
		CurrentCamera = ViewCamera;
		GameManager.Instance.SetViewPortToCamera(ViewCamera);
		ThirdPerson.Visible = false;
		playerControls.playerThing.avatar.ChangeLayer(GameManager.AllPlayerViewMask & ~((uint)(playerControls.playerInfo.viewLayer)));
	}
}
