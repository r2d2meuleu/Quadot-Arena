using Godot;
using System.Collections;
using System.Collections.Generic;
public static class SpawnerManager
{
	public static List<Target> deathMatchSpawner = new List<Target>();

	public static string respawnSound = "world/telein";

	public static void AddToList(Target target)
	{
		deathMatchSpawner.Add(target);
	}

	public static void SpawnToLocation(PlayerThing player)
	{
		Target target = deathMatchSpawner[GD.RandRange(0, deathMatchSpawner.Count - 1)];
		ClusterPVSManager.CheckPVS(player.playerInfo.viewLayer, target.destination);
		TeleporterThing.TelefragEverything(target.destination, player);
		player.Position = target.destination;
		player.playerControls.InvoqueSetTransformReset();

		SoundManager.Create3DSound(target.destination, SoundManager.LoadSound(respawnSound));
		player.playerControls.viewDirection.Y = target.angle;
		player.playerControls.impulseVector = Vector3.Zero;
		player.playerControls.playerVelocity = Vector3.Zero;
		player.playerControls.jumpPadVel = Vector3.Zero;
		player.Impulse(Quaternion.FromEuler(new Vector3(0, Mathf.DegToRad(target.angle), 0)) * Vector3.Forward, 1500);

	}
}
