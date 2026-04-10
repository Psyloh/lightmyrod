using LightMyRod.Client.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;

namespace LightMyRod.Client
{
	public class PlayerState(Highlighter highlighter, ModConfig config): IDisposable
	{
		public bool IsHighlighted = false;
		public IEnumerable<(int, LightningRod)> IndexedRods => _manager.Rods.Index();
		public IEnumerable<LightningRod> InactiveRods => _manager.InactiveRods;

		readonly RodManager _manager = new(config);

		public LightningRod? TryAdd(BlockPos position)
		{
			var rod = _manager.TryAdd(position);
			if (rod != null && rod.Value.Active)
			{
				UpdateHighlight();
			}
			return rod;
		}

		public int TryAddRange(IEnumerable<BlockPos> positions)
		{
			var count = 0;
			foreach (var pos in positions)
			{
				if (_manager.TryAdd(pos) != null)
				{
					count++;
				}
			}

			if (count > 0)
			{
				UpdateHighlight();
			}
			return count;
		}

		public bool TryRemove(int index)
		{
			var removed = _manager.TryRemove(index);
			if (removed)
			{
				UpdateHighlight();
			}
			return removed;
		}

		public bool TryRemove(BlockPos pos)
		{
			var removed = _manager.TryRemove(pos);
			if (removed)
			{
				UpdateHighlight();
			}
			return removed;
		}

		public void Clear()
		{
			_manager.Clear();
			UpdateHighlight();
		}

		void UpdateHighlight()
		{
			if (IsHighlighted)
			{
				if (_manager.HasActiveRods)
				{
					Task.Run(() => highlighter.Update(this));
				}
				else
				{
					highlighter.Unhi();
				}
			}
		}

		public IEnumerable<LightningRod>? Highlight()
		{
			if (!IsHighlighted)
			{
				IsHighlighted = true;

				if (_manager.HasActiveRods)
				{
					_ = Task.Run(async() =>
					{
						var registry = await _manager.GetRegistry();
						_ = highlighter.Highlight(registry);
					});
				}
			}
			return _manager.InactiveRods;
		}

		public void Unhi()
		{
			if (!IsHighlighted)
			{
				return;
			}
			IsHighlighted = false;

			if (_manager.HasActiveRods)
			{
				highlighter.Unhi();
			}
		}

		public void BlockEvent()
		{
			if (_manager.UpdateRods())
			{
				UpdateHighlight();
			}
		}

		public void Dispose()
		{
			highlighter.Dispose();

			GC.SuppressFinalize(this);
		}
	}
}