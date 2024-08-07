using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
public partial class GameManager : Node
{
	[Export]
	WorldEnvironment worldEnvironment;
	[Export]
	public Node3D Sun;
	[Export]
	public Node3D Root;
	[Export]
	public ConsoleManager console;
	[Export]
	public BasePak gameSelect = BasePak.QuakeLive;
	[Export]
	public string[] mapRotation;
	private List<string> _mapRotation = new List<string>();
	[Export]
	public int timeLimit = 7;
	[Export]
	public int fragLimit = 15;
	[Export]
	public int tessellations = 5;
	[Export]
	public float colorLightning = 1f;
	[Export]
	public float mixBrightness = 0.25f;             // Range from 0 to 1, .25f Is the nicest
	[Export]
	public float shadowIntensity = 1f;
	[Export]
	public Container[] SplitScreen;
	[Export]
	public Container IntermissionContainer;
	[Export]
	public SubViewport IntermissionViewPort;
	[Export]
	public SubViewport AdvertisementViewPort;
	[Export]
	public VideoStreamPlayer AdvertisementVideo;

	[Export]
	public PackedScene viewPortPrefab;
	[Export]
	public PackedScene scoreBoard;

	public static GameManager Instance;

	public Color ambientLightColor;
	// Quake3 also uses Doom and Wolf3d scaling down
	public const float sizeDividor = 1f / 32f;
	public const float modelScale = 1f / 64f;

	//Physic Layers
	public const short DefaultLayer = 0;
	public const short NoCollisionLayer = 1;

	public const short ColliderLayer = 2;
	public const short InvisibleBlockerLayer = 3;
	public const short WalkTriggerLayer = 4;

	public const short ThingsLayer = 5;
	public const short DamageablesLayer = 6;
	public const short Player1Layer = 7;
	public const short Player2Layer = 8;
	public const short Player3Layer = 9;
	public const short Player4Layer = 10;
	public const short Player5Layer = 11;
	public const short Player6Layer = 12;
	public const short Player7Layer = 13;
	public const short Player8Layer = 14;
	public const short RagdollLayer = 15;
	public const short FogLayer = 16;
	public const short WaterLayer = 17;
	
	//3DRender Layer
	public const short Player1ViewLayer = 0;
	public const short Player2ViewLayer = 1;
	public const short Player3ViewLayer = 2;
	public const short Player4ViewLayer = 3;
	public const short Player5ViewLayer = 4;
	public const short Player6ViewLayer = 5;
	public const short Player7ViewLayer = 6;
	public const short Player8ViewLayer = 7;

	public const short Player1UIViewLayer = 8;
	public const short Player2UIViewLayer = 9;
	public const short Player3UIViewLayer = 10;
	public const short Player4UIViewLayer = 11;
	public const short Player5UIViewLayer = 12;
	public const short Player6UIViewLayer = 13;
	public const short Player7UIViewLayer = 14;
	public const short Player8UIViewLayer = 15;
	public const short PlayerNormalDepthLayer = 16;
	public const short NotVisibleLayer = 17;
	public const short UINotVisibleLayer = 17;

	//Physic Masks
	public const uint TakeDamageMask = ((1 << DamageablesLayer) | 
										(1 << Player1Layer) | 
										(1 << Player2Layer) | 
										(1 << Player3Layer) | 
										(1 << Player4Layer) | 
										(1 << Player5Layer) | 
										(1 << Player6Layer) | 
										(1 << Player7Layer) | 
										(1 << Player8Layer));

	public const uint NoHitMask = ((1 << NoCollisionLayer) |
									(1 << InvisibleBlockerLayer) |
									(1 << WalkTriggerLayer) |
									(1 << ThingsLayer));

	//Rendering Masks
	public const int InvisibleMask = 0;
	public const uint AllPlayerViewMask = ((1 << Player1ViewLayer) | (1 << Player2ViewLayer) | (1 << Player3ViewLayer) | (1 << Player4ViewLayer) | (1 << Player5ViewLayer) | (1 << Player6ViewLayer) | (1 << Player7ViewLayer) | (1 << Player8ViewLayer));

	//SplitScreen Players
	public const int MaxLocalPlayers = 8;

	private bool paused = true;
	public static bool Paused { get { return Instance.paused; } }

	private float timeMs = 0.0f;
	public static float CurrentTimeMsec { get { return Instance.timeMs; } }

	public static FuncState CurrentState { get { return Instance.currentState; } }
	public static MusicType currentMusic = MusicType.None;

	private bool timeToSync = false;
	public static bool NewTickSeconds { get { return Instance.timeToSync; } }
	public static int NumLocalPlayers { get { return Instance.Players.Count; } }

	public static ConsoleManager Console { get { return Instance.console; } }

	public float gravity = 25f;					//default 800 * sizeDividor
	public float friction = 6f;
	public float waterFriction = 12f;
	public float waterDeadFall = 4.5f;
	public float terminalLimit = 256f;
	public float terminalVelocity = 16f;
	public float barrierVelocity = 1024f;
	public float playerHeight = 1.2f;
	public int playerMass = 80;
	public int gibHealth = -40;

	public float PlayerDamageReceive = 1f;
	public int PlayerAmmoReceive = 1;
	private Godot.Environment environment;
	private float syncTime = 1;

	//skip frames are used to easen up deltaTime after loading
	public int skipFrames = 5;
	public Node3D TemporaryObjectsHolder;
	[Export]
	public PackedScene playerPrefab;
	[Export]
	public MusicType musicType = MusicType.None;
	[Export]
	public GameType gameType = GameType.FreeForAll;
	[Export]
	public SoundData[] OverrideSounds;

	public List<int> playerController = new List<int>();
	public List<PlayerThing> Players = new List<PlayerThing>();
	public string[] defaultModels = { "Doom", "Crash", "Ranger", "Visor", "Sarge", "Major", "Anarki", "Grunt" };
	public string[] defaultSkins = { "default", "default", "default", "default", "default", "default", "default", "default" };

	public Camera3D interMissionCamera = null;
	public List<int> controllerWantToJoin = new List<int>();
	public Vector2I viewPortSize = new Vector2I(1280 , 720);
	public int QuadMul = 3;

	private int mapNum = 0;
	private float mapLeftTime = 0;
	public float currentDeathRatio = 0;

	public AudioStreamPlayer AnnouncerStream;
	public AudioStreamPlayer StaticMusicPlayer = null;
	public string announcer = Announcer.Quake;

	private static readonly string FiveMinutes = "feedback/5_minute";
	private static readonly string OneMinute = "feedback/1_minute";
	private static readonly string[] Seconds = { "feedback/three", "feedback/two", "feedback/one" };
	private static readonly string[] FragsLeft = { "feedback/1_frag", "feedback/2_frags", "feedback/3_frags" };
	private int second = 0;
	private int currentDeathCount = 0;

	private bool useCustomMap = false;
	private string nextMapName;
	public static class Announcer
	{
		public const string Male = "vo/";
		public const string Quake = "vo_evil/";
		public const string Female = "vo_female/";
	}
	public enum BasePak
	{
		All = 0,
		QuakeLive = 4,
		TeamArena = 3,
		Quake3 = 2,
		Demo = 1
	}
	public enum FuncState
	{
		None,
		Ready,
		Start,
		End
	}
	public enum PrintType
	{
		Log,
		Info,
		Warning,
		Error,
		Success
	}
	public enum MusicType
	{
		None,
		Static,
		Dynamic,
		Random
	}
	public enum GameType
	{
		SinglePlayer,
		FreeForAll,
		QuadHog,
		Tournament,
		TeamDeathmatch,
		CaptureTheFlag,
		OneFlagCTF,
		Overload,
		Harvester
	}
	public enum LimitReach
	{
		None,
		Time,
		Frag
	}
	public static class ControllerType
	{
		public const int MouseKeyboard = 0;
		public const int Joy_0 = 1;
		public const int Joy_1 = 2;
		public const int Joy_2 = 3;
		public const int Joy_3 = 4;
		public const int Joy_4 = 5;
		public const int Joy_5 = 6;
		public const int Joy_6 = 7;
		public const int Joy_7 = 8;
	}

	private FuncState currentState = FuncState.None;

	private LimitReach limitReach = LimitReach.None;

	private static PrintType printType = PrintType.Log;
	private static int printLine = 0;
	private bool loading = false;
	public override void _Ready()
	{
		//Disable Physics Jitter Fix
		Engine.PhysicsJitterFix = 0;

		AnnouncerStream = new AudioStreamPlayer();
		AddChild(AnnouncerStream);
		AnnouncerStream.VolumeDb = 7;
		AnnouncerStream.Name = "AnnouncerStream";
		AnnouncerStream.Bus = "FXBus";

		GD.Randomize();
		//Used in order to parse float with "." as decimal separator
		CultureInfo CurrentCultureInfo = new CultureInfo("en", false);
		CurrentCultureInfo.NumberFormat.NumberDecimalSeparator = ".";
		CurrentCultureInfo.NumberFormat.CurrencyDecimalSeparator = ".";
		CultureInfo.DefaultThreadCurrentCulture = CurrentCultureInfo;

		Input.MouseMode = Input.MouseModeEnum.Captured;
		environment = worldEnvironment.Environment;
		ambientLightColor = environment.AmbientLightColor;
		Instance = this;

		//Init Console
		console.Init();

		//Load Sounds
		SoundManager.AddSounds(OverrideSounds);

		if ((musicType == MusicType.Static) || (musicType == MusicType.Random))
		{
			StaticMusicPlayer = new AudioStreamPlayer();
			AddChild(StaticMusicPlayer);
			StaticMusicPlayer.VolumeDb = 7;
			StaticMusicPlayer.Name = "Music";
			StaticMusicPlayer.Bus = "BKGBus";
		}

		PakManager.LoadPK3Files();
		QShaderManager.ProcessShaders();
		MaterialManager.LoadFXShaders();
		MaterialManager.SetAmbient();

		//SetGameType
		switch (gameType)
		{
			default:
			break;
			case GameType.QuadHog:
				ThingsManager.uniqueGamePlayThings.Add("item_quad");
			break;
		}

		if (gameSelect != BasePak.Demo)
		{
			PakManager.OrderLists();
			_mapRotation = PakManager.LoadMapRotation();
		}

		if (_mapRotation.Count == 0)
		{
			for (int i = 0; i < mapRotation.Length; i++)
			{
				string mapName = mapRotation[i].ToUpper();
				if (PakManager.mapList.Contains(mapName))
					_mapRotation.Add(mapName);
			}
		}
		if (_mapRotation.Count == 0)
			_mapRotation.Add(PakManager.mapList[0]);

		if (gameSelect == BasePak.Demo)
			PakManager.KeepDemoList(_mapRotation);

		mapLeftTime = timeLimit * 60;
		nextMapName = _mapRotation[0];
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventJoypadButton)
		{
			for (int i = 1; i < 8; i++) 
			{
				if (Input.IsActionJustPressed("Start_"+i))
					controllerWantToJoin.Add(i);
			}
		}

		if (@event is InputEventKey)
		{
			if (Input.IsActionJustPressed("Console"))
				console.ChangeConsole();

			if (console.visible == false)
			{
				if (Input.IsActionJustPressed("Start_0"))
					controllerWantToJoin.Add(0);
			}

			if (Input.IsActionJustPressed("Escape"))
			{
				if (!paused)
				{
					paused = true;
					Input.MouseMode = Input.MouseModeEnum.Visible;
				}
				else
				{
					paused = false;
					Input.MouseMode = Input.MouseModeEnum.Captured;
					if (console.visible)
						console.commandLine.GrabFocus();
				}
				AdvertisementVideo.Paused = paused;
			}
		}
		else if (paused)
		{
			if (@event is InputEventMouseButton)
			{
				if (Input.IsActionJustPressed("Action_Fire_0"))
				{
					paused = false;
					AdvertisementVideo.Paused = paused;
					Input.MouseMode = Input.MouseModeEnum.Captured;
					if (console.visible)
						console.commandLine.GrabFocus();
				}
			}
		}
	}
	public override void _Process(double delta)
	{
		float deltaTime = (float)delta;

		if (!paused)
		{
			timeMs += deltaTime;
			timeToSync = CheckIfSyncTime(deltaTime);
			RenderingServer.GlobalShaderParameterSet("MsTime", CurrentTimeMsec);
		}

		if (mapLeftTime > 0)
		{
			mapLeftTime -= deltaTime;
			if ((mapLeftTime > 299) && (mapLeftTime < 300))
			{
				if (timeToSync)
					PlayAnnouncer(FiveMinutes);
			}
			else if ((mapLeftTime > 59) && (mapLeftTime < 60))
			{
				if (timeToSync)
					PlayAnnouncer(OneMinute);
			}
			else if (mapLeftTime < 4)
			{
				if (timeToSync)
				{
					if (mapLeftTime < 1)
					{
						IntermissionContainer.Show();
						if (limitReach == LimitReach.None)
							limitReach = LimitReach.Time;
						if (console.visible)
							console.ChangeConsole(true);
					}
					else if (limitReach == LimitReach.None)
						PlayAnnouncer(Seconds[second++]);
				}
			}

		}
		if (mapLeftTime < 0)
		{
			if (!useCustomMap)
			{
				mapNum++;
				if (mapNum >= _mapRotation.Count)
					mapNum = 0;
				nextMapName = _mapRotation[mapNum];
			}
			mapLeftTime = timeLimit * 60;
			second = 0;
			paused = true;
			CallDeferred("ChangeMap");
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		switch (currentState)
		{
			default:
			break;
			case FuncState.None:
				if (skipFrames > 0)
				{
					skipFrames--;
					if (skipFrames == 0)
					{
						LoadMap();
						currentState = FuncState.Ready;
					}
				}
			break;
			case FuncState.Ready:
				if (skipFrames > 0)
				{
					skipFrames--;
					if (skipFrames == 0)
					{
						if (loading)
						{
							if (Players.Count > 0)
								AddAllPlayer();
							loading = false;
						}
						else
							IntermissionViewPort.Size = DisplayServer.WindowGetSize();
						paused = false;
						currentState = FuncState.Start;
					}
				}
			break;
			case FuncState.Start:
				for(int i = 0; i < controllerWantToJoin.Count; i++)
				{
					int controller = controllerWantToJoin[i];
					if (playerController.Contains(controller))
						continue;
					int playerNum = Players.Count;
					SetupNewPlayer(playerNum, controller);
					CheckNumPlayerAdded(playerNum);
					controllerWantToJoin.Remove(controller);
				}
			break;
		}
	}
	public void PlayAnnouncer(string sound)
	{
		AudioStream audio = SoundManager.LoadSound(sound);
		if (audio == null)
			audio = SoundManager.LoadSound(sound.Replace("feedback/", announcer));

		if (audio == null)
			return;

		AnnouncerStream.Stream = audio;
		AnnouncerStream.Play();
	}

	public void CheckDeathCount(int frags)
	{
		int left = fragLimit - frags;
		if (left > 0)
		{
			left--;
			if (left < 3)
				PlayAnnouncer(FragsLeft[left]);
		}
		else
		{
			limitReach = LimitReach.Frag;
			mapLeftTime = 1f;
		}
		currentDeathCount++;
	}

	public float GetDeathRatioAndReset(bool reset = true)
	{
		currentDeathRatio = (currentDeathCount / Players.Count);
		if (reset)
			currentDeathCount = 0;
		return currentDeathRatio;
	}

	public void AddAllPlayer()
	{
		foreach (PlayerThing player in Players)
		{
			if (player.playerControls.playerWeapon != null)
			{
				player.playerControls.playerWeapon.QueueFree();
				player.playerControls.playerWeapon = null;
			}
			if (player.interpolatedTransform != null)
				player.interpolatedTransform.QueueFree();
			player.playerInfo.playerPostProcessing.playerHUD.RemoveAllItems();
			player.playerInfo.Reset();
			player.deaths = 0;
			player.playerInfo.playerPostProcessing.playerHUD.deathsText.Text = "0";
			player.frags = 0;
			player.playerInfo.playerPostProcessing.playerHUD.fragsText.Text = "0";
			player.InitPlayer();
			if (ScoreBoard.Instance != null)
				ScoreBoard.Instance.AddPlayer(player);
		}
		IntermissionContainer.Hide();
		switch (musicType)
		{
			default:
				currentMusic = MusicType.None;
			break;
			case MusicType.Static:
				StaticMusicPlayer.Stop();
				StaticMusicPlayer.Play();
				currentMusic = MusicType.Static;
			break;
			case MusicType.Dynamic:
				AdaptativeMusicManager.Instance.StopMusic();
				AdaptativeMusicManager.Instance.StartMusic();
				currentMusic = MusicType.Dynamic;
			break;
			case MusicType.Random:
				AdaptativeMusicManager.Instance.StopMusic();
				if (StaticMusicPlayer.Stream != null)
				{
					StaticMusicPlayer.Stop();
					if (GD.RandRange(0, 1) > 0)
					{
						StaticMusicPlayer.Play();
						currentMusic = MusicType.Static;
					}
					else
					{
						AdaptativeMusicManager.Instance.StartMusic();
						currentMusic = MusicType.Dynamic;
					}
				}
				else
				{
					AdaptativeMusicManager.Instance.StartMusic();
					currentMusic = MusicType.Dynamic;
				}
			break;
		}
	}

	public void LoadMap()
	{
		TemporaryObjectsHolder = new Node3D();
		TemporaryObjectsHolder.Name = "TemporaryObjectsHolder";
		AddChild(TemporaryObjectsHolder);
		if (MapLoader.Load(nextMapName))
		{
			interMissionCamera = null;
			ClusterPVSManager.Instance.ResetClusterList(MapLoader.surfaces.Count);
			MapLoader.GenerateMapCollider();
			MapLoader.GenerateMapFog();
			MapLoader.GenerateSurfaces();
			MapLoader.SetLightVolData();
			ThingsManager.AddThingsToMap();
			SpawnerManager.CheckSpawnLocations();
		}
		limitReach = LimitReach.None;
		skipFrames = 5;
		loading = true;
	}

	public void ChangeMap(string nextMap)
	{
		if (!string.IsNullOrEmpty(nextMap))
		{
			useCustomMap = true;
			nextMapName = nextMap;
		}
		console.ChangeConsole(true);
		limitReach = LimitReach.Time;
		mapLeftTime = 1;		
	}

	public void ChangeMap()
	{
		useCustomMap = false;
		MapLoader.UnloadMap();
		skipFrames = 5;
		currentState = FuncState.None;
	}

	public void CheckNumPlayerAdded(int playerNum)
	{
		if (playerNum > 0)
		{
			ThingsManager.NewLocalPlayerAdded();
			return;
		}

		switch (musicType)
		{
			default:
			break;
			case MusicType.Static:
				StaticMusicPlayer.Play();
			break;
			case MusicType.Dynamic:
				AdaptativeMusicManager.Instance.StartMusic();
			break;
			case MusicType.Random:
				if (GD.RandRange(0, 1) > 0)
					StaticMusicPlayer.Play();
				else
					AdaptativeMusicManager.Instance.StartMusic();
			break;
		}
	}

	public void SetupNewPlayer(int playerNum, int controllerNum)
	{
		PlayerThing player = (PlayerThing)playerPrefab.Instantiate();
		player.Name = "Player "+ playerNum;
		player.playerViewPort = (PlayerViewPort)viewPortPrefab.Instantiate();
		AddChild(player);
		if (playerNum == 0)
		{
			player.playerInfo.playerPostProcessing.ViewPortCamera.Current = true;
			IntermissionContainer.Hide();
		}

		player.playerName = defaultModels[playerNum];
		player.modelName = defaultModels[playerNum];
		player.skinName = defaultSkins[playerNum];

		player.playerInfo.SetPlayer(playerNum);
		player.playerControls.Init(controllerNum);
		player.InitPlayer();
		Players.Add(player);
		playerController.Add(controllerNum);
		switch (Players.Count)
		{
			default:
			break;
			case 1:
				SplitScreen[0].AddChild(player.playerViewPort);
			break;
			case 2:
				SplitScreen[1].AddChild(player.playerViewPort);
			break;
			case 3:
				SplitScreen[1].AddChild(player.playerViewPort);
			break;
			case 4:
				SplitScreen[0].AddChild(player.playerViewPort);
			break;
			case 5:
				SplitScreen[1].AddChild(player.playerViewPort);
			break;
			case 6:
				SplitScreen[0].AddChild(player.playerViewPort);
			break;
			case 7:
				Players[2].playerViewPort.Reparent(SplitScreen[2]);
				SplitScreen[2].AddChild(player.playerViewPort);
				IntermissionContainer.Reparent(SplitScreen[1]);
				SplitScreen[1].MoveChild(IntermissionContainer, 1);
				IntermissionContainer.Show();
			break;
			case 8:
				SplitScreen[2].AddChild(player.playerViewPort);
			break;
		}
		ArrangeSplitScreen();
		if (ScoreBoard.Instance != null)
			ScoreBoard.Instance.AddPlayer(player);
	}

	public void RemovePlayer(int playerNum)
	{
		if (playerNum >= Players.Count)
			return;

		if (Players.Count == 7)
		{
			IntermissionContainer.Reparent(this);
			IntermissionContainer.Hide();
		}
			
		Players[playerNum].Damage(1000, DamageType.Telefrag);
		Players[playerNum].playerViewPort.QueueFree();
		Players[playerNum].QueueFree();
		Players.RemoveAt(playerNum);
		playerController.RemoveAt(playerNum);
		ScoreBoard.Instance.RemovePlayer(playerNum);
		for(int i = 0; i < Players.Count; i++)
		{
			PlayerThing player = Players[i];
			player.playerInfo.UpdatePlayer(i);
			switch (Players.Count)
			{
				default:
					break;
				case 1:
					player.playerViewPort.Reparent(SplitScreen[0]);
					break;
				case 2:
				{
					switch (i)
					{
						default:
							player.playerViewPort.Reparent(SplitScreen[0]);
						break;
						case 1:
							player.playerViewPort.Reparent(SplitScreen[1]);
						break;
					}
				}
				break;
				case 3:
				{
					switch (i)
					{
						case 0:
							player.playerViewPort.Reparent(SplitScreen[0]);
						break;
						default:
							player.playerViewPort.Reparent(SplitScreen[1]);
						break;
					}
				}
				break;
				case 4:
				{
					switch (i)
					{
						case 0:
						case 3:
							player.playerViewPort.Reparent(SplitScreen[0]);
						break;
						default:
							player.playerViewPort.Reparent(SplitScreen[1]);
						break;
					}
				}
				break;
				case 5:
				{
					switch (i)
					{
						case 0:
						case 3:
							player.playerViewPort.Reparent(SplitScreen[0]);
						break;
						default:
							player.playerViewPort.Reparent(SplitScreen[1]);
						break;
					}
				}
				break;
				case 6:
				{
					switch (i)
					{
						case 0:
						case 3:
						case 5:
							player.playerViewPort.Reparent(SplitScreen[0]);
						break;
						default:
							player.playerViewPort.Reparent(SplitScreen[1]);
						break;
					}
				}
				break;
				case 7:
				{
					switch (i)
					{
						case 0:
						case 3:
						case 5:
							player.playerViewPort.Reparent(SplitScreen[0]);
						break;
						case 2:
							player.playerViewPort.Reparent(SplitScreen[2]);
						break;
						case 6:
							player.playerViewPort.Reparent(SplitScreen[2]);
							IntermissionContainer.Reparent(SplitScreen[1]);
							SplitScreen[1].MoveChild(IntermissionContainer, 1);
							IntermissionContainer.Show();
						break;
						default:
							player.playerViewPort.Reparent(SplitScreen[1]);
						break;
					}
				}
				break;
			}
		}
		ArrangeSplitScreen();
	}

	public void ArrangeSplitScreen()
	{
		Vector2I Size = DisplayServer.WindowGetSize();
		int i = 0;
		foreach (PlayerThing player in Players) 
		{
			Vector2I size = Size;
			switch (Players.Count)
			{
				default:
				case 1:
				break;
				case 2:
					size.Y /= 2;
				break;
				case 3:
					size.Y /= 2;
					if (i > 0)
						size.X /= 2;
				break;
				case 4:
					size.Y /= 2;
					size.X /= 2;
				break;
				case 5:
					size.Y /= 2;
					if ((i == 0) || (i == 3))
						size.X /= 2;
					else
						size.X /= 3;
				break;
				case 6:
					size.Y /= 2;
					size.X /= 3;
				break;
				case 7:
					size.Y /= 3;
					if ((i == 2) || (i == 6))
						size.X /= 2;
					else
						size.X /= 3;
					if (i == 0)
						IntermissionViewPort.Size = size;
				break;
				case 8:
					size.Y /= 3;
					size.X /= 3;
					if (i == 0)
						IntermissionViewPort.Size = size;
				break;
			}
			player.playerViewPort.viewPort.Size = size;
			player.playerInfo.playerPostProcessing.ViewPort.Size = size;
			SetViewPortToCamera(player.playerInfo.playerPostProcessing.ViewPortCamera, player.playerViewPort.viewPort);
			i++;
		}
	}

	public void SetViewPortToCamera(Camera3D camera, SubViewport viewPort)
	{
		var CamRID = camera.GetCameraRid();
		var viewPortRID = viewPort.GetViewportRid();
		RenderingServer.ViewportAttachCamera(viewPortRID, CamRID);
	}

	public static List<Node> GetAllChildrens(Node parent)
	{
		List<Node> list = new List<Node>();

		var Childrens = parent.GetChildren();
		foreach (var child in Childrens)
		{
			if (child.IsQueuedForDeletion())
				continue;

			list.Add(child);
			list.AddRange(GetAllChildrens(child));
		}
		return list;
	}

	public static List<MeshInstance3D> GetModulateMeshes(Node parent, List<MeshInstance3D> ignoreList = null)
	{
		var Childrens = GetAllChildrens(parent);
		List<MeshInstance3D> currentMeshes = new List<MeshInstance3D>();
		if (ignoreList == null)
			ignoreList = new List<MeshInstance3D>();
		foreach (var child in Childrens)
		{
			if (child is MeshInstance3D mesh)
			{
				if (mesh.Mesh == null)
					continue;

				if (ignoreList.Contains(mesh))
					continue;
				ShaderMaterial shaderMaterial = (ShaderMaterial)mesh.GetActiveMaterial(0);
				var Results = RenderingServer.GetShaderParameterList(shaderMaterial.Shader.GetRid());
				foreach (var result in Results)
				{
					Variant nameVar;
					if (result.TryGetValue("name", out nameVar))
					{
						string name = (string)nameVar;
						if (name.Contains("UseModulation"))
						{
							currentMeshes.Add(mesh);
							break;
						}
					}
				}
			}
		}
		return currentMeshes;
	}
	public static List<MeshInstance3D> CreateFXMeshInstance3D(Node parent)
	{
		var Childrens = GetAllChildrens(parent);
		List<MeshInstance3D> fxMeshes = new List<MeshInstance3D>();
		foreach (var child in Childrens)
		{
			if (child is MeshInstance3D mesh)
			{
				if (mesh.Mesh == null)
					continue;

				MeshInstance3D fxMesh = new MeshInstance3D();
				fxMesh.Mesh = mesh.Mesh;
				fxMesh.Layers = mesh.Layers;
				fxMesh.Visible = false;
				mesh.AddChild(fxMesh);
				fxMeshes.Add(fxMesh);
			}
		}
		return fxMeshes;
	}
	public static void ChangeQuadFx(List<MeshInstance3D> fxMeshes, bool enable, bool viewModel = false)
	{
		for (int i = 0; i < fxMeshes.Count; i++)
		{
			MeshInstance3D mesh = fxMeshes[i];
			if (enable)
			{
				if (viewModel)
					mesh.SetSurfaceOverrideMaterial(0, MaterialManager.quadWeaponFxMaterial);
				else
					mesh.SetSurfaceOverrideMaterial(0, MaterialManager.quadFxMaterial);
				mesh.Visible = true;
			}
			else
				mesh.Visible = false;
		}
	}
	public static void Print(string Message, PrintType type = PrintType.Log)
	{
		if (type >= printType)
		{
			GD.Print(printLine + ": " + Message);

			if (Instance == null)
				return;

			Console.AddToConsole(Message, type);
			switch (type)
			{
				default:
				break;
				case PrintType.Warning:
					GD.PushWarning(printLine + ": " + Message);
				break;
				case PrintType.Error:
					GD.PushError(printLine + ": " + Message);
				break;
			}
			printLine++;
		}
	}
	private bool CheckIfSyncTime(float deltaTime)
	{
		syncTime -= deltaTime;
		if (syncTime < 0)
		{
			syncTime += 1;
			return true;
		}
		return false;
	}

	public void ChangeTimeLimit(int limit)
	{
		if (limit < timeLimit)
		{
			mapLeftTime -= (timeLimit - limit) * 60;
			if (mapLeftTime < 1)
			{
				limitReach = LimitReach.Time;
				mapLeftTime = 1;
			}
		}
		else
			mapLeftTime += (limit - timeLimit) * 60;
		timeLimit = limit;
	}

	public void ChangeFragLimit(int limit)
	{
		fragLimit = limit;
		foreach (PlayerThing player in Players)
			CheckDeathCount(player.frags);
	}

	public static void QuitGame()
	{
		SceneTree main = Instance.GetTree();
		main.Root.PropagateNotification((int)NotificationWMCloseRequest);
		main.Quit();
	}

}
