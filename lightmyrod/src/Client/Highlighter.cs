using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace LightMyRod.Client
{
	class Highlighter(ModConfig config)
	{
		public void Hightlight(PlayerState state, IPlayer player)
		{
			Registry registry = new();

			foreach (var area in state.HighlightableAreas)
			{
				area.Feed(registry);
			}

			foreach (var (coverage, list) in registry.Positions)
			{
				var color = GetColor(coverage);
				List<int> colors = [.. Enumerable.Repeat(color, list.Count)];
				ApiHelper.HighlightBlocks(player, GetChannel(coverage), list, colors);
			}
		}

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

		public static void Unhi(IPlayer player)
		{
			foreach (var coverage in Enum.GetValues<Coverage>())
			{
				ApiHelper.UnhiBlocks(player, GetChannel(coverage));
			}
		}
	}
}