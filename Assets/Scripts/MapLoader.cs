using Godot;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
public static class MapLoader
{
	public static string CurrentMap;

	private static BinaryReader BSPMap;

	public static BSPHeader header;

	public static List<QSurface> surfaces;
	public static List<ImageTexture> lightMaps;
	public static List<QVertex> verts;
	public static List<int> vertIndices;
	public static List<QPlane> planes;
	public static List<QNode> nodes;
	public static List<QLeaf> leafs;
	public static List<int> leafsSurfaces;
	public static int[] leafRenderFrameLayer;
	public static List<QModel> models;
	public static List<QBrush> brushes;
	public static List<int> leafsBrushes;
	public static List<QBrushSide> brushSides;
	public static List<QShader> mapTextures;
	public static QVisData visData;

	public static LightMapSize currentLightMapSize = LightMapSize.Q3_QL;

	public static Node3D MapMesh;
	public static Node3D ColliderGroup;

	public static int MAX_MESH_SURFACES = 256;
	public enum LightMapSize
	{
		Q3_QL = 128,
		QAA = 512
	}
	public enum LightMapLenght
	{
		Q3_QL = 49152,      //128*128*3
		QAA = 786432        //512*512*3
	}
	public static bool Load(string mapName)
	{

		string path = Directory.GetCurrentDirectory() + "/StreamingAssets/maps/" + mapName + ".bsp";
		if (File.Exists(path))
			BSPMap = new BinaryReader(File.Open(path, FileMode.Open));
		else if (PakManager.ZipFiles.ContainsKey(path = ("maps/" + mapName + ".bsp").ToUpper()))
		{
			string FileName = PakManager.ZipFiles[path];
			var reader = new ZipReader();
			reader.Open(FileName);
			MemoryStream ms = new MemoryStream(reader.ReadFile(path, false));
			BSPMap = new BinaryReader(ms);
		}
		else
			return false;

		//header
		{
			header = new BSPHeader(BSPMap);
		}

		//entities
		{
			BSPMap.BaseStream.Seek(header.Directory[LumpType.Entities].Offset, SeekOrigin.Begin);
		}

		QShaderManager.ProcessShaders();
		//shaders (textures)
		{
			BSPMap.BaseStream.Seek(header.Directory[LumpType.Shaders].Offset, SeekOrigin.Begin);
			int num = header.Directory[LumpType.Shaders].Length / 72;
			mapTextures = new List<QShader>(num);
			GD.Print("mapTextures " + num);
			for (int i = 0; i < num; i++)
			{
				mapTextures.Add(new QShader(new string(BSPMap.ReadChars(64)), BSPMap.ReadUInt32(), BSPMap.ReadUInt32(), false));
			}
		}

		//planes
		{
			BSPMap.BaseStream.Seek(header.Directory[LumpType.Planes].Offset, SeekOrigin.Begin);
			int num = header.Directory[LumpType.Planes].Length / 16;
			planes = new List<QPlane>(num);
			GD.Print("planes " + num);
			for (int i = 0; i < num; i++)
			{
				planes.Add(new QPlane(new Vector3(BSPMap.ReadSingle(), BSPMap.ReadSingle(), BSPMap.ReadSingle()), BSPMap.ReadSingle()));
			}
		}

		//nodes
		{
			BSPMap.BaseStream.Seek(header.Directory[LumpType.Nodes].Offset, SeekOrigin.Begin);
			int num = header.Directory[LumpType.Nodes].Length / 36;
			nodes = new List<QNode>(num);
			GD.Print("nodes " + num);
			for (int i = 0; i < num; i++)
			{
				nodes.Add(new QNode(BSPMap.ReadInt32(), BSPMap.ReadInt32(), BSPMap.ReadInt32(), new Vector3I(BSPMap.ReadInt32(), BSPMap.ReadInt32(), BSPMap.ReadInt32()), new Vector3I(BSPMap.ReadInt32(), BSPMap.ReadInt32(), BSPMap.ReadInt32())));
			}
		}

		//leafs
		{
			BSPMap.BaseStream.Seek(header.Directory[LumpType.Leafs].Offset, SeekOrigin.Begin);
			int num = header.Directory[LumpType.Leafs].Length / 48;
			leafs = new List<QLeaf>(num);
			GD.Print("leafs " + num);
			for (int i = 0; i < num; i++)
			{
				leafs.Add(new QLeaf(BSPMap.ReadInt32(), BSPMap.ReadInt32(), new Vector3I(BSPMap.ReadInt32(), BSPMap.ReadInt32(), BSPMap.ReadInt32()), new Vector3I(BSPMap.ReadInt32(), BSPMap.ReadInt32(), BSPMap.ReadInt32()), BSPMap.ReadInt32(), BSPMap.ReadInt32(), BSPMap.ReadInt32(), BSPMap.ReadInt32()));
			}
		}

		//leafs faces
		{
			BSPMap.BaseStream.Seek(header.Directory[LumpType.LeafSurfaces].Offset, SeekOrigin.Begin);
			int num = header.Directory[LumpType.LeafSurfaces].Length / 4;
			leafsSurfaces = new List<int>(num);
			leafRenderFrameLayer = new int[num];
			GD.Print("leafsSurfaces " + num);
			for (int i = 0; i < num; i++)
			{
				leafsSurfaces.Add(BSPMap.ReadInt32());
			}
		}

		//leafs brushes
		{
			BSPMap.BaseStream.Seek(header.Directory[LumpType.LeafBrushes].Offset, SeekOrigin.Begin);
			int num = header.Directory[LumpType.LeafBrushes].Length / 4;
			leafsBrushes = new List<int>(num);
			GD.Print("leafsBrushes " + num);
			for (int i = 0; i < num; i++)
			{
				leafsBrushes.Add(BSPMap.ReadInt32());
			}
		}

		//models (map geometry)
		{
			BSPMap.BaseStream.Seek(header.Directory[LumpType.Models].Offset, SeekOrigin.Begin);
			int num = header.Directory[LumpType.Models].Length / 40;
			models = new List<QModel>(num);
			GD.Print("map geometry " + num);
			for (int i = 0; i < num; i++)
			{
				models.Add(new QModel(new Vector3(BSPMap.ReadInt32(), BSPMap.ReadInt32(), BSPMap.ReadInt32()),
										new Vector3(BSPMap.ReadInt32(), BSPMap.ReadInt32(), BSPMap.ReadInt32()),
										BSPMap.ReadInt32(), BSPMap.ReadInt32(), BSPMap.ReadInt32(), BSPMap.ReadInt32()));
			}
		}

		//brushes
		{
			BSPMap.BaseStream.Seek(header.Directory[LumpType.Brushes].Offset, SeekOrigin.Begin);
			int num = header.Directory[LumpType.Brushes].Length / 12;
			brushes = new List<QBrush>(num);
			GD.Print("brushes " + num);
			for (int i = 0; i < num; i++)
			{
				brushes.Add(new QBrush(BSPMap.ReadInt32(), BSPMap.ReadInt32(), BSPMap.ReadInt32()));
			}
		}

		//brush sides
		{
			BSPMap.BaseStream.Seek(header.Directory[LumpType.BrushSides].Offset, SeekOrigin.Begin);
			int num = header.Directory[LumpType.BrushSides].Length / 8;
			brushSides = new List<QBrushSide>(num);
			GD.Print("brushSides " + num);
			for (int i = 0; i < num; i++)
			{
				brushSides.Add(new QBrushSide(BSPMap.ReadInt32(), BSPMap.ReadInt32()));
			}
		}

		//vertices
		{
			BSPMap.BaseStream.Seek(header.Directory[LumpType.Vertexes].Offset, SeekOrigin.Begin);
			int num = header.Directory[LumpType.Vertexes].Length / 44;
			verts = new List<QVertex>(num);
			GD.Print("vertices " + num);
			for (int i = 0; i < num; i++)
			{
				verts.Add(new QVertex(i, new Vector3(BSPMap.ReadSingle(), BSPMap.ReadSingle(), BSPMap.ReadSingle()),
													BSPMap.ReadSingle(), BSPMap.ReadSingle(), BSPMap.ReadSingle(), BSPMap.ReadSingle(),
													new Vector3(BSPMap.ReadSingle(), BSPMap.ReadSingle(), BSPMap.ReadSingle()), BSPMap.ReadBytes(4)));
			}
		}

		//vertex indices
		{
			BSPMap.BaseStream.Seek(header.Directory[LumpType.VertIndices].Offset, SeekOrigin.Begin);
			int num = header.Directory[LumpType.VertIndices].Length / 4;
			vertIndices = new List<int>(num);
			GD.Print("vertIndices " + num);
			for (int i = 0; i < num; i++)
			{
				vertIndices.Add(BSPMap.ReadInt32());
			}
		}

		//We need to determine the max number in order to check lightmap type
		int maxlightMapNum = 0;
		//surfaces
		{
			BSPMap.BaseStream.Seek(header.Directory[LumpType.Surfaces].Offset, SeekOrigin.Begin);
			int num = header.Directory[LumpType.Surfaces].Length / 104;
			surfaces = new List<QSurface>(num);
			GD.Print("surfaces " + num);
			for (int i = 0; i < num; i++)
			{
				surfaces.Add(new QSurface(i, BSPMap.ReadInt32(), BSPMap.ReadInt32(), BSPMap.ReadInt32(), BSPMap.ReadInt32(),
					BSPMap.ReadInt32(), BSPMap.ReadInt32(), BSPMap.ReadInt32(), BSPMap.ReadInt32(), new[]
					{
						BSPMap.ReadInt32(),
						BSPMap.ReadInt32()
					}, new[]
					{
						BSPMap.ReadInt32(),
						BSPMap.ReadInt32()
					}, new Vector3(BSPMap.ReadSingle(), BSPMap.ReadSingle(), BSPMap.ReadSingle()), new[]
					{
						new Vector3(BSPMap.ReadSingle(), BSPMap.ReadSingle(), BSPMap.ReadSingle()),
						new Vector3(BSPMap.ReadSingle(), BSPMap.ReadSingle(), BSPMap.ReadSingle())
					}, new Vector3(BSPMap.ReadSingle(), BSPMap.ReadSingle(), BSPMap.ReadSingle()), new[]
					{
						BSPMap.ReadInt32(),
						BSPMap.ReadInt32()
					}));

				if (surfaces[i].lightMapID > maxlightMapNum)
					maxlightMapNum = surfaces[i].lightMapID;
			}
			//Need to count lightmap 0
			maxlightMapNum++;
		}

		//Q3/QL lightmaps (128x128x3)
		//QAA lightmaps (512x512x3)
		{
			//Check lightmap type
			int lightMapLenght = (int)LightMapLenght.QAA;
			if ((maxlightMapNum * lightMapLenght) > header.Directory[LumpType.LightMaps].Length)
				lightMapLenght = (int)LightMapLenght.Q3_QL;
			else
				currentLightMapSize = LightMapSize.QAA;

			BSPMap.BaseStream.Seek(header.Directory[LumpType.LightMaps].Offset, SeekOrigin.Begin);
			int num = header.Directory[LumpType.LightMaps].Length / lightMapLenght;
			lightMaps = new List<ImageTexture>(num);
			GD.Print("lightMaps " + num);
			for (int i = 0; i < num; i++)
			{
				lightMaps.Add(TextureLoader.CreateLightmapTexture(BSPMap.ReadBytes(lightMapLenght)));
			}
		}

		//vis data
		{
			BSPMap.BaseStream.Seek(header.Directory[LumpType.VisData].Offset, SeekOrigin.Begin);
			if (header.Directory[LumpType.VisData].Length > 0)
			{
				visData = new QVisData(BSPMap.ReadInt32(), BSPMap.ReadInt32());
				visData.bitSets = BSPMap.ReadBytes(visData.numOfClusters * visData.bytesPerCluster);
			}
		}

		LerpColorOnRepeatedVertex();
		GetMapTextures();
		BSPMap.Close();

		return true;
	}

	public static void GenerateMapCollider()
	{
		Node3D MapColliders = new Node3D();
		MapColliders.Name = "MapColliders";
		ColliderGroup = MapColliders;
		GameManager.Instance.AddChild(MapColliders);

		List<QBrush> staticBrushes = new List<QBrush>();
		for (int i = 0; i < models[0].numBrushes; i++)
			staticBrushes.Add(brushes[models[0].firstBrush + i]);

		// Each brush group is its own object
		var groups = staticBrushes.GroupBy(x => new { mapTextures[x.shaderId].contentsFlags, mapTextures[x.shaderId].surfaceFlags });
		int groupId = 0;
		foreach (var group in groups)
		{
			QBrush[] groupBrushes = group.ToArray();
			if (groupBrushes.Length == 0)
				continue;
			
			groupId++;

			Mesher.GenerateGroupBrushCollider(groupId, ColliderGroup, groupBrushes);
		}
	}
	public static void GenerateSurfaces()
	{
		MapMesh = new Node3D();
		MapMesh.Name = "MapMeshes";
		Node3D holder = MapMesh;
		GameManager.Instance.AddChild(MapMesh);

		List<QSurface> staticGeometry = new List<QSurface>();
		for (int i = 0; i < models[0].numSurfaces; i++)
			staticGeometry.Add(surfaces[models[0].firstSurface + i]);

		// Each surface group is its own object
		var groups = staticGeometry.GroupBy(x => new { x.type, x.shaderId, x.lightMapID });
		int groupId = 0;
		foreach (var bigGroup in groups)
		{
			var limitedGroup = bigGroup.Chunk(MAX_MESH_SURFACES);
			foreach (var group in limitedGroup)
			{
				QSurface[] groupSurfaces = group.ToArray();
				if (groupSurfaces.Length == 0)
					continue;

				groupId++;

				switch (bigGroup.Key.type)
				{
					case QSurfaceType.Patch:
						Mesher.GenerateBezObject(mapTextures[groupSurfaces[0].shaderId].name, groupSurfaces[0].lightMapID, groupId, holder, groupSurfaces);
						break;
					case QSurfaceType.Polygon:
					case QSurfaceType.Mesh:
						Mesher.GeneratePolygonObject(mapTextures[groupSurfaces[0].shaderId].name, groupSurfaces[0].lightMapID, groupId, holder, groupSurfaces);
						break;
					case QSurfaceType.Billboard:
	//					Mesher.GenerateBillBoardObject(mapTextures[groupSurfaces[0].shaderId].name, groupSurfaces[0].lightMapID, groupId, holder, groupSurfaces);
						break;
					default:
						GD.Print("Group " + groupId + "Skipped surface because it was not a polygon, mesh, or bez patch (" + bigGroup.Key.type + ").");
						break;
				}
			}
		}

		System.GC.Collect(2, System.GCCollectionMode.Forced);
	}
	public static void LerpColorOnRepeatedVertex()
	{
		// We are only looking for bezier type
		var groupsurfaces = surfaces.Where(s => s.type == QSurfaceType.Patch);

		// Initialize 2 lists (one for test) to hold the vertices of each surface in the group
		List<QVertex> surfVerts = new List<QVertex>();
		List<QVertex> testVerts = new List<QVertex>();

		// Now searh all the vertexes for all the bezier surface
		foreach (var groupsurface in groupsurfaces)
		{
			testVerts.Clear();

			int startVert = groupsurface.startVertIndex;
			for (int j = 0; j < groupsurface.numOfVerts; j++)
				testVerts.Add(verts[startVert + j]);

			//Get number of groups for all the vertexes by their position, as we want to get the uniques it need to match the number of vertex
			int numGroups = testVerts.GroupBy(v => new { v.position.X, v.position.Y, v.position.Z }).Count();

			// If the verts are unique, add the test vertices to the surface list
			if (numGroups == groupsurface.numOfVerts)
				surfVerts.AddRange(testVerts);
		}

		//Now we got unique vertexes for each bezier surface, search for common positions
		var vGroups = surfVerts.GroupBy(v => new { v.position.X, v.position.Y, v.position.Z });

		foreach (var vGroup in vGroups)
		{
			QVertex[] groupVerteces = vGroup.ToArray();

			if (groupVerteces.Length == 0)
				continue;

			// Set the initial color to the color of the first vertex in the group
			// The we will be interpolating the color of every common vertex
			Color color = groupVerteces[0].color;
			for (int i = 1; i < groupVerteces.Length; i++)
				color = color.Lerp(groupVerteces[i].color, 0.5f);

			// Finally set the final color to all the common vertexex
			for (int i = 0; i < groupVerteces.Length; i++)
			{
				int index = groupVerteces[i].vertId;
				verts[index].color = color;
			}
		}
	}
	public static void GetMapTextures()
	{
		TextureLoader.LoadTextures(mapTextures);
		TextureLoader.LoadTextures(mapTextures, TextureLoader.ImageFormat.TGA);
	}
}
