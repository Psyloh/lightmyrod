using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Vintagestory.API.MathTools;

namespace LightMyRod.Client.Model
{
	public struct MeshCube(BlockPos pos, int facing)
	{
		public readonly BlockPos Position => pos.Copy();
		public readonly int Facing => facing;
	}

	public class MeshInfo
	{
		readonly ConcurrentBag<MeshCube> _cubes = [];
		public MeshCube[] Cubes => [.. _cubes];

		BlockPos? _origin;
		public BlockPos Origin => _origin!.Copy();

		public void AddInfo(BlockPos position, int facing)
		{
			_cubes.Add(new(position, facing));

			if (_origin is null)
			{
				_origin = position.Copy();
			}
			else
			{
				_origin.X = Math.Min(_origin.X, position.X);
				_origin.Y = Math.Min(_origin.Y, position.Y);
				_origin.Z = Math.Min(_origin.Z, position.Z);
			}
		}
	}

	public class Registry
	{
		readonly ConcurrentDictionary<BlockPos, Coverage> _positions = [];

		public int Count => _positions.Count;
		public int PartialCount => _positions.Values.Count(c => c == Coverage.Partial);

		public Dictionary<Coverage, MeshInfo> MeshInfos
		{
			get
			{
				var sw = Stopwatch.StartNew();
				var meshInfos = Enum.GetValues<Coverage>().Select(c => (c, new MeshInfo())).ToDictionary();

				_positions.AsParallel().ForAll(entry =>
				{
					var (pos, coverage) = entry;

					var facing = 0;
					foreach (var face in BlockFacing.ALLFACES)
					{
						var neighbor = pos.AddCopy(face);
						if (_positions.TryGetValue(neighbor, out var neighborCoverage))
						{
							if (coverage == neighborCoverage)
							{
								continue;
							}
						}
						facing |= face.Flag;
					}
					
					if (facing != 0)
					{
						meshInfos[coverage].AddInfo(pos, facing);
					}
				});
				_positions.Clear();

				sw.Stop();
				ApiHelper.ModLogger.Error($"Mesh infos ready : {sw.Elapsed} | {meshInfos.Count} meshInfos");

				return meshInfos;
			}
		}

		public void Register(BlockPos pos, Coverage coverage)
		{
			var key = pos.Copy();
			var value = _positions.GetOrAdd(key, coverage);

			if (value != coverage && value != Coverage.Full)
			{
				_positions[key] = coverage;
			}
		}

		public void RegisterWhile(Func<BlockPos, bool> predicate, ref BlockPos blockPos, Coverage coverage)
		{
			while (predicate(blockPos))
			{
				Register(blockPos, coverage);
				blockPos.Up();
			}
		}
	}
}