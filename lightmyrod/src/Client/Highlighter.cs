using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace LightMyRod.Client
{
	class Highlighter(ModConfig config, PlayerState state)
	{
		public async Task Highlight()
		{
			if (!state.IsHighlighted)
			{
				return;
			}

			Stopwatch sw = Stopwatch.StartNew();
			Registry registry = new();

			foreach (var area in state.HighlightableAreas)
			{
				await area.FeedParallel(registry);
			}

			var meshContexts = registry.MeshContexts;

			sw.Stop();
			ApiHelper.Api.Logger.Error($"{sw.Elapsed}");



			return;
			foreach (var (coverage, context) in meshContexts)
			{
				if (coverage == Coverage.Partial && !config.ShowPartial)
				{
					continue;
				}

				var color = GetColor(coverage);
				//List<int> colors = [.. Enumerable.Repeat(color, list.Count)];
				//ApiHelper.HighlightBlocks(GetChannel(coverage), list, colors);
			}
		}

		//void Test(Registry registry, int color)
		//{
		//	var positions = registry.Positions[Coverage.Full];
		//	Dictionary<BlockPos, int> faces = new(positions.Count);

		//	MeshData meshData = new(positions.Count * 4 * 6, positions.Count * 6 * 6, false, false, true, false);

		//	BlockPos start = positions[0].Copy();
		//	var allFaces = BlockFacing.ALLFACES;

		//	foreach (BlockPos position in positions)
		//	{
		//		int pattern = 0;
		//		foreach (var face in allFaces)
		//		{
		//			if ()
		//		}
		//		faces[position] = pattern;

		//		if (!faces.ContainsKey(blockPos3.AddCopy(BlockFacing.NORTH)))
		//		{
		//			num |= BlockFacing.NORTH.Flag;
		//		}

		//		if (!faces.ContainsKey(blockPos3.AddCopy(BlockFacing.EAST)))
		//		{
		//			num |= BlockFacing.EAST.Flag;
		//		}

		//		if (!faces.ContainsKey(blockPos3.AddCopy(BlockFacing.SOUTH)))
		//		{
		//			num |= BlockFacing.SOUTH.Flag;
		//		}

		//		if (!faces.ContainsKey(blockPos3.AddCopy(BlockFacing.WEST)))
		//		{
		//			num |= BlockFacing.WEST.Flag;
		//		}

		//		if (!faces.ContainsKey(blockPos3.AddCopy(BlockFacing.UP)))
		//		{
		//			num |= BlockFacing.UP.Flag;
		//		}

		//		if (!faces.ContainsKey(blockPos3.AddCopy(BlockFacing.DOWN)))
		//		{
		//			num |= BlockFacing.DOWN.Flag;
		//		}

		//		dictionary[blockPos3] = num;
		//	}

		//	var origin = blockPos.Copy();

			
		//	Vec3f vec3f = new Vec3f();
		//	Vec3f sizeXyz = new Vec3f(1f, 1f, 1f);
			
		//	float[] defaultBlockSideShadingsByFacing = CubeMeshUtil.DefaultBlockSideShadingsByFacing;
		//	foreach (KeyValuePair<BlockPos, int> item in dictionary)
		//	{
		//		int value = item.Value;
		//		vec3f.X = (float)(item.Key.X - origin.X) + 0.5f;
		//		vec3f.Y = (float)(item.Key.InternalY - origin.Y) + 0.5f;
		//		vec3f.Z = (float)(item.Key.Z - origin.Z) + 0.5f;
		//		for (int l = 0; l < 6; l++)
		//		{
		//			BlockFacing blockFacing = BlockFacing.ALLFACES[l];
		//			if ((value & blockFacing.Flag) != 0)
		//			{
		//				ModelCubeUtilExt.AddFaceSkipTex(meshData, blockFacing, vec3f, sizeXyz, color, defaultBlockSideShadingsByFacing[blockFacing.Index]);
		//			}
		//		}
		//	}
		//	var modelRef = ApiHelper.Api.Render.UploadMesh(meshData);
		//}

		int GetColor(Coverage coverage) => coverage switch
		{
			Coverage.Partial => ColorUtil.ColorFromRgba(config.PartialProtectionColor),
			Coverage.Full => ColorUtil.ColorFromRgba(config.FullProtectionColor),
			_ => throw new NotImplementedException()
		};

		static int GetChannel(Coverage coverage) => coverage switch
		{
			Coverage.Partial => 68,
			Coverage.Full => 69,
			_ => throw new NotImplementedException()
		};

		public static void Unhi()
		{
			foreach (var coverage in Enum.GetValues<Coverage>())
			{
				ApiHelper.UnhiBlocks(GetChannel(coverage));
			}
		}
	}
}