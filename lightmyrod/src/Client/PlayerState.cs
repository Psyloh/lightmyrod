using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace LightMyRod.Client
{
	public class PlayerState(ModConfig config)
	{
		readonly Dictionary<LightningRod, ProtectiveArea> _rodAreas = [];
		public KeyValuePair<LightningRod, ProtectiveArea>[] RodAreas => [.. _rodAreas];
		public LightningRod[] Rods => [.. _rodAreas.Keys];
		public LightningRod[] CoveredRods => [.. _rodAreas.Keys.Where(r => r.Covered)];
		public ProtectiveArea[] HighlightableAreas => [.. _rodAreas.Values.Where(a => !a.Rod.Covered)];
		public (int, LightningRod)[] IndexedRods => [.. _rodAreas.Keys.Index()];

		public bool IsHighlighted = false;
		public bool IsEmpty => _rodAreas.Count == 0;

		public LightningRod? TryAdd(BlockPos position)
		{
			if (_rodAreas.Keys.IndexOf(r => r.Position == position) != -1)
			{
				return null;
			}

			var rod = new LightningRod(position);
			_rodAreas[rod] = new(rod, config);

			return rod;
		}

		public int TryAdd(IEnumerable<BlockPos> positions)
		{
			var count = 0;
			foreach (var pos in positions)
			{
				if (TryAdd(pos) != null)
				{
					count++;
				}
			}
			return count;
		}

		public bool TryRemove(int index)
		{
			if (index < 0 || index >= _rodAreas.Count)
			{
				return false;
			}
			_rodAreas.Remove(_rodAreas.ElementAt(index).Key);

			return true;
		}

		public void Clear()
		{
			_rodAreas.Clear();
		}

		public bool BlockPlaced() => UpdateIf(rod => !rod.Covered && !rod.SeesTheSky());
		public bool BlockRemoved() => UpdateIf(rod => rod.Covered && rod.SeesTheSky());

		bool UpdateIf(System.Func<LightningRod, bool> predicate)
		{
			var toUpdate = _rodAreas.Keys.Where(predicate);
			if (toUpdate.FirstOrDefault() == null)
			{
				return false;
			}

			foreach (var rod in toUpdate)
			{
				rod.Covered = !rod.Covered;
			}
			return true;
		}
	}

	public class LightningRod
	{
		readonly BlockPos _position;
		public BlockPos Position => _position;
		public Vec3i LocalPosition => ApiHelper.GetLocalPosition(_position);
		public bool Covered { get; set; }

		public LightningRod(BlockPos position)
		{
			_position = position;
			Covered = !SeesTheSky();
		}

		public bool SeesTheSky() => ApiHelper.IsRainMap(_position);
	}
}