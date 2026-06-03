using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Assimp;
using ReeLib;
using ReeLib.Common;
using ReeLib.Mesh;
using ReeLib.Mot;
using ReeLib.MplyMesh;

internal static class Exporter
{
	private sealed class Ctx
	{
		public Scene Scene = new Scene();

		public Dictionary<string, (Node node, bool deforming, Matrix4x4 inverse)> Bones = new Dictionary<string, (Node, bool, Matrix4x4)>();

		public string RootBoneName = "root";

		public string Format = "";

		public bool IncludeLods;
	}

	public static void Export(MeshFile mesh, IEnumerable<MotFile> motions, string outputPath, bool includeLods)
	{
		string ext = Path.GetExtension(outputPath).TrimStart('.').ToLowerInvariant();
		if (ext == "glb")
		{
			ManualGltf.ExportGlb(mesh, motions.ToList(), outputPath, includeLods);
			return;
		}
		using AssimpContext assimpContext = new AssimpContext();
		string text = assimpContext.GetSupportedExportFormats().FirstOrDefault((ExportFormatDescription f) => string.Equals(f.FileExtension, ext, StringComparison.OrdinalIgnoreCase))?.FormatId;
		if (string.IsNullOrEmpty(text))
		{
			throw new Exception("Assimp has no export format for ." + ext);
		}
		Ctx ctx = new Ctx
		{
			Format = text,
			IncludeLods = includeLods
		};
		ctx.Scene.RootNode = new Node(Path.GetFileNameWithoutExtension(outputPath));
		PrepareSkeleton(ctx, mesh);
		AddMesh(ctx, mesh, Path.GetFileNameWithoutExtension(outputPath));
		foreach (MotFile motion in motions)
		{
			AddMot(ctx.Scene, mesh, motion, text);
		}
		Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
		assimpContext.ExportFile(ctx.Scene, outputPath, text);
		Console.WriteLine($"Assimp export format={text} meshes={ctx.Scene.Meshes.Count} animations={ctx.Scene.Animations.Count}");
	}

	private static Matrix4x4 Ai(Matrix4x4 m)
	{
		return Matrix4x4.Transpose(m);
	}

	private static Matrix4x4 LocalMatrix(Vector3 pos, Quaternion rot, Vector3 scale)
	{
		return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
	}

	private static void PrepareSkeleton(Ctx ctx, MeshFile mesh)
	{
		if (mesh.BoneData == null)
		{
			return;
		}
		Queue<MeshBone> queue = new Queue<MeshBone>(mesh.BoneData.Bones.Where((MeshBone b) => b.parentIndex == -1).Concat(mesh.BoneData.Bones.Where((MeshBone b) => b.parentIndex != -1)));
		MeshBone result;
		while (queue.TryDequeue(out result))
		{
			if (ctx.Bones.ContainsKey(result.name))
			{
				continue;
			}
			Node node = ctx.Scene.RootNode;
			if (result.Parent != null)
			{
				if (!ctx.Bones.TryGetValue(result.Parent.name, out (Node, bool, Matrix4x4) value))
				{
					queue.Enqueue(result);
					continue;
				}
				(node, _, _) = value;
			}
			if (ctx.Bones.Count == 0)
			{
				ctx.RootBoneName = result.name;
			}
			Node node2 = new Node(result.name, node)
			{
				Transform = Ai(result.localTransform.ToSystem())
			};
			node.Children.Add(node2);
			ctx.Bones[result.name] = (node2, result.IsDeformBone, result.inverseGlobalTransform.ToSystem());
		}
		Console.WriteLine($"Prepared skeleton nodes={ctx.Bones.Count} root={ctx.RootBoneName}");
	}

	private static void AddMesh(Ctx ctx, MeshFile file, string rootName)
	{
		foreach (string name in file.MaterialNames)
		{
			if (!ctx.Scene.Materials.Any((Material m) => m.Name == name))
			{
				ctx.Scene.Materials.Add(new Material
				{
					Name = name
				});
			}
		}
		if (file.MeshData == null)
		{
			throw new Exception("Mesh has no MeshData");
		}
		for (int num = 0; num < file.MeshData.LODs.Count; num++)
		{
			MeshLOD lod = file.MeshData.LODs[num];
			ExportLod(ctx, file, lod, ctx.IncludeLods ? $"{rootName}_lod{num}_" : (rootName + "_"));
			if (!ctx.IncludeLods)
			{
				break;
			}
		}
	}

	private static void ExportLod(Ctx ctx, MeshFile file, MeshLOD lod, string prefix)
	{
		foreach (MeshGroup meshGroup in lod.MeshGroups)
		{
			int num = 0;
			foreach (Submesh submesh in meshGroup.Submeshes)
			{
				Mesh mesh = new Mesh(PrimitiveType.Triangle);
				string matName = file.MaterialNames.ElementAtOrDefault(submesh.materialIndex) ?? "NO_MATERIAL";
				int num2 = ctx.Scene.Materials.FindIndex((Material m) => m.Name == matName);
				if (num2 < 0)
				{
					num2 = ctx.Scene.Materials.Count;
					ctx.Scene.Materials.Add(new Material
					{
						Name = matName
					});
				}
				mesh.MaterialIndex = num2;
				mesh.Name = $"{prefix}Group_{meshGroup.groupId.ToString(CultureInfo.InvariantCulture)}_sub{num++}__{matName}";
				mesh.Vertices.AddRange(submesh.Positions);
				if (submesh.Buffer.UV0.Length != 0)
				{
					Span<HFloat2> uV = submesh.UV0;
					for (int num3 = 0; num3 < uV.Length; num3++)
					{
						HFloat2 hFloat = uV[num3];
						mesh.TextureCoordinateChannels[0].Add(new Vector3((float)hFloat.x, 1f - (float)hFloat.y, 0f));
					}
					mesh.UVComponentCount[0] = 2;
				}
				if (submesh.Buffer.NormalsTangents.Length != 0)
				{
					Span<QuantizedNorTan> normalsTangents = submesh.NormalsTangents;
					for (int num3 = 0; num3 < normalsTangents.Length; num3++)
					{
						QuantizedNorTan quantizedNorTan = normalsTangents[num3];
						mesh.Normals.Add(quantizedNorTan.Normal);
						mesh.Tangents.Add(quantizedNorTan.Tangent);
						mesh.BiTangents.Add(quantizedNorTan.BiTangent);
					}
				}
				if (file.BoneData != null && submesh.Buffer.Weights.Length != 0)
				{
					AddWeights(ctx, file, submesh, mesh);
				}
				int[] array = (file.MeshData.integerFaces ? submesh.IntegerIndices.ToArray() : ((IEnumerable<ushort>)submesh.Indices.ToArray()).Select((Func<ushort, int>)((ushort x) => x)).ToArray());
				int num4 = array.Length / 3;
				for (int num5 = 0; num5 < num4; num5++)
				{
					Face face = new Face();
					int num6 = array[num5 * 3] - submesh.vertsIndexOffset;
					int num7 = array[num5 * 3 + 1] - submesh.vertsIndexOffset;
					int num8 = array[num5 * 3 + 2] - submesh.vertsIndexOffset;
					if (num6 >= 0 && num7 >= 0 && num8 >= 0 && num6 < submesh.Positions.Length && num7 < submesh.Positions.Length && num8 < submesh.Positions.Length)
					{
						face.Indices.Add(num6);
						face.Indices.Add(num7);
						face.Indices.Add(num8);
						mesh.Faces.Add(face);
					}
				}
				Node node = new Node(mesh.Name, ctx.Scene.RootNode)
				{
					Transform = Matrix4x4.Identity
				};
				node.MeshIndices.Add(ctx.Scene.Meshes.Count);
				ctx.Scene.RootNode.Children.Add(node);
				ctx.Scene.Meshes.Add(mesh);
			}
		}
	}

	private static void AddWeights(Ctx ctx, MeshFile file, Submesh sub, Mesh aiMesh)
	{
		int indexCount = sub.Weights[0].IndexCount;
		for (int i = 0; i < sub.Weights.Length; i++)
		{
			VertexBoneWeights vertexBoneWeights = sub.Weights[i];
			for (int j = 0; j < indexCount; j++)
			{
				float weight = vertexBoneWeights.GetWeight(j);
				if (weight <= 0f)
				{
					continue;
				}
				MeshBone srcBone = ((file.BoneData.DeformBones.Count == 0) ? file.BoneData.RootBones[0] : file.BoneData.DeformBones[vertexBoneWeights.GetIndex(j)]);
				if (ctx.Bones.TryGetValue(srcBone.name, out (Node, bool, Matrix4x4) value))
				{
					Bone bone = aiMesh.Bones.FirstOrDefault((Bone x) => x.Name == srcBone.name);
					if (bone == null)
					{
						bone = new Bone
						{
							Name = srcBone.name,
							OffsetMatrix = Ai(value.Item3)
						};
						aiMesh.Bones.Add(bone);
					}
					bone.VertexWeights.Add(new VertexWeight(i, weight));
				}
			}
		}
	}

	private static IEnumerable<Node> Flat(Node n)
	{
		yield return n;
		foreach (Node item in n.Children.SelectMany(Flat))
		{
			yield return item;
		}
	}

	private static void AddMot(Scene scene, MeshFile mesh, MotFile mot, string exportFormat)
	{
		bool num = exportFormat == "fbx";
		Animation animation = new Animation
		{
			Name = mot.Name,
			TicksPerSecond = (int)mot.Header.FrameRate,
			DurationInTicks = mot.Header.endFrame
		};
		float num2 = (num ? ((float)(int)mot.Header.FrameRate / 24f) : 1f);
		Dictionary<uint, Node> dictionary = new Dictionary<uint, Node>();
		foreach (Node item in Flat(scene.RootNode))
		{
			uint result;
			uint key = ((item.Name.StartsWith("_hash") && uint.TryParse(item.Name.AsSpan("_hash".Length), out result)) ? result : MurMur3HashUtils.GetHash(item.Name));
			dictionary.TryAdd(key, item);
		}
		foreach (BoneMotionClip boneClip in mot.BoneClips)
		{
			BoneClipHeader clipHeader = boneClip.ClipHeader;
			Node valueOrDefault = dictionary.GetValueOrDefault(clipHeader.boneHash);
			string text = clipHeader.boneName ?? clipHeader.OriginalName ?? mot.GetBoneByHash(clipHeader.boneHash)?.boneName ?? valueOrDefault?.Name;
			if (text == null)
			{
				continue;
			}
			NodeAnimationChannel nodeAnimationChannel = new NodeAnimationChannel
			{
				NodeName = text
			};
			if (boneClip.HasTranslation)
			{
				if (boneClip.Translation.frameIndexes == null)
				{
					Vector3[]? translations = boneClip.Translation.translations;
					if (translations != null && translations.Length != 0)
					{
						nodeAnimationChannel.PositionKeys.Add(new VectorKey(0.0, boneClip.Translation.translations[0]));
					}
				}
				else
				{
					for (int i = 0; i < boneClip.Translation.frameIndexes.Length; i++)
					{
						nodeAnimationChannel.PositionKeys.Add(new VectorKey((float)boneClip.Translation.frameIndexes[i] * num2, boneClip.Translation.translations[i]));
					}
				}
			}
			else
			{
				Vector3 value = mot.GetBoneByHash(clipHeader.boneHash)?.translation ?? Vector3.Zero;
				nodeAnimationChannel.PositionKeys.Add(new VectorKey(0.0, value));
			}
			if (boneClip.HasRotation)
			{
				if (boneClip.Rotation.frameIndexes == null)
				{
					Quaternion[]? rotations = boneClip.Rotation.rotations;
					if (rotations != null && rotations.Length != 0)
					{
						nodeAnimationChannel.RotationKeys.Add(new QuaternionKey(0.0, boneClip.Rotation.rotations[0]));
					}
				}
				else
				{
					for (int j = 0; j < boneClip.Rotation.frameIndexes.Length; j++)
					{
						nodeAnimationChannel.RotationKeys.Add(new QuaternionKey((float)boneClip.Rotation.frameIndexes[j] * num2, boneClip.Rotation.rotations[j]));
					}
				}
			}
			else
			{
				Quaternion value2 = mot.GetBoneByHash(clipHeader.boneHash)?.quaternion ?? Quaternion.Identity;
				nodeAnimationChannel.RotationKeys.Add(new QuaternionKey(0.0, value2));
			}
			if (boneClip.HasScale)
			{
				if (boneClip.Scale.frameIndexes == null)
				{
					Vector3[]? translations2 = boneClip.Scale.translations;
					if (translations2 != null && translations2.Length != 0)
					{
						nodeAnimationChannel.ScalingKeys.Add(new VectorKey(0.0, boneClip.Scale.translations[0]));
					}
				}
				else
				{
					for (int k = 0; k < boneClip.Scale.frameIndexes.Length; k++)
					{
						nodeAnimationChannel.ScalingKeys.Add(new VectorKey((float)boneClip.Scale.frameIndexes[k] * num2, boneClip.Scale.translations[k]));
					}
				}
			}
			animation.NodeAnimationChannels.Add(nodeAnimationChannel);
		}
		scene.Animations.Add(animation);
		Console.WriteLine($"Added anim {mot.Name}: channels={animation.NodeAnimationChannels.Count} frames={mot.Header.frameCount} fps={mot.Header.FrameRate}");
	}
}
