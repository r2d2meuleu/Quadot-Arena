using Godot;
using System.Collections.Generic;

public partial class ModelController : Node3D
{
	[Export]
	public string modelName = "";
	[Export]
	public string shaderName = "";
	[Export]
	public bool isTransparent = false;
	[Export]
	public bool receiveShadows = false;
	[Export]
	public bool castShadows = false;
	[Export]
	public bool useLowCountMultiMesh = true;
	[Export]
	public bool alphaFade = false;

	public MD3 Model { get { return md3Model; } }
	private MD3 md3Model = null;

	private MeshProcessed model;
	[Export]
	public float modelAnimationFPS = 0;
	[Export]
	public float textureAnimationFPS = 0;
	[Export]
	public DestroyType destroyType;
	[Export]
	public float destroyTimer = 0;

	private List<MultiMeshData> multiMeshDataList = new List<MultiMeshData>();
	private List<Node3D> destroyNodes = new List<Node3D>();
	private List<int> modelAnim = new List<int>();
	private List<int> textureAnim = new List<int>();
	private Dictionary<int, ShaderMaterial[]> materials = new Dictionary<int, ShaderMaterial[]>();

	private int modelCurrentFrame;
	private List<int> textureCurrentFrame = new List<int>();

	private Vector3 currentOrigin;
	private float height;

	private float ModelLerpTime = 0;
	private float ModelCurrentLerpTime = 0;

	private float TextureLerpTime = 0;
	private float TextureCurrentLerpTime = 0;

	private GameManager.FuncState currentState = GameManager.FuncState.None;

	private Color Modulate = Colors.Black;
	private float baseTime = 1;
	public enum DestroyType
	{
		NoDestroy,
		DestroyAfterTime,
		DestroyAfterModelLastFrame,
		DestroyAfterTextureLastFrame
	}

	private Vector3 lastGlobalPosition = new Vector3(0, 0, 0);
	private Basis	lastGlobalBasis = Basis.Identity;	

	public void AddDestroyNode(Node3D node)
	{
		if (!destroyNodes.Contains(node))
			destroyNodes.Add(node);
	}

	public override void _Ready()
	{
		Init();
	}

	public void Init()
	{
		currentState = GameManager.FuncState.Ready;
		if (string.IsNullOrEmpty(modelName))
			return;

		md3Model = ModelsManager.GetModel(modelName, isTransparent);
		if (md3Model == null)
			return;

		Node3D currentObject = this;
		Dictionary<string, string> meshToSkin = null;
		if (!string.IsNullOrEmpty(shaderName))
		{
			shaderName = shaderName.ToUpper();
			meshToSkin = new Dictionary<string, string>
			{
				{ md3Model.meshes[0].name, shaderName }
			};
			if (!TextureLoader.HasTexture(shaderName))
				TextureLoader.AddNewTexture(shaderName, isTransparent);
		}

		model = Mesher.GenerateModelFromMeshes(md3Model, GameManager.AllPlayerViewMask, receiveShadows, castShadows, currentObject, isTransparent, ((modelAnimationFPS == 0) && !isTransparent), meshToSkin, useLowCountMultiMesh, alphaFade);

		for (int i = 0; i < md3Model.meshes.Count; i++)
		{
			var modelMesh = md3Model.meshes[i];
			if (modelMesh.numFrames > 1)
				modelAnim.Add(i);

			if (!string.IsNullOrEmpty(shaderName))
				continue;

			if (modelMesh.numSkins > 1)
			{
				ShaderMaterial[] frames = new ShaderMaterial[modelMesh.numSkins];
				for (int j = 0; j < modelMesh.numSkins; j++)
				{
					string texName = modelMesh.skins[j].name;
					bool currentTransparent = isTransparent;
					if (TextureLoader.HasTexture(texName))
						frames[j] = MaterialManager.GetMaterials(texName, -1, ref currentTransparent);
					else
					{
						TextureLoader.AddNewTexture(texName, isTransparent);
						frames[j] = MaterialManager.GetMaterials(texName, -1, ref currentTransparent);
					}
				}
				textureAnim.Add(i);
				textureCurrentFrame.Add(0);
				materials.Add(i, frames);
			}
		}

		modelCurrentFrame = 0;
		if (destroyTimer != 0)
			baseTime = destroyTimer;
		ModelsManager.AddModel(this);
	}
	void AnimateModel(float deltaTime)
	{
		if (modelAnimationFPS == 0)
			return;

		int currentFrame = modelCurrentFrame;
		int nextFrame = currentFrame + 1;
		
		if (nextFrame >= md3Model.numFrames)
			nextFrame = 0;
		if ((nextFrame == 0) && (destroyType == DestroyType.DestroyAfterModelLastFrame))
		{
			QueueFree();
			return;
		}

		if (currentFrame == nextFrame)
			return;

		for (int i = 0; i < modelAnim.Count; i++)
		{
			MD3Mesh currentMesh = md3Model.meshes[modelAnim[i]];
			for (int j = 0; j < currentMesh.numVertices; j++)
			{
				Vector3 newVertex = currentMesh.verts[currentFrame][j].Lerp(currentMesh.verts[nextFrame][j], ModelCurrentLerpTime);
				Vector3 newNormal = currentMesh.normals[currentFrame][j].Lerp(currentMesh.normals[nextFrame][j], ModelCurrentLerpTime);
				model.data[i].meshDataTool.SetVertex(j, newVertex);
				model.data[i].meshDataTool.SetVertexNormal(j, newNormal);
			}
			model.data[i].arrMesh.ClearSurfaces();
			model.data[i].meshDataTool.CommitToSurface(model.data[i].arrMesh);
		}

		ModelLerpTime = modelAnimationFPS * deltaTime;
		ModelCurrentLerpTime += ModelLerpTime;

		if (ModelCurrentLerpTime >= 1.0f)
		{
			ModelCurrentLerpTime -= 1.0f;
			modelCurrentFrame = nextFrame;
		}
	}
	void AnimateTexture(float deltaTime)
	{
		if (textureAnimationFPS == 0)
			return;

		TextureLerpTime = textureAnimationFPS * deltaTime;
		TextureCurrentLerpTime += TextureLerpTime;

		for (int i = 0; i < textureAnim.Count; i++)
		{
			MD3Mesh currentMesh = md3Model.meshes[textureAnim[i]];

			int currentFrame = textureCurrentFrame[i];
			int nextFrame = currentFrame + 1;

			if (nextFrame >= currentMesh.numSkins)
				nextFrame = 0;
			if ((nextFrame == 0) && (destroyType == DestroyType.DestroyAfterTextureLastFrame))
			{
				QueueFree();
				return;
			}

			if (currentFrame == nextFrame)
				continue;

			if (TextureCurrentLerpTime >= 1.0f)
			{
				model.data[currentMesh.meshNum].meshDataTool.SetMaterial(materials[i][nextFrame]);
				model.data[currentMesh.meshNum].arrMesh.SurfaceSetMaterial(0, materials[i][nextFrame]);
				textureCurrentFrame[i] = nextFrame;
			}
		}

		if (TextureCurrentLerpTime >= 1.0f)
			TextureCurrentLerpTime -= 1.0f;
	}
	public void Start()
	{
		if (string.IsNullOrEmpty(modelName))
		{
			QueueFree();
			return;
		}

		if (md3Model == null)
		{
			GameManager.Print("Model not found: " + modelName, GameManager.PrintType.Warning);
			QueueFree();
			return;
		}

		if (modelAnimationFPS == 0)
		{
			for (int i = 0; i < model.data.Length; i++)
			{
				if (model.data[i] == null)
					continue;
				if (model.data[i].isTransparent)
					continue;

				if (Mesher.MultiMeshes.ContainsKey(model.data[i].multiMesh))
				{
					MultiMeshData multiMeshData = new MultiMeshData();
					multiMeshData.multiMesh = model.data[i].multiMesh;
					Mesher.AddNodeToMultiMeshes(model.data[i].multiMesh, this, Modulate);
					multiMeshData.owner = this;
					multiMeshDataList.Add(multiMeshData);
				}
			}
		}

		currentState = GameManager.FuncState.Start;
	}

	void UpdateMultiMesh()
	{
		if (multiMeshDataList.Count == 0)
			return;

		if ((alphaFade) ||
			((GlobalPosition - lastGlobalPosition).LengthSquared() > Mathf.Epsilon) ||
			((GlobalBasis.X - lastGlobalBasis.X).LengthSquared() > Mathf.Epsilon) ||
			((GlobalBasis.Y - lastGlobalBasis.Y).LengthSquared() > Mathf.Epsilon) ||
			((GlobalBasis.Z - lastGlobalBasis.Z).LengthSquared() > Mathf.Epsilon))
		{

			for (int i = 0; i < multiMeshDataList.Count; i++)
			{
				if (alphaFade)
					Mesher.UpdateInstanceMultiMesh(multiMeshDataList[i].multiMesh, this, Modulate);
				else
					Mesher.UpdateInstanceMultiMesh(multiMeshDataList[i].multiMesh, this);
			}
		}

		lastGlobalPosition = GlobalPosition;
		lastGlobalBasis = GlobalBasis;	
	}

	public override void _Notification(int what)
	{
		if (what == NotificationPredelete)
			Destroy();
	}
	public void Destroy()
	{
		List<MultiMesh> updateMultiMesh = new List<MultiMesh>();
		for (int i = 0; i < multiMeshDataList.Count; i++)
		{
			MultiMesh multiMesh = multiMeshDataList[i].multiMesh;
			Dictionary<Node3D, int> multiMeshSet;
			if (Mesher.MultiMeshes.TryGetValue(multiMesh, out multiMeshSet))
			{
				if (multiMeshSet.ContainsKey(multiMeshDataList[i].owner))
					multiMeshSet.Remove(multiMeshDataList[i].owner);
			}
			if (!updateMultiMesh.Contains(multiMesh))
				updateMultiMesh.Add(multiMesh);
		}

		//No need to update or detroy other nodes if changing map
		if (GameManager.CurrentState != GameManager.FuncState.Start)
			return;

		foreach (MultiMesh multiMesh in updateMultiMesh)
			Mesher.MultiMeshUpdateInstances(multiMesh);

		foreach (Node3D node in destroyNodes)
		{
			if (IsInstanceValid(node))
				node.QueueFree();
		}

		ModelsManager.RemoveModel(this);
	}
	public void Process(float deltaTime)
	{
		switch (currentState)
		{
			default:
			break;
			case GameManager.FuncState.Ready:
				Start();
			break;
		}

		AnimateModel(deltaTime);
		AnimateTexture(deltaTime);
		UpdateMultiMesh();
		if (alphaFade)
		{
			float alphaValue = Mathf.Lerp(1.0f, 0.0f, destroyTimer / baseTime);
			Modulate = new Color(alphaValue, alphaValue, alphaValue);
		}	
		if (destroyType == DestroyType.DestroyAfterTime)
		{
			destroyTimer -= deltaTime;
			if (destroyTimer < 0)
				QueueFree();
		}
	}
}
