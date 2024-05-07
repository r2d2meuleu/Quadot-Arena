using Godot;
using System;
using System.Collections;
using System.Collections.Generic;

public partial class TriggerController : Node3D
{
	public List<Area3D> Areas = new List<Area3D>();
	public string triggerName = "";
	public bool activated = false;
	private List<Action<PlayerThing>> OnActivate = new List<Action<PlayerThing>>();
	private HashSet<Node3D> CurrentColliders = new HashSet<Node3D>();
	public bool Repeatable = false;
	public bool AutoReturn = false;
	public float AutoReturnTime = 1f;
	public bool activateOnInit = false;

	public Func<bool> PreReq = new Func<bool>(() => { return true; });

	public float time = 0f;
	private float waitTime = 0;
	private GameManager.FuncState currentState = GameManager.FuncState.None;

	public void SetController(string name, Action<PlayerThing> activeAction)
	{
		triggerName = name;
		OnActivate.Add(activeAction);
	}
	public bool Activate(PlayerThing playerThing)
	{
		if (!PreReq())
		{
			GameManager.Print("TriggerController: Prereq False for: " + triggerName, GameManager.PrintType.Info);
			return false;
		}

		if ((!Repeatable) || (AutoReturn))
			if (activated)
				return false;

		if (AutoReturn)
			time = AutoReturnTime;

		activated = !activated;
		for(int i = 0; i < OnActivate.Count; i++)
			OnActivate[i].Invoke(playerThing);

		return true;
	}

	public void ActivateAfterTime(float time)
	{
		waitTime = time;
		currentState = GameManager.FuncState.Start;
	}

	public override void _Process(double delta)
	{
		if (GameManager.Paused)
			return;

		float deltaTime = (float)delta;
		switch (currentState)
		{
			default:
			break;
			case GameManager.FuncState.None:
				if (activateOnInit)
					Activate(null);
				currentState = GameManager.FuncState.Ready;
			break;
			case GameManager.FuncState.Start:
				waitTime -= deltaTime;
				if (waitTime <= 0)
				{
					Activate(null);
					currentState = GameManager.FuncState.Ready;
				}
			break;
		}


		if (time <= 0)
			return;
		else
		{
			time -= deltaTime;
			if (time <= 0)
				activated = !activated;
		}
	}
	public void OnBodyEntered(Node3D other)
	{
		if (GameManager.Paused)
			return;

		if (other is PlayerThing player)
		{
			if (!player.ready)
				return;

			GameManager.Print("Someone " + other.Name + " tried to activate this " + Name);

			//Will activate everything back on the main thread
			if (!CurrentColliders.Contains(other))
				CurrentColliders.Add(other);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (GameManager.Paused)
			return;

		if (Areas.Count == 0)
		{
			SetPhysicsProcess(false);
			return;
		}

		if (CurrentColliders.Count == 0)
			return;

		for (int n = 0; n < Areas.Count; n++)
		{
			Area3D Area = Areas[n];
			var CurrentBodies = Area.GetOverlappingBodies();
			int CurrentBodiesNum = CurrentBodies.Count;
			if (CurrentBodiesNum == 0)
			{
				CurrentColliders.Clear();
				return;
			}

			for (int i = 0; i < CurrentBodiesNum; i++)
			{
				Node3D CurrentBody = CurrentBodies[i];
				if (CurrentColliders.Contains(CurrentBody))
				{
					PlayerThing playerThing = CurrentBody as PlayerThing;
					if ((!playerThing.ready) || (playerThing.Dead))
					{
						CurrentColliders.Remove(CurrentBody);
						continue;
					}

					GameManager.Print("Someone " + CurrentBody.Name + " activated this " + Name);
					Activate(playerThing);
					CurrentColliders.Remove(CurrentBody);
				}
			}
		}
	}
}
