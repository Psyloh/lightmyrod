using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;

namespace LightMyRod.Client
{
	public class MeshContext
	{
		int _color;
		ConcurrentDictionary<BlockPos, int> _facings = [];

		public void AddPosition(BlockPos position)
		{
			var currentFacing = BlockFacing.HorizontalFlags | BlockFacing.VerticalFlags;
			_facings[position] = currentFacing;

			foreach (var face in BlockFacing.ALLFACES)
			{
				var neighbor = position.AddCopy(face);
				if (_facings.TryGetValue(neighbor, out var facing))
				{
					_facings[position] = currentFacing & ~face.Flag;
					_facings[neighbor] = facing & ~face.Opposite.Flag;
				}
			}
		}
	}

	public class Registry
	{
		readonly ConcurrentDictionary<BlockPos, Coverage> _positions = [];
		public Dictionary<Coverage, MeshContext> MeshContexts
		{
			get
			{
				var contexts = Enum.GetValues<Coverage>().Select(c => (c, new MeshContext())).ToDictionary();

				foreach (var (bp, coverage) in _positions)
				{
					contexts[coverage].AddPosition(bp);
				}
				_positions.Clear();

				return contexts;
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