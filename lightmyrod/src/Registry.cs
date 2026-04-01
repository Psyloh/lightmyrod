using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;

namespace LightMyRod
{
	public class Registry
	{
		readonly Dictionary<BlockPos, Coverage> _positions = [];
		public Dictionary<Coverage, List<BlockPos>> Positions
		{
			get
			{
				var positions = Enum.GetValues<Coverage>().ToDictionary(c => c, c => new List<BlockPos>());

				foreach (var (bp, coverage) in _positions)
				{
					positions[coverage].Add(bp);
				}
				_positions.Clear();

				return positions;
			}
		}

		public void Register(BlockPos pos, Coverage coverage)
		{
			if (!_positions.TryGetValue(pos, out var value))
			{
				_positions.Add(pos.Copy(), coverage);
			}
			else if (value != Coverage.Full)
			{
				_positions[pos] = coverage;
			}
		}

		public void RegisterUntil(Func<BlockPos, bool> predicate, ref BlockPos blockPos, Coverage coverage)
		{
			while (predicate(blockPos))
			{
				Register(blockPos, coverage);
				blockPos.Up();
			}
		}
	}
}