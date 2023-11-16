using Godot;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static MD3GodotConverted;

public static class Mesher
{
	public static Node3D MapMeshes;

	private static List<Vector3> vertsCache = new List<Vector3>();
	private static List<Vector2> uvCache = new List<Vector2>();
	private static List<Vector2> uv2Cache = new List<Vector2>();
	private static List<Vector3> normalsCache = new List<Vector3>();
	private static List<Color> vertsColor = new List<Color>();
	private static List<int> indiciesCache = new List<int>();

	private const int VertexInd = (int)Mesh.ArrayType.Vertex;
	private const int NormalInd = (int)Mesh.ArrayType.Normal;
	private const int TexUVInd = (int)Mesh.ArrayType.TexUV;
	private const int TexUV2Ind = (int)Mesh.ArrayType.TexUV2;
	private const int ColorInd = (int)Mesh.ArrayType.Color;
	private const int TriIndex = (int)Mesh.ArrayType.Index;

	public const float APROX_ERROR = 0.001f;

	public const int LOW_USE_MULTIMESHES = 128;
	public const int HIGH_USE_MULTIMESHES = 1024;

	public const uint MaskSolid = ContentFlags.Solid;
	public const uint MaskPlayerSolid = ContentFlags.Solid | ContentFlags.PlayerClip | ContentFlags.Body;
	public const uint MaskDeadSolid = ContentFlags.Solid | ContentFlags.PlayerClip;
	public const uint MaskWater = ContentFlags.Water | ContentFlags.Lava | ContentFlags.Slime;
	public const uint MaskOpaque = ContentFlags.Solid | ContentFlags.Lava | ContentFlags.Slime;
	public const uint MaskShot = ContentFlags.Solid | ContentFlags.Body | ContentFlags.Corpse;

	public const uint MaskTransparent = SurfaceFlags.NonSolid | SurfaceFlags.Sky;
	public const uint NoMarks = SurfaceFlags.NoImpact | SurfaceFlags.NoMarks;

	public static Dictionary<MultiMesh, List<Node3D>> MultiMeshes = new Dictionary<MultiMesh, List<Node3D>>();
	public static Dictionary<MultiMesh, MultiMeshInstance3D> MultiMeshesInstances = new Dictionary<MultiMesh, MultiMeshInstance3D>();

	public static void ClearMesherCache()
	{
		vertsCache = new List<Vector3>();
		uvCache = new List<Vector2>();
		uv2Cache = new List<Vector2>();
		normalsCache = new List<Vector3>();
		indiciesCache = new List<int>();
		vertsColor = new List<Color>();
		BezierMesh.ClearCaches();
	}

	public static void GenerateBezObject(string textureName, int lmIndex, int indexId, Node3D holder, QSurface[] surfaces, bool addPVS = true)
	{
		if (surfaces == null || surfaces.Length == 0)
			return;

		string Name = "Bezier_Surfaces";
		int[] numPatches = new int[surfaces.Length];
		int totalPatches = 0;
		for (int i = 0; i < surfaces.Length; i++)
		{
			int patches = (surfaces[i].size[0] - 1) / 2 * ((surfaces[i].size[1] - 1) / 2);
			numPatches[i] = patches;
			totalPatches += patches;
			Name += "_" + surfaces[i].surfaceId;
		}

		ShaderMaterial material = MaterialManager.GetMaterials(textureName, lmIndex);
		MeshInstance3D mesh = new MeshInstance3D();
		ArrayMesh arrMesh = new ArrayMesh();
		StaticBody3D collider = new StaticBody3D();
		MapLoader.ColliderGroup.AddChild(collider);
		collider.Name = "Bezier_" + indexId + "_collider";
		uint OwnerShapeId = collider.CreateShapeOwner(holder);

		int offset = 0;
		for (int i = 0; i < surfaces.Length; i++)
		{
			for (int n = 0; n < numPatches[i]; n++)
				GenerateBezMesh(OwnerShapeId, collider, surfaces[i], n, ref offset);
		}

		BezierMesh.FinalizeBezierMesh(arrMesh);
		arrMesh.SurfaceSetMaterial(0, material);
		holder.AddChild(mesh);
		if (addPVS)
			mesh.Layers = GameManager.InvisibleMask;
		else //As dynamic surface don't have bsp data, assign it to the always visible layer 
			mesh.Layers = GameManager.AllPlayerViewMask;
		mesh.Name = Name;
		mesh.Mesh = arrMesh;
//		mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

		//PVS only add on Static Geometry, as it has BSP Nodes
		if (addPVS)
			ClusterPVSManager.Instance.RegisterClusterAndSurfaces(mesh, surfaces);
	}
	public static void GenerateBezMesh(uint OwnerShapeId, CollisionObject3D collider, QSurface surface, int patchNumber, ref int offset)
	{
		//Calculate how many patches there are using size[]
		//There are n_patchesX by n_patchesY patches in the grid, each of those
		//starts at a vert (i,j) in the overall grid
		//We don't actually need to know how many are on the Y length
		//but the forumla is here for historical/academic purposes
		int n_patchesX = (surface.size[0] - 1) / 2;
		//int n_patchesY = ((surface.size[1]) - 1) / 2;


		//Calculate what [n,m] patch we want by using an index
		//called patchNumber  Think of patchNumber as if you 
		//numbered the patches left to right, top to bottom on
		//the grid in a piece of paper.
		int pxStep = 0;
		int pyStep = 0;
		for (int i = 0; i < patchNumber; i++)
		{
			pxStep++;
			if (pxStep == n_patchesX)
			{
				pxStep = 0;
				pyStep++;
			}
		}

		//Create an array the size of the grid, which is given by
		//size[] on the surface object.
		QVertex[,] vertGrid = new QVertex[surface.size[0], surface.size[1]];

		//Read the verts for this surface into the grid, making sure
		//that the final shape of the grid matches the size[] of
		//the surface.
		int gridXstep = 0;
		int gridYstep = 0;
		int vertStep = surface.startVertIndex;
		for (int i = 0; i < surface.numOfVerts; i++)
		{
			vertGrid[gridXstep, gridYstep] = MapLoader.verts[vertStep];
			vertStep++;
			gridXstep++;
			if (gridXstep == surface.size[0])
			{
				gridXstep = 0;
				gridYstep++;
			}
		}

		//We now need to pluck out exactly nine vertexes to pass to our
		//teselate function, so lets calculate the starting vertex of the
		//3x3 grid of nine vertexes that will make up our patch.
		//we already know how many patches are in the grid, which we have
		//as n and m.  There are n by m patches.  Since this method will
		//create one object at a time, we only need to be able to grab
		//one.  The starting vertex will be called vi,vj think of vi,vj as x,y
		//coords into the grid.
		int vi = 2 * pxStep;
		int vj = 2 * pyStep;
		//Now that we have those, we need to get the vert at [vi,vj] and then
		//the two verts at [vi+1,vj] and [vi+2,vj], and then [vi,vj+1], etc.
		//the ending vert will at [vi+2,vj+2]

		int capacity = 3 * 3;
		List<Vector3> bverts = new List<Vector3>(capacity);

		//read texture/lightmap coords while we're at it
		//they will be tessellated as well.
		List<Vector2> uv = new List<Vector2>(capacity);
		List<Vector2> uv2 = new List<Vector2>(capacity);
		List<Color> color = new List<Color>(capacity);

		//Top row
		bverts.Add(vertGrid[vi, vj].position);
		bverts.Add(vertGrid[vi + 1, vj].position);
		bverts.Add(vertGrid[vi + 2, vj].position);

		uv.Add(vertGrid[vi, vj].textureCoord);
		uv.Add(vertGrid[vi + 1, vj].textureCoord);
		uv.Add(vertGrid[vi + 2, vj].textureCoord);

		uv2.Add(vertGrid[vi, vj].lightmapCoord);
		uv2.Add(vertGrid[vi + 1, vj].lightmapCoord);
		uv2.Add(vertGrid[vi + 2, vj].lightmapCoord);

		color.Add(vertGrid[vi, vj].color);
		color.Add(vertGrid[vi + 1, vj].color);
		color.Add(vertGrid[vi + 2, vj].color);

		//Middle row
		bverts.Add(vertGrid[vi, vj + 1].position);
		bverts.Add(vertGrid[vi + 1, vj + 1].position);
		bverts.Add(vertGrid[vi + 2, vj + 1].position);

		uv.Add(vertGrid[vi, vj + 1].textureCoord);
		uv.Add(vertGrid[vi + 1, vj + 1].textureCoord);
		uv.Add(vertGrid[vi + 2, vj + 1].textureCoord);

		uv2.Add(vertGrid[vi, vj + 1].lightmapCoord);
		uv2.Add(vertGrid[vi + 1, vj + 1].lightmapCoord);
		uv2.Add(vertGrid[vi + 2, vj + 1].lightmapCoord);

		color.Add(vertGrid[vi, vj + 1].color);
		color.Add(vertGrid[vi + 1, vj + 1].color);
		color.Add(vertGrid[vi + 2, vj + 1].color);

		//Bottom row
		bverts.Add(vertGrid[vi, vj + 2].position);
		bverts.Add(vertGrid[vi + 1, vj + 2].position);
		bverts.Add(vertGrid[vi + 2, vj + 2].position);

		uv.Add(vertGrid[vi, vj + 2].textureCoord);
		uv.Add(vertGrid[vi + 1, vj + 2].textureCoord);
		uv.Add(vertGrid[vi + 2, vj + 2].textureCoord);

		uv2.Add(vertGrid[vi, vj + 2].lightmapCoord);
		uv2.Add(vertGrid[vi + 1, vj + 2].lightmapCoord);
		uv2.Add(vertGrid[vi + 2, vj + 2].lightmapCoord);

		color.Add(vertGrid[vi, vj + 2].color);
		color.Add(vertGrid[vi + 1, vj + 2].color);
		color.Add(vertGrid[vi + 2, vj + 2].color);

		BezierMesh.GenerateBezierMesh(GameManager.Instance.tessellations, bverts, uv, uv2, color, ref offset);
		BezierMesh.BezierColliderMesh(OwnerShapeId, collider, surface.surfaceId, patchNumber, bverts);

		return;
	}
	public static void GeneratePolygonObject(string textureName, int lmIndex, Node3D holder, QSurface[] surfaces, bool addPVS = true)
	{
		if (surfaces == null || surfaces.Length == 0)
		{
			GD.Print("Failed to create polygon object because there are no surfaces");
			return;
		}

		ShaderMaterial material = MaterialManager.GetMaterials(textureName, lmIndex);

		MeshInstance3D mesh = new MeshInstance3D();
		ArrayMesh arrMesh = new ArrayMesh();
		string Name = "Mesh_Surfaces";
		int offset = 0;
		for (var i = 0; i < surfaces.Length; i++)
		{
			GeneratePolygonMesh(surfaces[i], lmIndex, ref offset);
			Name += "_" + surfaces[i].surfaceId;
		}
		FinalizePolygonMesh(arrMesh);
		arrMesh.SurfaceSetMaterial(0, material);
		holder.AddChild(mesh);
		if (addPVS)
			mesh.Layers = GameManager.InvisibleMask;
		else //As dynamic surface don't have bsp data, assign it to the always visible layer 
			mesh.Layers = GameManager.AllPlayerViewMask;
		mesh.Name = Name;
		mesh.Mesh = arrMesh;
//		mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		//PVS only add on Static Geometry, as it has BSP Nodes
		if (addPVS)
			ClusterPVSManager.Instance.RegisterClusterAndSurfaces(mesh, surfaces);
	}
	public static void GeneratePolygonMesh(QSurface surface, int lm_index, ref int offset)
	{
		if (offset == 0)
		{
			vertsCache.Clear();
			uvCache.Clear();
			uv2Cache.Clear();
			normalsCache.Clear();
			indiciesCache.Clear();
			vertsColor.Clear();
		}

		int vstep = surface.startVertIndex;
		for (int n = 0; n < surface.numOfVerts; n++)
		{
			vertsCache.Add(MapLoader.verts[vstep].position);
			uvCache.Add(MapLoader.verts[vstep].textureCoord);
			uv2Cache.Add(MapLoader.verts[vstep].lightmapCoord);
			normalsCache.Add(MapLoader.verts[vstep].normal);

			//Need to compensate for Color lightning as lightmapped textures will change
			if (lm_index >= 0)
				vertsColor.Add(MapLoader.verts[vstep].color);
			else
				vertsColor.Add(TextureLoader.ChangeColorLighting(MapLoader.verts[vstep].color));
			vstep++;
		}

		// Rip meshverts / triangles
		int mstep = surface.startIndex;
		for (int n = 0; n < surface.numOfIndices; n++)
		{
			indiciesCache.Add(MapLoader.vertIndices[mstep] + offset);
			mstep++;
		}

		offset += surface.numOfVerts;
	}
	public static void FinalizePolygonMesh(ArrayMesh arrMesh)
	{
		var surfaceArray = new Godot.Collections.Array();
		surfaceArray.Resize((int)Mesh.ArrayType.Max);

		// add the verts, uvs, and normals we ripped to the surfaceArray
		surfaceArray[VertexInd] = vertsCache.ToArray();
		surfaceArray[NormalInd] = normalsCache.ToArray();

		// Add the texture co-ords (or UVs) to the surface/mesh
		surfaceArray[TexUVInd] = uvCache.ToArray();
		surfaceArray[TexUV2Ind] = uv2Cache.ToArray();

		// Add the vertex color
		surfaceArray[ColorInd] = vertsColor.ToArray();

		// add the meshverts to the object being built
		surfaceArray[TriIndex] = indiciesCache.ToArray();

		// Create the Mesh.
		arrMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfaceArray);
	}
	public static MD3GodotConverted GenerateModelFromMeshes(MD3 model, Dictionary<string, string> meshToSkin, uint layer = GameManager.AllPlayerViewMask)
	{
		return GenerateModelFromMeshes(model, layer, null, false, false, meshToSkin);
	}
	public static MD3GodotConverted GenerateModelFromMeshes(MD3 model, uint layer, Node3D ownerObject = null, bool forceSkinAlpha = false, bool useCommon = true, Dictionary<string, string> meshToSkin = null, bool useLowMultimeshes = true)
	{
		if (model == null || model.meshes.Count == 0)
		{
			GD.Print("Failed to create model object because there are no meshes");
			return null;
		}

		if (ownerObject == null)
		{
			GD.Print("No ownerObject");
			ownerObject = new Node3D();
			ownerObject.Name = "Model_" + model.name;
		}

		MD3GodotConverted md3Model = new MD3GodotConverted();
		md3Model.node = ownerObject;
		md3Model.numMeshes = model.meshes.Count;
		md3Model.data = new dataMeshes[md3Model.numMeshes];

		int groupId = 0;
		if ((model.numFrames > 1) || (meshToSkin != null))
		{
			foreach (MD3Mesh modelMesh in model.meshes)
			{
				dataMeshes data = new dataMeshes();
				var surfaceArray = GenerateModelMesh(modelMesh);
				data.arrMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfaceArray);

				Node3D modelObject;

				if (groupId == 0)
					modelObject = ownerObject;
				else
				{
					modelObject = new Node3D();
					modelObject.Name = "Mesh_" + groupId;
					ownerObject.AddChild(modelObject);
					modelObject.Position = Vector3.Zero;
				}

				ShaderMaterial material = null;
				string skinName;
				if (meshToSkin == null)
					skinName = modelMesh.skins[0].name;
				else
					skinName = meshToSkin[modelMesh.name];
				bool currentTransparent = forceSkinAlpha;
				material = MaterialManager.GetMaterials(skinName, -1, ref currentTransparent);

				data.arrMesh.SurfaceSetMaterial(0, material);
				data.meshDataTool.CreateFromSurface(data.arrMesh, 0);
				md3Model.data[modelMesh.meshNum] = data;
				model.readySurfaceArray.Add(surfaceArray);
				MultiMesh multiMesh = new MultiMesh();
				data.multiMesh = multiMesh;
				multiMesh.Mesh = data.arrMesh;
				multiMesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
				if (useLowMultimeshes)
					multiMesh.InstanceCount = LOW_USE_MULTIMESHES;
				else
					multiMesh.InstanceCount = HIGH_USE_MULTIMESHES;
				model.commonMesh.Add(multiMesh);
				model.useTransparent.Add(currentTransparent);
				model.readyMaterials.Add(material);
				if (!MultiMeshes.ContainsKey(multiMesh))
				{
					List<Node3D> list = new List<Node3D> ();
					MultiMeshes.Add(multiMesh, list);
				}

				if (useCommon && !currentTransparent)
				{
					MultiMeshInstance3D mesh = new MultiMeshInstance3D();
					mesh.Name = modelMesh.name;
					mesh.Multimesh = multiMesh;
					mesh.Layers = layer;
					mesh.SetInstanceShaderParameter("OffSetTime", GameManager.CurrentTimeMsec);
					GameManager.Instance.TemporaryObjectsHolder.AddChild(mesh);
					MultiMeshesInstances.Add(multiMesh, mesh);
					GD.Print("Adding MultiMesh : " + mesh.Name);
				}
				else
				{
					MeshInstance3D mesh = new MeshInstance3D();
					mesh.Name = modelMesh.name;
					mesh.Mesh = data.arrMesh;
					mesh.Layers = layer;
					mesh.SetInstanceShaderParameter("OffSetTime", GameManager.CurrentTimeMsec);
					modelObject.AddChild(mesh);
					GD.Print("Adding Child: " + mesh.Name + " to: " + modelObject.Name);
				}
				groupId++;
			}
		}
		else
		{
			var baseGroups = model.meshes.GroupBy(x => new { x.numSkins });
			foreach (var baseGroup in baseGroups)
			{
				MD3Mesh[] baseGroupMeshes = baseGroup.ToArray();
				if (baseGroupMeshes.Length == 0)
					continue;

				var groupMeshes = baseGroupMeshes.GroupBy(x => new { x.skins[0].name });
				foreach (var groupMesh in groupMeshes)
				{
					MD3Mesh[] meshes = groupMesh.ToArray();
					if (meshes.Length == 0)
						continue;

					string Name = "Mesh_";
					int offset = 0;
					for (int i = 0; i < meshes.Length; i++)
					{
						GenerateModelMesh(meshes[i],ref offset);
						if (i != 0)
							Name += "_";
						Name += meshes[i].name;
					}

					dataMeshes data = new dataMeshes();
					var surfaceArray = FinalizeModelMesh();
					data.arrMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfaceArray);

					Node3D modelObject;
					if (groupId == 0)
						modelObject = ownerObject;
					else
					{
						modelObject = new Node3D();
						modelObject.Name = "Mesh_" + groupId;
						ownerObject.AddChild(modelObject);
						modelObject.Position = Vector3.Zero;
					}
					bool currentTransparent = forceSkinAlpha;
					ShaderMaterial material = MaterialManager.GetMaterials(meshes[0].skins[0].name, -1, ref currentTransparent);

					for (int i = 0; i < meshes.Length; i++)
						md3Model.data[meshes[i].meshNum] = data;

					data.arrMesh.SurfaceSetMaterial(0, material);
					data.meshDataTool.CreateFromSurface(data.arrMesh, 0);
					model.readySurfaceArray.Add(surfaceArray);
					MultiMesh multiMesh = new MultiMesh();
					data.multiMesh = multiMesh;
					multiMesh.Mesh = data.arrMesh;
					multiMesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
					if (useLowMultimeshes)
						multiMesh.InstanceCount = LOW_USE_MULTIMESHES;
					else
						multiMesh.InstanceCount = HIGH_USE_MULTIMESHES;
					model.commonMesh.Add(multiMesh);
					model.useTransparent.Add(currentTransparent);
					model.readyMaterials.Add(material);
					if (!MultiMeshes.ContainsKey(multiMesh))
					{
						List<Node3D> list = new List<Node3D>();
						MultiMeshes.Add(multiMesh, list);
					}

					if (useCommon && !currentTransparent)
					{
						MultiMeshInstance3D mesh = new MultiMeshInstance3D();
						mesh.Name = Name;
						mesh.Multimesh = multiMesh;
						mesh.Layers = layer;
						mesh.SetInstanceShaderParameter("OffSetTime", GameManager.CurrentTimeMsec);
						GameManager.Instance.TemporaryObjectsHolder.AddChild(mesh);
						MultiMeshesInstances.Add(multiMesh, mesh);
						GD.Print("Adding MultiMesh : " + mesh.Name + " skin group name " + meshes[0].skins[0].name);
					}
					else
					{
						MeshInstance3D mesh = new MeshInstance3D();
						mesh.Name = Name;
						mesh.Mesh = data.arrMesh;
						mesh.Layers = layer;
						mesh.SetInstanceShaderParameter("OffSetTime", GameManager.CurrentTimeMsec);
						modelObject.AddChild(mesh);
						GD.Print("Adding Child: " + mesh.Name + " to: " + modelObject.Name + " skin group name " + meshes[0].skins[0].name);
					}
					groupId++;
				}
			}
		}
		return md3Model;
	}
	
	public static MD3GodotConverted FillModelFromProcessedData(MD3 model, Dictionary<string, string> meshToSkin, uint layer = GameManager.AllPlayerViewMask)
	{
		return FillModelFromProcessedData(model, layer, null, false, meshToSkin);
	}

	public static MD3GodotConverted FillModelFromProcessedData(MD3 model, uint layer, Node3D ownerObject = null, bool useCommon = true, Dictionary<string, string> meshToSkin = null)
	{
		if (ownerObject == null)
		{
			ownerObject = new Node3D();
			ownerObject.Name = "Model_" + model.name;
		}

		MD3GodotConverted md3Model = new MD3GodotConverted();
		md3Model.node = ownerObject;
		md3Model.numMeshes = model.meshes.Count;
		md3Model.data = new dataMeshes[md3Model.numMeshes];

		for (int i = 0; i < model.readySurfaceArray.Count; i++)
		{
			Node3D modelObject;
			if (i == 0)
				modelObject = ownerObject;
			else
			{
				modelObject = new Node3D();
				modelObject.Name = "Mesh_" + i;
				ownerObject.AddChild(modelObject);
				modelObject.Position = Vector3.Zero;
			}

			md3Model.data[model.meshes[i].meshNum] = new dataMeshes();
			bool useTransparent = model.useTransparent[i];
			md3Model.data[model.meshes[i].meshNum].isTransparent = useTransparent;
			if (useCommon && !useTransparent)
			{
				md3Model.data[model.meshes[i].meshNum].multiMesh = model.commonMesh[i];
				if (!MultiMeshesInstances.ContainsKey(model.commonMesh[i]))
				{
					MultiMeshInstance3D mesh = new MultiMeshInstance3D();
					mesh.Multimesh = model.commonMesh[i];
					mesh.Layers = layer;
					GameManager.Instance.TemporaryObjectsHolder.AddChild(mesh);
					MultiMeshesInstances.Add(model.commonMesh[i], mesh);
					mesh.SetInstanceShaderParameter("OffSetTime", GameManager.CurrentTimeMsec);
				}
			}
			else
			{
				MeshInstance3D mesh = new MeshInstance3D();
				var surfaceArray = model.readySurfaceArray[i];
				md3Model.data[model.meshes[i].meshNum].arrMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfaceArray);
				md3Model.data[model.meshes[i].meshNum].arrMesh.SurfaceSetMaterial(0, model.readyMaterials[i]);
				md3Model.data[model.meshes[i].meshNum].meshDataTool.CreateFromSurface(md3Model.data[model.meshes[i].meshNum].arrMesh, 0);
				mesh.Mesh = md3Model.data[model.meshes[i].meshNum].arrMesh;
				mesh.Layers = layer;
				modelObject.AddChild(mesh);
				mesh.SetInstanceShaderParameter("OffSetTime", GameManager.CurrentTimeMsec);
			}			
		}
		return md3Model;
	}

	public static Godot.Collections.Array GenerateModelMesh(MD3Mesh md3Mesh)
	{
		if (md3Mesh == null)
		{
			GD.Print("Failed to generate polygon mesh because there are no meshe info");
			return null;
		}

		var surfaceArray = new Godot.Collections.Array();
		surfaceArray.Resize((int)Mesh.ArrayType.Max);

		List<int> Triangles = new List<int>();

		for (int i = 0; i < md3Mesh.triangles.Count; i++)
		{
			Triangles.Add(md3Mesh.triangles[i].vertex1);
			Triangles.Add(md3Mesh.triangles[i].vertex2);
			Triangles.Add(md3Mesh.triangles[i].vertex3);
		}

		// add the verts
		surfaceArray[VertexInd] = md3Mesh.verts[0].ToArray();

		// add normals
		surfaceArray[NormalInd] = md3Mesh.normals[0].ToArray();

		// Add the texture co-ords (or UVs) to the surface/mesh
		surfaceArray[TexUVInd] = md3Mesh.texCoords.ToArray();

		// add the meshverts to the object being built
		surfaceArray[TriIndex] = Triangles.ToArray();

		return surfaceArray;
	}

	public static void GenerateModelMesh(MD3Mesh md3Mesh, ref int offset)
	{
		if (offset == 0)
		{
			vertsCache.Clear();
			uvCache.Clear();
			uv2Cache.Clear();
			normalsCache.Clear();
			indiciesCache.Clear();
			vertsColor.Clear();
		}

		vertsCache.AddRange(md3Mesh.verts[0].ToArray());
		normalsCache.AddRange(md3Mesh.normals[0].ToArray());
		uvCache.AddRange(md3Mesh.texCoords.ToArray());

		// Rip meshverts / triangles
		for (int i = 0; i < md3Mesh.triangles.Count; i++)
		{
			indiciesCache.Add(md3Mesh.triangles[i].vertex1 + offset);
			indiciesCache.Add(md3Mesh.triangles[i].vertex2 + offset);
			indiciesCache.Add(md3Mesh.triangles[i].vertex3 + offset);
		}
		offset += md3Mesh.verts[0].Count;
	}
	public static Godot.Collections.Array FinalizeModelMesh()
	{
		var surfaceArray = new Godot.Collections.Array();
		surfaceArray.Resize((int)Mesh.ArrayType.Max);

		// add the verts, uvs, and normals we ripped to the surfaceArray
		surfaceArray[VertexInd] = vertsCache.ToArray();
		surfaceArray[NormalInd] = normalsCache.ToArray();

		// Add the texture co-ords (or UVs) to the surface/mesh
		surfaceArray[TexUVInd] = uvCache.ToArray();

		// add the meshverts to the object being built
		surfaceArray[TriIndex] = indiciesCache.ToArray();

		return surfaceArray;
	}

	public static uint GenerateGroupBrushCollider(int indexId, Node3D holder, QBrush[] brushes, CollisionObject3D objCollider = null, uint extraContentFlag = 0)
	{
		bool isTrigger = false;
		uint type = MapLoader.mapTextures[brushes[0].shaderId].contentsFlags;
		uint stype = MapLoader.mapTextures[brushes[0].shaderId].surfaceFlags;
		if (((type & ContentFlags.Details) != 0) || ((type & ContentFlags.Structural) != 0))
		{
			GD.Print("brushes: " + indexId + " Not used for collisions, Content Type is: " + type);
			return 0;
		}

/*		if ((stype & SurfaceFlags.NonSolid) != 0)
		{
			GD.Print("brushes: " + indexId + " Is not solid, Surface Type is: " + stype);
			return;
		}
*/		ContentType contentType = new ContentType();
		contentType.Init(type | extraContentFlag);

		if ((contentType.value & MaskPlayerSolid) == 0)
			isTrigger = true;

		if (objCollider == null)
		{
			if (isTrigger)
				objCollider = new Area3D();
			else
				objCollider = new StaticBody3D();

			objCollider.Name = "Polygon_" + indexId + "_collider";
			holder.AddChild(objCollider);
		}
		objCollider.AddChild(contentType);

		uint OwnerShapeId = objCollider.CreateShapeOwner(holder);
		for (int i = 0; i < brushes.Length; i++)
		{
			ConvexPolygonShape3D convexHull = GenerateBrushCollider(brushes[i]);
			if (convexHull != null)
				objCollider.ShapeOwnerAddShape(OwnerShapeId, convexHull);
		}
		
		SurfaceType surfaceType = new SurfaceType();
		surfaceType.Init(stype);

		if ((surfaceType.value & MaskTransparent) != 0)
			objCollider.CollisionLayer = (1 << GameManager.InvisibleBlockerLayer);
		else
			objCollider.CollisionLayer = (1 << GameManager.ColliderLayer);

		//If noMarks add it to the table
		if ((surfaceType.value & NoMarks) != 0)
			MapLoader.noMarks.Add(objCollider);

		objCollider.CollisionMask = GameManager.TakeDamageMask | (1 << GameManager.RagdollLayer);
		objCollider.AddChild(surfaceType);

		return OwnerShapeId;
	}
	public static ConvexPolygonShape3D GenerateBrushCollider(QBrush brush)
	{
		List<Vector3> possibleIntersectPoint = new List<Vector3>();
		List<Vector3> intersectPoint = new List<Vector3>();

		for (int i = 0; i < brush.numOfBrushSides; i++)
		{
			int planeIndex = MapLoader.brushSides[brush.brushSide + i].plane;
			QPlane p1 = MapLoader.planes[planeIndex];

			for (int j = i + 1; j < brush.numOfBrushSides; j++)
			{
				planeIndex = MapLoader.brushSides[brush.brushSide + j].plane;
				QPlane p2 = MapLoader.planes[planeIndex];
				for (int k = j + 1; k < brush.numOfBrushSides; k++)
				{
					planeIndex = MapLoader.brushSides[brush.brushSide + k].plane;
					QPlane p3 = MapLoader.planes[planeIndex];
					List<float> intersect = p1.IntersectPlanes(p2, p3);
					if (intersect != null)
						possibleIntersectPoint.Add(new Vector3(intersect[0], intersect[1], intersect[2]));
				}
			}
		}

		for (int i = 0; i < possibleIntersectPoint.Count; i++)
		{
			bool inside = true;
			for (int j = 0; j < brush.numOfBrushSides; j++)
			{
				int planeIndex = MapLoader.brushSides[brush.brushSide + j].plane;
				QPlane plane = MapLoader.planes[planeIndex];
				if (plane.GetSide(possibleIntersectPoint[i], QPlane.CheckPointPlane.IsFront))
				{
					inside = false;
					j = brush.numOfBrushSides;
				}
			}
			if (inside)
			{
				if (!intersectPoint.Contains(possibleIntersectPoint[i]))
					intersectPoint.Add(possibleIntersectPoint[i]);
			}
		}

		intersectPoint = RemoveDuplicatedVectors(intersectPoint);
		Vector3 normal = Vector3.Zero;
		if (!CanForm3DConvexHull(intersectPoint, ref normal))
		{
			GD.Print("Cannot Form 3DConvexHull " + brush.brushSide + " this was a waste of time");
			return null;
		}

		ConvexPolygonShape3D convexHull = new ConvexPolygonShape3D();
		convexHull.Points = intersectPoint.ToArray();

		return convexHull;
	}
	public static bool GenerateBrushCollider(QBrush brush, Node3D holder, CollisionObject3D objCollider = null, bool addRigidBody = false, uint extraContentFlag = 0)
	{
		bool isTrigger = false;
		//Remove brushed used for BSP Generations and for Details
		uint type = MapLoader.mapTextures[brush.shaderId].contentsFlags;

		if (((type & ContentFlags.Details) != 0) || ((type & ContentFlags.Structural) != 0))
		{
//			GD.Print("brushSide: " + brush.brushSide + " Not used for collisions, Content Type is: " + type);
			return false;
		}

		List<Vector3> possibleIntersectPoint = new List<Vector3>();
		List<Vector3> intersectPoint = new List<Vector3>();
		for (int i = 0; i < brush.numOfBrushSides; i++)
		{
			int planeIndex = MapLoader.brushSides[brush.brushSide + i].plane;
			QPlane p1 = MapLoader.planes[planeIndex];

			for (int j = i + 1; j < brush.numOfBrushSides; j++)
			{
				planeIndex = MapLoader.brushSides[brush.brushSide + j].plane;
				QPlane p2 = MapLoader.planes[planeIndex];
				for (int k = j + 1; k < brush.numOfBrushSides; k++)
				{
					planeIndex = MapLoader.brushSides[brush.brushSide + k].plane;
					QPlane p3 = MapLoader.planes[planeIndex];
					List<float> intersect = p1.IntersectPlanes(p2, p3);
					if (intersect != null)
						possibleIntersectPoint.Add(new Vector3(intersect[0], intersect[1], intersect[2]));
				}
			}
		}

		for (int i = 0; i < possibleIntersectPoint.Count; i++)
		{
			bool inside = true;
			for (int j = 0; j < brush.numOfBrushSides; j++)
			{
				int planeIndex = MapLoader.brushSides[brush.brushSide + j].plane;
				QPlane plane = MapLoader.planes[planeIndex];
				if (plane.GetSide(possibleIntersectPoint[i], QPlane.CheckPointPlane.IsFront))
				{
					inside = false;
					j = brush.numOfBrushSides;
				}
			}
			if (inside)
			{
				if (!intersectPoint.Contains(possibleIntersectPoint[i]))
					intersectPoint.Add(possibleIntersectPoint[i]);
			}
		}

		intersectPoint = RemoveDuplicatedVectors(intersectPoint);
		Vector3 normal = Vector3.Zero;
		if (!CanForm3DConvexHull(intersectPoint, ref normal))
		{
			GD.Print("Cannot Form 3DConvexHull " + brush.brushSide + " this was a waste of time");
			return false;
		}

		ContentType contentType = new ContentType();
		contentType.Init(type | extraContentFlag);

		if ((contentType.value & MaskPlayerSolid) == 0)
			isTrigger = true;

		if (objCollider == null)
		{
			if (isTrigger)
				objCollider = new Area3D();
			else if (addRigidBody)
				objCollider = new AnimatableBody3D();
			else
				objCollider = new StaticBody3D();
			objCollider.Name = "Polygon_" + brush.brushSide + "_collider";
			holder.AddChild(objCollider);
		}
		objCollider.CollisionLayer = (1 << GameManager.ColliderLayer);
		objCollider.CollisionMask = GameManager.TakeDamageMask | (1 << GameManager.RagdollLayer);

		CollisionShape3D mc = new CollisionShape3D();
		mc.Name = "brushSide: " + brush.brushSide;
		objCollider.AddChild(mc);
		objCollider.AddChild(contentType);

		ConvexPolygonShape3D convexHull = new ConvexPolygonShape3D();
		convexHull.Points = intersectPoint.ToArray();
		mc.Shape = convexHull;

//		if ((contentType.value & ContentFlags.PlayerClip) == 0)
//			objCollider.layer = GameManager.InvisibleBlockerLayer;

		type = MapLoader.mapTextures[brush.shaderId].surfaceFlags;
		SurfaceType surfaceType = new SurfaceType();
		surfaceType.Init(type);
		objCollider.AddChild(surfaceType);

//		if ((surfaceType.value & NoMarks) != 0)
//			MapLoader.noMarks.Add(mc);

		if ((surfaceType.value & MaskTransparent) != 0)
			objCollider.CollisionLayer = (1 << GameManager.InvisibleBlockerLayer);

//		if ((type & SurfaceFlags.NonSolid) != 0)
//			GD.Print("brushSide: " + brush.brushSide + " Surface Type is: " + type);

		return true;
	}
	public static bool CanForm3DConvexHull(List<Vector3> points, ref Vector3 normal)
	{
		const float EPSILON = 0.00001f;
		int i;
		bool retry = false;

		if (points.Count < 4)
			return false;

		// Calculate a normal vector
		tryagain:
		for (i = 0; i < points.Count; i++)
		{
			Vector3 v1 = points[1] - points[i];
			Vector3 v2 = points[2] - points[i];
			normal = v1.Cross(v2);

			// check that v1 and v2 were NOT collinear
			if (normal.LengthSquared() > 0)
				break;
			if (i == 0)
				i = 2;
		}

		//Check if we got a normal
		if (i == points.Count)
		{
			if (retry)
				return false;

			retry = true;
			points = RemoveDuplicatedVectors(points);
			goto tryagain;
		}

		// Check if all points lie on the plane
		for (i = 0; i < points.Count; i++)
		{
			Vector3 px = points[i] - points[0];
			float dotProduct = px.Dot(normal);

			if (Mathf.Abs(dotProduct) > EPSILON)
				return true;
		}
		
		normal = normal.Normalized();

		return false;
	}

	public static bool CanForm2DConvexHull(List<Vector2> points)
	{
		const float EPSILON = 0.001f;

		Vector2 min = points[0];
		Vector2 max = points[0];

		if (points.Count < 3)
			return false;

		for (int i = 1; i < points.Count; i++)
		{
			Vector2 p = points[i];

			if (p.X < min.X)
				min.X = p.X;
			else if (p.X > max.X)
				max.X = p.X;

			if (p.Y < min.Y)
				min.Y = p.Y;
			else if (p.Y > max.Y)
				max.Y = p.Y;
		}

		float xWidth = Mathf.Abs(max.X - min.X);
		float yWidth = Mathf.Abs(max.Y - min.Y);

		if ((xWidth < EPSILON) || (yWidth < EPSILON))
			return false;

		return true;
	}

	public static Vector3[] GetExtrudedVerticesFromPoints(Vector3[] points, Vector3 normal)
	{
		Vector3[] vertices = new Vector3[points.Length * 2];
		float depth = 0.001f;

		for (int i = 0; i < points.Length; i++)
		{
			vertices[i].X = points[i].X;
			vertices[i].Y = points[i].Y;
			vertices[i].Z = points[i].Z;
			vertices[i + points.Length].X = points[i].X - depth * normal.X;
			vertices[i + points.Length].Y = points[i].Y - depth * normal.Y;
			vertices[i + points.Length].Z = points[i].Z + depth * normal.Z;
		}

		return vertices;
	}
	public static List<Vector3> RemoveDuplicatedVectors(List<Vector3> test)
	{
		List<Vector3> uniqueVector = new List<Vector3>();
		for (int i = 0; i < test.Count; i++)
		{
			bool isUnique = true;
			for (int j = i + 1; j < test.Count; j++)
			{
				if (FloatAprox(test[i].X, test[j].X))
					if (FloatAprox(test[i].Y, test[j].Y))
						if (FloatAprox(test[i].Z, test[j].Z))
							isUnique = false;
			}
			if (isUnique)
				uniqueVector.Add(new Vector3(RoundUp4Decimals(test[i].X), RoundUp4Decimals(test[i].Y), RoundUp4Decimals(test[i].Z)));
		}
		return uniqueVector;
	}
	public static bool FloatAprox(float f1, float f2)
	{
		float d = f1 - f2;

		if (d < -APROX_ERROR || d > APROX_ERROR)
			return false;
		return true;
	}
	public static float RoundUp4Decimals(float f)
	{
		float d = Mathf.CeilToInt(f * 10000) / 10000.0f;
		return d;
	}

	public static void AddNodeToMultiMeshes(MultiMesh multiMesh, Node3D owner)
	{
		int instanceNum;
		if (MultiMeshes.ContainsKey(multiMesh))
		{
			List<Node3D> multiMeshList = MultiMeshes[multiMesh];
			instanceNum = multiMeshList.Count;
/*			int threshold = (multiMesh.InstanceCount >> 1);
			if (instanceNum > threshold)
			{
				DestroyAfterTime destroy = new DestroyAfterTime();
				destroy.parent = owner.GetParent();
				multiMeshList[0].AddChild(destroy);
			}
*/
			if (multiMeshList.Contains(owner))
				return;

			multiMeshList.Add(owner);
			multiMesh.SetInstanceTransform(instanceNum, owner.GlobalTransform);
			multiMesh.VisibleInstanceCount = instanceNum + 1;
		}
	}
	public static void UpdateInstanceMultiMesh(MultiMesh multiMesh, Node3D owner)
	{
		if (MultiMeshes.ContainsKey(multiMesh))
		{
			List<Node3D> multiMeshList = MultiMeshes[multiMesh];
			if (!multiMeshList.Contains(owner))
				return;

			int index = multiMeshList.IndexOf(owner);
			if (owner.IsVisibleInTree())
				multiMesh.SetInstanceTransform(index, owner.GlobalTransform);
			else //Move it out of the map
			{
				Transform3D min = new Transform3D(owner.Basis, MapLoader.mapMinCoord * 2f);
				multiMesh.SetInstanceTransform(index, min);
			}
		}
	}
	public static void MultiMeshUpdateInstances(MultiMesh multiMesh)
	{
		if (MultiMeshes.ContainsKey(multiMesh))
		{
			List<Node3D> multiMeshList = MultiMeshes[multiMesh];
			for (int i = 0 ; i < multiMeshList.Count; i++)
				multiMesh.SetInstanceTransform(i, multiMeshList[i].Transform);
			multiMesh.VisibleInstanceCount = multiMeshList.Count;
		}
	}
}

