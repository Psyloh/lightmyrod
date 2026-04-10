using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;

namespace LightMyRod.Client.Model
{
	public enum Coverage
	{
		Partial, Full
	}
	//TODO: implement CenteredOnRod!
	public class RodManager
	{
		readonly HashSet<LightningRod> _rods = [];
		readonly ModConfig _config;
		readonly Pattern _pattern;

		public bool HasActiveRods => _rods.Any(r => r.Active);
		public IEnumerable<LightningRod> Rods => GetWhere();
		public IEnumerable<LightningRod> InactiveRods => GetWhere(r => !r.Active);

		IEnumerable<LightningRod> GetWhere(Func<LightningRod, bool>? function = null)
		{
			if (_rods.Count == 0)
			{
				return [];
			}

			if (function is null)
			{
				return _rods;
			}
			return _rods.Where(r => function(r));
		}

		public RodManager(ModConfig config)
		{
			_config = config;
			_pattern = new Pattern(config.MaxRadiusOffset);

			var origin = new FastVec2f(0, 0);
			var maxYOffset = config.MaxYOffset;

			for (var z = 0; z < _pattern.SideLength; z++)
			{
				for (var x = 0; x < _pattern.SideLength; x++)
				{
					ref var value = ref _pattern[x, z];

					var inner = new FastVec2f(x, z);

					if (origin.DistanceTo(inner) >= config.MaxRadius)
					{
						value = -1;
						continue;
					}

					var outer = inner + 1;

					var yOffset = maxYOffset;
					float radius = config.MaxRadius;

					LoopWhile(r => origin.DistanceTo(outer) <= r, ref yOffset, ref radius);
					value = yOffset << 10;

					LoopWhile(r => origin.DistanceTo(inner) <= r, ref yOffset, ref radius);
					value |= yOffset;
				}
			}
		}

		void LoopWhile(Func<float, bool> predicate, ref int yOffset, ref float radius)
		{
			while (yOffset > 0 && predicate(radius))
			{
				yOffset--;
				radius = _config.GetRadiusFor(yOffset);
			}
		}

		public LightningRod? TryAdd(BlockPos position)
		{
			if (_rods.Any(r => r.Position == position))
			{
				return null;
			}
			var rod = new LightningRod(position);
			_rods.Add(rod);
			return rod;
		}

		public bool TryRemove(int index)
		{
			if (index < 0 || index >= _rods.Count)
			{
				return false;
			}
			return _rods.Remove(_rods.ElementAt(index));
		}

		public bool TryRemove(BlockPos position)
		{
			LightningRod? rod = _rods.FirstOrDefault(r => r.Position == position);
			if (rod is null)
			{
				return false; 
			}
			return _rods.Remove(rod.Value);
		}

		public void Clear()
		{
			_rods.Clear();
		}

		public bool UpdateRods()
		{
			var updated = false;
			foreach (var rod in _rods)
			{
				updated |= rod.Update();
			}
			return updated;
		}

		public async Task<Registry> GetRegistry()
		{
			var sw = Stopwatch.StartNew();

			var registry = new Registry();
			var columnsData = GetColumnsData();
			Parallel.ForEach(columnsData, new() { MaxDegreeOfParallelism = Environment.ProcessorCount - 1 }, columnData =>
			{
				var (coords, (full, partial)) = columnData;
				var verticalPos = new BlockPos(coords.X, ApiHelper.GetRainMapHeight(coords), coords.Z);

				if (full > -1)
				{
					registry.RegisterWhile(bp => bp.Y < full, ref verticalPos, Coverage.Full);
				}
				registry.RegisterWhile(bp => bp.Y < partial, ref verticalPos, Coverage.Partial);
			});

			sw.Stop();
			ApiHelper.ModLogger.Error($"Registry fed : {sw.Elapsed} | {registry.Count} positions | {registry.PartialCount} partial");

			return registry;
		}

		static void AddCoords(ConcurrentDictionary<FastVec2i, (int, int)> data, BlockPos rodTop, int x, int z , (int, int) maxYs)
		{
			data.AddOrUpdate(new(rodTop.X + x, rodTop.Z + z), maxYs, (_, entry) =>
			{
				if (entry != maxYs)
				{
					return (Math.Max(entry.Item1, maxYs.Item1), Math.Max(entry.Item2, maxYs.Item2));
				}
				return entry;
			});
		}

		ConcurrentDictionary<FastVec2i, (int, int)> GetColumnsData()
		{
			var sw = Stopwatch.StartNew();

			var maxYOffset = _config.MaxYOffset;
			var rodTops = GetWhere(r => r.Active).Select(r => r.Position.UpCopy(_config.ElevationOffset));
			var columnsData = new ConcurrentDictionary<FastVec2i, (int, int)>(-1, _pattern.Length);
			Parallel.For(0, _pattern.Length, new() { MaxDegreeOfParallelism = Environment.ProcessorCount - 1 }, i =>
			{
				var value = _pattern[i];
				if (value < 0)
				{
					return;
				}
				
				var (z, x) = Math.DivRem(i, _pattern.SideLength);
				foreach (var rodTop in rodTops)
				{
					var full = value >> 10;
					var maxYs = (full == maxYOffset ? -1 : rodTop.Y - full, rodTop.Y - (value & 0x3ff));

					if (x > 0 || z > 0)
					{
						AddCoords(columnsData, rodTop, x, z, maxYs);
					}
					AddCoords(columnsData, rodTop, -x - 1, -z - 1, maxYs);
					AddCoords(columnsData, rodTop, x, -z - 1, maxYs);
					AddCoords(columnsData, rodTop, -x - 1, z, maxYs);
				}
			});

			sw.Stop();
			ApiHelper.ModLogger.Error($"columns data : {sw.Elapsed} | {columnsData.Count} coords");

			return columnsData;
		}
	}

	struct FeedContext(int patternValue, int rainHeight, FastVec2i coords)
	{
		public readonly int RainHeight => rainHeight;
		public readonly int PatternValue => patternValue;
		public readonly FastVec2i Coords => coords;
	}
}