using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using ReeLib;
using ReeLib.Common;
using ReeLib.Mesh;
using ReeLib.Mot;
using ReeLib.MplyMesh;

internal static class ManualGltf
{
	private sealed class Writer
	{
		public readonly List<byte> Bin = new List<byte>();

		public readonly JsonArray BufferViews = new JsonArray();

		public readonly JsonArray Accessors = new JsonArray();

		private void Align(int n = 4)
		{
			while (Bin.Count % n != 0)
			{
				Bin.Add(0);
			}
		}

		private int AddBufferView(byte[] data, int? target = null)
		{
			Align();
			int count = Bin.Count;
			Bin.AddRange(data);
			Align();
			JsonObject jsonObject = new JsonObject
			{
				["buffer"] = 0,
				["byteOffset"] = count,
				["byteLength"] = data.Length
			};
			if (target.HasValue)
			{
				jsonObject["target"] = target.Value;
			}
			BufferViews.Add(jsonObject);
			return BufferViews.Count - 1;
		}

		public int AddAccessor(byte[] data, int componentType, int count, string type, int? target = null, JsonArray? min = null, JsonArray? max = null)
		{
			int num = AddBufferView(data, target);
			JsonObject jsonObject = new JsonObject
			{
				["bufferView"] = num,
				["byteOffset"] = 0,
				["componentType"] = componentType,
				["count"] = count,
				["type"] = type
			};
			if (min != null)
			{
				jsonObject["min"] = min;
			}
			if (max != null)
			{
				jsonObject["max"] = max;
			}
			Accessors.Add(jsonObject);
			return Accessors.Count - 1;
		}
	}

	private const int FLOAT = 5126;

	private const int UNSIGNED_SHORT = 5123;

	private const int UNSIGNED_INT = 5125;

	private const int ARRAY_BUFFER = 34962;

	private const int ELEMENT_ARRAY_BUFFER = 34963;

	public static void ExportGlb(MeshFile mesh, IReadOnlyList<MotFile> motions, string outputPath, bool includeLods)
	{
		if (mesh.MeshData == null)
		{
			throw new Exception("Mesh has no MeshData");
		}
		Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
		Writer writer = new Writer();
		JsonArray jsonArray = new JsonArray();
		JsonArray jsonArray2 = new JsonArray();
		JsonArray jsonArray3 = new JsonArray();
		JsonArray jsonArray4 = new JsonArray();
		JsonArray jsonArray5 = new JsonArray();
		JsonArray jsonArray6 = new JsonArray();
		foreach (string materialName in mesh.MaterialNames)
		{
			jsonArray2.Add(new JsonObject
			{
				["name"] = materialName,
				["pbrMetallicRoughness"] = new JsonObject
				{
					["baseColorFactor"] = new JsonArray(new JsonNode[4] { 0.8, 0.8, 0.8, 1.0 }),
					["metallicFactor"] = 0.0,
					["roughnessFactor"] = 0.8
				}
			});
		}
		if (jsonArray2.Count == 0)
		{
			jsonArray2.Add(new JsonObject { ["name"] = "default" });
		}
		Dictionary<string, int> dictionary = new Dictionary<string, int>();
		Dictionary<uint, int> dictionary2 = new Dictionary<uint, int>();
		List<int> list = new List<int>();
		int? num = null;
		if (mesh.BoneData != null)
		{
			foreach (MeshBone item in mesh.BoneData.Bones.OrderBy((MeshBone b) => b.index))
			{
				Matrix4x4.Decompose(item.localTransform.ToSystem(), out var scale, out var rotation, out var translation);
				if (!IsFinite(scale) || scale.LengthSquared() == 0f)
				{
					scale = Vector3.One;
				}
				if (!IsFinite(rotation))
				{
					rotation = Quaternion.Identity;
				}
				rotation = Quaternion.Normalize(rotation);
				if (!IsFinite(translation))
				{
					translation = Vector3.Zero;
				}
				JsonObject value = new JsonObject
				{
					["name"] = item.name,
					["translation"] = Vec3(translation),
					["rotation"] = Quat(rotation),
					["scale"] = Vec3(scale)
				};
				jsonArray.Add(value);
				int num2 = jsonArray.Count - 1;
				dictionary[item.name] = num2;
				dictionary2[MurMur3HashUtils.GetHash(item.name)] = num2;
				list.Add(num2);
				if (item.parentIndex < 0 && !num.HasValue)
				{
					num = num2;
				}
			}
			foreach (MeshBone bone in mesh.BoneData.Bones.OrderBy((MeshBone b) => b.index))
			{
				if (bone.parentIndex < 0 || !dictionary.TryGetValue(bone.name, out var value2))
				{
					continue;
				}
				MeshBone meshBone = mesh.BoneData.Bones.FirstOrDefault((MeshBone b) => b.index == bone.parentIndex);
				if (meshBone != null && dictionary.TryGetValue(meshBone.name, out var value3))
				{
					JsonArray jsonArray7 = jsonArray[value3]["children"] as JsonArray;
					if (jsonArray7 == null)
					{
						jsonArray7 = (JsonArray)(jsonArray[value3]["children"] = new JsonArray());
					}
					jsonArray7.Add(value2);
				}
			}
			if (num.HasValue)
			{
				jsonArray6.Add(num.Value);
			}
			List<byte> list2 = new List<byte>();
			foreach (MeshBone item2 in mesh.BoneData.Bones.OrderBy((MeshBone b) => b.index))
			{
				WriteMat4ColumnMajor(list2, item2.inverseGlobalTransform.ToSystem());
			}
			int num3 = writer.AddAccessor(list2.ToArray(), 5126, list.Count, "MAT4");
			jsonArray4.Add(new JsonObject
			{
				["name"] = "Armature",
				["skeleton"] = num ?? list.FirstOrDefault(),
				["joints"] = new JsonArray(((IEnumerable<int>)list).Select((Func<int, JsonNode>)((int i) => i)).ToArray()),
				["inverseBindMatrices"] = num3
			});
		}
		JsonArray jsonArray8 = new JsonArray();
		foreach (MeshGroup meshGroup in (mesh.MeshData.LODs.FirstOrDefault() ?? throw new Exception("Mesh has no LODs")).MeshGroups)
		{
			int num4 = ((meshGroup.Submeshes.Count != 0) ? meshGroup.Submeshes.Min((Submesh s) => s.vertsIndexOffset) : 0);
			int num5 = meshGroup.Submeshes.SelectMany((Submesh sm) => (!mesh.MeshData.integerFaces) ? ((IEnumerable<ushort>)sm.Indices.ToArray()).Select((Func<ushort, int>)((ushort x) => x)).ToArray() : sm.IntegerIndices.ToArray()).DefaultIfEmpty(-1).Max();
			int val = Math.Max((meshGroup.vertexCount > 0) ? meshGroup.vertexCount : (meshGroup.Submeshes.Max((Submesh s) => s.vertsIndexOffset + s.vertCount) - num4), num5 + 1);
			val = Math.Min(val, meshGroup.Buffer.Positions.Length - num4);
			Vector3[] array = meshGroup.Buffer.Positions.AsSpan(num4, val).ToArray();
			QuantizedNorTan[] array2 = ((meshGroup.Buffer.NormalsTangents.Length >= num4 + val) ? meshGroup.Buffer.NormalsTangents.AsSpan(num4, val).ToArray() : Array.Empty<QuantizedNorTan>());
			HFloat2[] array3 = ((meshGroup.Buffer.UV0.Length >= num4 + val) ? meshGroup.Buffer.UV0.AsSpan(num4, val).ToArray() : Array.Empty<HFloat2>());
			VertexBoneWeights[] array4 = ((meshGroup.Buffer.Weights.Length >= num4 + val) ? meshGroup.Buffer.Weights.AsSpan(num4, val).ToArray() : Array.Empty<VertexBoneWeights>());
			foreach (Submesh submesh in meshGroup.Submeshes)
			{
				int[] array5 = (mesh.MeshData.integerFaces ? submesh.IntegerIndices.ToArray() : ((IEnumerable<ushort>)submesh.Indices.ToArray()).Select((Func<ushort, int>)((ushort x) => x)).ToArray());
				if (array.Length == 0 || array5.Length == 0)
				{
					continue;
				}
				JsonObject jsonObject = new JsonObject { ["POSITION"] = AddVec3Accessor(writer, array, 34962, includeBounds: true) };
				if (array2.Length != 0)
				{
					Vector3[] vals = array2.Select((QuantizedNorTan n) => n.Normal).ToArray();
					jsonObject["NORMAL"] = AddVec3Accessor(writer, vals, 34962, includeBounds: false);
				}
				if (array3.Length != 0)
				{
					List<byte> list3 = new List<byte>();
					HFloat2[] array6 = array3;
					for (int num6 = 0; num6 < array6.Length; num6++)
					{
						HFloat2 hFloat = array6[num6];
						WriteFloat(list3, (float)hFloat.x);
						WriteFloat(list3, (float)hFloat.y);
					}
					jsonObject["TEXCOORD_0"] = writer.AddAccessor(list3.ToArray(), 5126, array3.Length, "VEC2", 34962);
				}
				if (mesh.BoneData != null && array4.Length == array.Length)
				{
					AddSkinAttributes(writer, mesh, array4, jsonObject);
				}
				List<byte> list4 = new List<byte>();
				uint num7 = 0u;
				int num8 = 0;
				int num9 = 0;
				for (int num10 = 0; num10 + 2 < array5.Length; num10 += 3)
				{
					int num11 = array5[num10];
					int num12 = array5[num10 + 1];
					int num13 = array5[num10 + 2];
					if (num11 < 0 || num12 < 0 || num13 < 0)
					{
						num9++;
						continue;
					}
					uint num14 = (uint)num11;
					uint num15 = (uint)num12;
					uint num16 = (uint)num13;
					if (num14 >= array.Length || num15 >= array.Length || num16 >= array.Length)
					{
						num9++;
						continue;
					}
					num7 = Math.Max(num7, Math.Max(num14, Math.Max(num15, num16)));
					WriteUInt(list4, num14);
					WriteUInt(list4, num15);
					WriteUInt(list4, num16);
					num8 += 3;
				}
				if (num8 != 0)
				{
					if (num9 > 0)
					{
						Console.WriteLine($"Warning: skipped {num9} out-of-range triangles in submesh material={submesh.materialIndex}");
					}
					int num17 = writer.AddAccessor(list4.ToArray(), 5125, num8, "SCALAR", 34963, new JsonArray(new JsonNode[1] { 0 }), new JsonArray(new JsonNode[1] { num7 }));
					jsonArray8.Add(new JsonObject
					{
						["attributes"] = jsonObject,
						["indices"] = num17,
						["material"] = Math.Clamp(submesh.materialIndex, 0, jsonArray2.Count - 1),
						["mode"] = 4
					});
				}
			}
		}
		jsonArray3.Add(new JsonObject
		{
			["name"] = "mesh_lod0",
			["primitives"] = jsonArray8
		});
		JsonObject jsonObject2 = new JsonObject
		{
			["name"] = "mesh_lod0",
			["mesh"] = 0
		};
		if (jsonArray4.Count > 0)
		{
			jsonObject2["skin"] = 0;
		}
		jsonArray.Add(jsonObject2);
		jsonArray6.Add(jsonArray.Count - 1);
		foreach (MotFile motion in motions)
		{
			JsonObject jsonObject3 = BuildAnimation(writer, motion, dictionary2);
			if (jsonObject3 != null)
			{
				jsonArray5.Add(jsonObject3);
			}
		}
		JsonObject jsonObject4 = new JsonObject();
		jsonObject4["asset"] = new JsonObject
		{
			["version"] = "2.0",
			["generator"] = "REE-Content-Exporter PoC using REE-Lib"
		};
		jsonObject4["scene"] = 0;
		jsonObject4["scenes"] = new JsonArray(new JsonObject
		{
			["name"] = "Scene",
			["nodes"] = jsonArray6
		});
		jsonObject4["nodes"] = jsonArray;
		jsonObject4["meshes"] = jsonArray3;
		jsonObject4["materials"] = jsonArray2;
		jsonObject4["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = writer.Bin.Count });
		jsonObject4["bufferViews"] = writer.BufferViews;
		jsonObject4["accessors"] = writer.Accessors;
		JsonObject jsonObject5 = jsonObject4;
		if (jsonArray4.Count > 0)
		{
			jsonObject5["skins"] = jsonArray4;
		}
		if (jsonArray5.Count > 0)
		{
			jsonObject5["animations"] = jsonArray5;
		}
		WriteGlb(jsonObject5, writer.Bin.ToArray(), outputPath);
		Console.WriteLine($"Manual GLB export meshes=1 primitives={jsonArray8.Count} nodes={jsonArray.Count} skins={jsonArray4.Count} animations={jsonArray5.Count} bytes={new FileInfo(outputPath).Length}");
	}

	private static JsonObject? BuildAnimation(Writer w, MotFile mot, Dictionary<uint, int> boneNodeByHash)
	{
		JsonArray jsonArray = new JsonArray();
		JsonArray jsonArray2 = new JsonArray();
		foreach (BoneMotionClip boneClip in mot.BoneClips)
		{
			if (!boneNodeByHash.TryGetValue(boneClip.ClipHeader.boneHash, out var value))
			{
				continue;
			}
			if (boneClip.HasTranslation)
			{
				Track? translation = boneClip.Translation;
				if (translation != null && translation.translations?.Length > 0)
				{
					AddChannel(w, jsonArray, jsonArray2, value, "translation", boneClip.Translation.frameIndexes, (int)mot.Header.FrameRate, boneClip.Translation.translations);
				}
			}
			if (boneClip.HasRotation)
			{
				Track? rotation = boneClip.Rotation;
				if (rotation != null && rotation.rotations?.Length > 0)
				{
					AddChannel(w, jsonArray, jsonArray2, value, "rotation", boneClip.Rotation.frameIndexes, (int)mot.Header.FrameRate, boneClip.Rotation.rotations);
				}
			}
			if (boneClip.HasScale)
			{
				Track? scale = boneClip.Scale;
				if (scale != null && scale.translations?.Length > 0)
				{
					AddChannel(w, jsonArray, jsonArray2, value, "scale", boneClip.Scale.frameIndexes, (int)mot.Header.FrameRate, boneClip.Scale.translations);
				}
			}
		}
		if (jsonArray2.Count == 0)
		{
			return null;
		}
		return new JsonObject
		{
			["name"] = mot.Name,
			["samplers"] = jsonArray,
			["channels"] = jsonArray2
		};
	}

	private static void AddChannel(Writer w, JsonArray samplers, JsonArray channels, int node, string path, int[]? frameIndexes, float frameRate, Vector3[] values)
	{
		int num = values.Length;
		List<byte> list = new List<byte>();
		float num2 = 0f;
		float num3 = 0f;
		for (int i = 0; i < num; i++)
		{
			float num4 = (float)((frameIndexes == null) ? i : frameIndexes[i]) / frameRate;
			if (i == 0)
			{
				num2 = (num3 = num4);
			}
			else
			{
				num2 = Math.Min(num2, num4);
				num3 = Math.Max(num3, num4);
			}
			WriteFloat(list, num4);
		}
		List<byte> list2 = new List<byte>();
		for (int j = 0; j < values.Length; j++)
		{
			Vector3 vector = values[j];
			WriteFloat(list2, vector.X);
			WriteFloat(list2, vector.Y);
			WriteFloat(list2, vector.Z);
		}
		byte[] data = list.ToArray();
		JsonArray min = new JsonArray(new JsonNode[1] { num2 });
		JsonArray max = new JsonArray(new JsonNode[1] { num3 });
		int num5 = w.AddAccessor(data, 5126, num, "SCALAR", null, min, max);
		int num6 = w.AddAccessor(list2.ToArray(), 5126, num, "VEC3");
		int count = samplers.Count;
		samplers.Add(new JsonObject
		{
			["input"] = num5,
			["output"] = num6,
			["interpolation"] = "LINEAR"
		});
		channels.Add(new JsonObject
		{
			["sampler"] = count,
			["target"] = new JsonObject
			{
				["node"] = node,
				["path"] = path
			}
		});
	}

	private static void AddChannel(Writer w, JsonArray samplers, JsonArray channels, int node, string path, int[]? frameIndexes, float frameRate, Quaternion[] values)
	{
		int num = values.Length;
		List<byte> list = new List<byte>();
		float num2 = 0f;
		float num3 = 0f;
		for (int i = 0; i < num; i++)
		{
			float num4 = (float)((frameIndexes == null) ? i : frameIndexes[i]) / frameRate;
			if (i == 0)
			{
				num2 = (num3 = num4);
			}
			else
			{
				num2 = Math.Min(num2, num4);
				num3 = Math.Max(num3, num4);
			}
			WriteFloat(list, num4);
		}
		List<byte> list2 = new List<byte>();
		foreach (Quaternion quaternion in values)
		{
			Quaternion quaternion2 = (IsFinite(quaternion) ? Quaternion.Normalize(quaternion) : Quaternion.Identity);
			WriteFloat(list2, quaternion2.X);
			WriteFloat(list2, quaternion2.Y);
			WriteFloat(list2, quaternion2.Z);
			WriteFloat(list2, quaternion2.W);
		}
		byte[] data = list.ToArray();
		JsonArray min = new JsonArray(new JsonNode[1] { num2 });
		JsonArray max = new JsonArray(new JsonNode[1] { num3 });
		int num5 = w.AddAccessor(data, 5126, num, "SCALAR", null, min, max);
		int num6 = w.AddAccessor(list2.ToArray(), 5126, num, "VEC4");
		int count = samplers.Count;
		samplers.Add(new JsonObject
		{
			["input"] = num5,
			["output"] = num6,
			["interpolation"] = "LINEAR"
		});
		channels.Add(new JsonObject
		{
			["sampler"] = count,
			["target"] = new JsonObject
			{
				["node"] = node,
				["path"] = path
			}
		});
	}

	private static int AddVec3Accessor(Writer w, IReadOnlyList<Vector3> vals, int target, bool includeBounds)
	{
		List<byte> list = new List<byte>();
		Vector3 vector = new Vector3(float.MaxValue);
		Vector3 vector2 = new Vector3(float.MinValue);
		foreach (Vector3 val in vals)
		{
			WriteFloat(list, val.X);
			WriteFloat(list, val.Y);
			WriteFloat(list, val.Z);
			vector = Vector3.Min(vector, val);
			vector2 = Vector3.Max(vector2, val);
		}
		return w.AddAccessor(list.ToArray(), 5126, vals.Count, "VEC3", target, includeBounds ? Vec3(vector) : null, includeBounds ? Vec3(vector2) : null);
	}

	private static void AddSkinAttributes(Writer w, MeshFile mesh, VertexBoneWeights[] vertexWeights, JsonObject attributes)
	{
		Dictionary<int, int> dictionary = mesh.BoneData.Bones.OrderBy((MeshBone b) => b.index).ToList().Select((MeshBone b, int i) => (index: b.index, i: i))
			.ToDictionary(((int index, int i) x) => x.index, ((int index, int i) x) => x.i);
		List<byte> list = new List<byte>();
		List<byte> list2 = new List<byte>();
		int indexCount = vertexWeights[0].IndexCount;
		foreach (VertexBoneWeights vertexBoneWeights in vertexWeights)
		{
			List<(ushort, float)> list3 = new List<(ushort, float)>();
			for (int num2 = 0; num2 < indexCount; num2++)
			{
				float weight = vertexBoneWeights.GetWeight(num2);
				if (weight <= 0f)
				{
					continue;
				}
				int index = vertexBoneWeights.GetIndex(num2);
				if (index >= 0 && index < mesh.BoneData.DeformBones.Count)
				{
					MeshBone meshBone = mesh.BoneData.DeformBones[index];
					if (dictionary.TryGetValue(meshBone.index, out var value))
					{
						list3.Add(((ushort)value, weight));
					}
				}
			}
			list3 = list3.OrderByDescending<(ushort, float), float>(((ushort joint, float weight) x) => x.weight).Take(4).ToList();
			float num3 = list3.Sum<(ushort, float)>(((ushort joint, float weight) x) => x.weight);
			while (list3.Count < 4)
			{
				list3.Add((0, 0f));
			}
			for (int num4 = 0; num4 < 4; num4++)
			{
				WriteUShort(list, list3[num4].Item1);
			}
			for (int num5 = 0; num5 < 4; num5++)
			{
				WriteFloat(list2, (num3 > 0f) ? (list3[num5].Item2 / num3) : 0f);
			}
		}
		attributes["JOINTS_0"] = w.AddAccessor(list.ToArray(), 5123, vertexWeights.Length, "VEC4", 34962);
		attributes["WEIGHTS_0"] = w.AddAccessor(list2.ToArray(), 5126, vertexWeights.Length, "VEC4", 34962);
	}

	private static void WriteGlb(JsonObject root, byte[] bin, string outputPath)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
		bytes = Pad(bytes, 32);
		bin = Pad(bin, 0);
		int v = 20 + bytes.Length + 8 + bin.Length;
		using FileStream fileStream = File.Create(outputPath);
		WriteUInt(fileStream, 1179937895u);
		WriteUInt(fileStream, 2u);
		WriteUInt(fileStream, (uint)v);
		WriteUInt(fileStream, (uint)bytes.Length);
		WriteUInt(fileStream, 1313821514u);
		fileStream.Write(bytes);
		WriteUInt(fileStream, (uint)bin.Length);
		WriteUInt(fileStream, 5130562u);
		fileStream.Write(bin);
	}

	private static JsonArray Vec3(Vector3 v)
	{
		return new JsonArray(new JsonNode[3] { v.X, v.Y, v.Z });
	}

	private static JsonArray Quat(Quaternion q)
	{
		return new JsonArray(new JsonNode[4] { q.X, q.Y, q.Z, q.W });
	}

	private static bool IsFinite(Vector3 v)
	{
		if (float.IsFinite(v.X) && float.IsFinite(v.Y))
		{
			return float.IsFinite(v.Z);
		}
		return false;
	}

	private static bool IsFinite(Quaternion q)
	{
		if (float.IsFinite(q.X) && float.IsFinite(q.Y) && float.IsFinite(q.Z) && float.IsFinite(q.W))
		{
			return q.LengthSquared() > 0f;
		}
		return false;
	}

	private static void WriteFloat(List<byte> b, float v)
	{
		b.AddRange(BitConverter.GetBytes(v));
	}

	private static void WriteUShort(List<byte> b, ushort v)
	{
		b.AddRange(BitConverter.GetBytes(v));
	}

	private static void WriteUInt(List<byte> b, uint v)
	{
		b.AddRange(BitConverter.GetBytes(v));
	}

	private static void WriteUInt(Stream s, uint v)
	{
		s.Write(BitConverter.GetBytes(v));
	}

	private static byte[] Pad(byte[] data, byte pad)
	{
		int num = (data.Length + 3) & -4;
		if (num == data.Length)
		{
			return data;
		}
		byte[] array = new byte[num];
		Array.Copy(data, array, data.Length);
		for (int i = data.Length; i < num; i++)
		{
			array[i] = pad;
		}
		return array;
	}

	private static void WriteMat4ColumnMajor(List<byte> b, Matrix4x4 m)
	{
		WriteFloat(b, m.M11);
		WriteFloat(b, m.M12);
		WriteFloat(b, m.M13);
		WriteFloat(b, m.M14);
		WriteFloat(b, m.M21);
		WriteFloat(b, m.M22);
		WriteFloat(b, m.M23);
		WriteFloat(b, m.M24);
		WriteFloat(b, m.M31);
		WriteFloat(b, m.M32);
		WriteFloat(b, m.M33);
		WriteFloat(b, m.M34);
		WriteFloat(b, m.M41);
		WriteFloat(b, m.M42);
		WriteFloat(b, m.M43);
		WriteFloat(b, m.M44);
	}
}
