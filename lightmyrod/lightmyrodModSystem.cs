using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace LightMyRod
{
	class BehaviorProperties
	{
		public float ArtificialElevation { get; set; } = 1;
		public float ElevationAttractivenessMultiplier { get; set; } = 1;

		public float GetRadiusFor(int yDiff)
		{
			return yDiff * ElevationAttractivenessMultiplier;
		}

		public int GetYDiffAt(float radius)
		{
			return (int)Math.Ceiling(radius / ElevationAttractivenessMultiplier);
		}

		public static BehaviorProperties GetFromEntity(BlockEntity entity)
		{
			var behavior = entity.GetBehavior<BEBehaviorAttractsLightning>();
			var properties = behavior.properties?.AsObject<BehaviorProperties>();

			return properties ?? new();
		}
	}

	class Pattern
	{
		readonly int _sideLength;
		readonly int[] _data;
		readonly int _centerIndex;

		public Pattern(int maxRadius)
		{
			_sideLength = (maxRadius << 1) + 1;
			_centerIndex = maxRadius * _sideLength + maxRadius;

			_data = new int[_sideLength * _sideLength];
		}

		public ref int this[int x, int z]
		{
			get {
				var index = _centerIndex + z * _sideLength + x;
				return ref _data[index];
			}
		}
	}

	class ProtectiveArea(ICoreAPI api, BlockPos rodPosition, int maxRadius)
	{
		public BlockPos RodPosition { get { return rodPosition; } }
		public int MaxRadius { get { return maxRadius; } }

		public bool SeesTheSky { get { return rodPosition.Y == api.World.BlockAccessor.GetRainMapHeightAt(rodPosition); } }

		static BehaviorProperties? _properties;
		BehaviorProperties Properties
		{
			get
			{
				if (_properties is null)
				{
					var entity = api.World.BlockAccessor.GetBlockEntity(rodPosition);
					_properties = BehaviorProperties.GetFromEntity(entity);
				}
				return _properties;
			}
		}

		BlockPos? _topPosition;
		public BlockPos TopPosition
		{
			get
			{
				_topPosition ??= rodPosition.UpCopy((int)Properties.ArtificialElevation);
				return _topPosition;
			}
		}

		int? _maxYDiff;
		public int MaxYDiff
		{
			get
			{
				_maxYDiff ??= Properties.GetYDiffAt(maxRadius);
				return _maxYDiff.Value;
			}
		}

		static Pattern? _pattern;
		public Pattern Pattern
		{
			get
			{
				_pattern ??= CalcPattern();
				return _pattern;
			}
		}

		void LoopUntil(System.Func<float, bool> predicate, ref int yDiff, ref float radius)
		{
			while (yDiff > 0 && predicate(radius))
			{
				yDiff--;
				radius = Properties.GetRadiusFor(yDiff);
			}
		}

		Pattern CalcPattern()
		{
			var pattern = new Pattern(maxRadius);
			pattern[0, 0] = -1;

			var origin = new FastVec2f(0, 0);

			for (var z = -maxRadius; z < maxRadius; z++)
			{
				for (var x= -maxRadius; x < maxRadius; x++)
				{
					ref var value = ref pattern[x, z];

					if (value < 0) continue;

					var inner = new FastVec2f(
						x < 0 ? -x - 1 : x,
						z < 0 ? -z - 1 : z
					);

					if (origin.DistanceTo(inner) >= maxRadius)
					{
						value = -1;
						continue;
					}

					var outer = inner + 1;

					var yDiff = MaxYDiff;
					float radius = maxRadius;

					LoopUntil(r => origin.DistanceTo(outer) <= r, ref yDiff, ref radius);
					value = yDiff << 10;

					LoopUntil(r => origin.DistanceTo(inner) <= r, ref yDiff, ref radius);
					value |= yDiff;
				}
			}

			return pattern;
		}
	}

	enum Coverage
	{
		Half = 70, Full = 69
	}

	class Highlighter(ICoreAPI api, IPlayer player)
	{
		public List<BlockPos> Hightlight(List<ProtectiveArea> areas)
		{
			Registry registry = new();

			List<BlockPos> burried = [];

			foreach (var area in areas)
			{
				if (!area.SeesTheSky)
				{
					burried.Add(area.RodPosition);
					continue;
				}
				var pattern = area.Pattern;

				var topPos = area.TopPosition;
				var maxRadius = area.MaxRadius;
				var minHeight = topPos.Y - area.MaxYDiff;

				for (var z = -maxRadius; z < maxRadius; z++)
				{
					for (var x = -maxRadius; x < maxRadius; x++)
					{
						var value = pattern[x, z];

						if (value < 0) continue;

						var above = topPos.Y - (value & 0x3ff);
						var limit = topPos.Y - (value >> 10);

						var verticalPos = new BlockPos(topPos.X + x, 0, topPos.Z + z);
						verticalPos.Y = api.World.BlockAccessor.GetRainMapHeightAt(verticalPos.X, verticalPos.Z);

						if (limit > minHeight)
						{
							registry.RegisterUntil(bp => bp.Y < limit, ref verticalPos, Coverage.Full);
						}

						registry.RegisterUntil(bp => bp.Y < above, ref verticalPos, Coverage.Half);
					}
				}
			}

			foreach (var (coverage, list) in registry.Positions)
			{
				var color = GetColor(coverage);
				List<int> colors = [.. Enumerable.Repeat(color, list.Count)];
				api.World.HighlightBlocks(player, (int)coverage, list, colors);
			}

			return burried;
		}

		static int GetColor(Coverage coverage)
		{
			return coverage switch
			{
				//Coverage.Null => ColorUtil.ColorFromRgba([0, 0, 0, 180]),
				//Coverage.Barely => ColorUtil.ColorFromRgba([255, 50, 10, 100]),
				Coverage.Half => ColorUtil.ColorFromRgba([180, 40, 5, 150]),
				Coverage.Full => ColorUtil.ColorFromRgba([0, 255, 80, 80]),
				_ => throw new NotImplementedException()
			};
		}
	}

	class Registry
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
				return positions;
			}
		}

		public void RegisterUntil(System.Func<BlockPos, bool> predicate, ref BlockPos blockPos, Coverage coverage)
		{
			while (predicate(blockPos))
			{
				if (!_positions.TryGetValue(blockPos, out var value))
				{
					_positions.Add(blockPos.Copy(), coverage);
				}
				else if (value != Coverage.Full)
				{
					_positions[blockPos] = coverage;
				}
				blockPos.Up();
			}
		}
	}

	class PlayerState
	{
		public List<ProtectiveArea> Areas = [];
		public bool IsHighlighted = false;
	}

	public class MainModSystem : ModSystem
	{
		ICoreAPI? _api;
		ICoreAPI Api
		{
			get
			{
				if (_api is null) throw new System.Exception("Api is null");

				return _api;
			}
			set { _api = value; }
		}

		readonly Dictionary<string, PlayerState> _playerStates = [];


		public override void Start(ICoreAPI api)
		{
			Api = api;
			api.ChatCommands.GetOrCreate("lightmyrod").WithAlias("lmr")
				.RequiresPrivilege(Privilege.chat)
				.RequiresPlayer()
				.BeginSub("list")
					.HandleWith(OnList)
				.EndSub()
				.BeginSub("add")
					.HandleWith(OnAdd)
				.EndSub()
				.BeginSub("clear")
					.HandleWith(OnClear)
				.EndSub()
				.BeginSub("remove")
					.WithArgs(api.ChatCommands.Parsers.Int("index"))
					.HandleWith(OnRemove)
				.EndSub()
				.BeginSub("hi")
					.HandleWith(OnHi)
				.EndSub()
				.BeginSub("unhi")
					.HandleWith(OnUnhi)
				.EndSub();
		}

		public override void StartServerSide(ICoreServerAPI api)
		{
			api.Event.DidBreakBlock += Event_DidBreakBlock;
		}

		public override void Dispose()
		{
			(Api as ICoreServerAPI)?.Event.DidBreakBlock -= Event_DidBreakBlock;
		}

		TextCommandResult OnAdd(TextCommandCallingArgs args)
		{
			IPlayer player = args.Caller.Player;
			BlockSelection? selected = player.CurrentBlockSelection;

			if (selected is null || selected.Block.Code != "lightningrod")
			{
				return TextCommandResult.Success($"You need to be aiming at a lightning rod!");
			}
			
			if (!_playerStates.TryGetValue(player.PlayerUID, out PlayerState? state))
			{
				state = new();
				_playerStates.Add(player.PlayerUID, state);
			}
			state.Areas.Add(new(Api, selected.Position, 40));

			if (state.IsHighlighted)
			{
				Hi(player, state.Areas);
			}
			return TextCommandResult.Success($"Rod at {selected.Position.ToLocalPosition(Api)} got added to your list!");
		}

		private void Event_DidBreakBlock(IServerPlayer byPlayer, int oldblockId, BlockSelection blockSel)
		{
			if (_playerStates.TryGetValue(byPlayer.PlayerUID, out PlayerState? state))
			{
				var index = state.Areas.IndexOf(a => a.RodPosition == blockSel.Position);

				if (index >= 0)
				{
					Remove(byPlayer, state, index);
				}
			}
		}

		TextCommandResult OnList(TextCommandCallingArgs args)
		{
			IPlayer player = args.Caller.Player;

			if (!_playerStates.TryGetValue(player.PlayerUID, out PlayerState? state))
			{
				return TextCommandResult.Success($"Your list is empty!");
			}

			foreach (var (index, area) in state.Areas.Select((a, i) => (i, a)))
			{
				//TODO: check if sendmessage is individual
				(player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"{index}: {area.TopPosition.ToLocalPosition(Api)}", EnumChatType.OwnMessage);
			}
			return TextCommandResult.Success($"{state.Areas.Count} lightning rods registered!");
		}
		
		TextCommandResult OnClear(TextCommandCallingArgs args)
		{
			IPlayer player = args.Caller.Player;

			if (_playerStates.TryGetValue(player.PlayerUID, out PlayerState? state) && state.Areas.Count > 0)
			{
				Unhi(player, state);

				state.Areas.Clear();

				return TextCommandResult.Success($"All cleared!");
			}
			return TextCommandResult.Success($"There's nothing to clear!");
		}

		TextCommandResult OnRemove(TextCommandCallingArgs args)
		{
			IPlayer player = args.Caller.Player;

			if (_playerStates.TryGetValue(player.PlayerUID, out PlayerState? state))
			{
				var index = (int)args.Parsers[0].GetValue();
				if (index >= 0 && state.Areas.Count > index)
				{
					Remove(player, state, index);

					return TextCommandResult.Success($"Rod unregistered!");
				}
				return TextCommandResult.Success($@"Wrong index : use ""/lmr list"" to choose an index from!");
			}
			return TextCommandResult.Success($"There's nothing to remove...");
		}

		TextCommandResult OnHi(TextCommandCallingArgs args)
		{
			IPlayer player = args.Caller.Player;

			if (_playerStates.TryGetValue(player.PlayerUID, out PlayerState? state) && state.Areas.Count > 0)
			{
				var burried = Hi(player, state.Areas);

				state.IsHighlighted = true;

				if (burried.Count > 0)
				{
					foreach (var rod in burried)
					{
						(player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, $"{rod.ToLocalPosition(Api)}", EnumChatType.OwnMessage);
					}
					return TextCommandResult.Success("Some rods, at positions listed above, can't see the sky :-/");
				}
				return TextCommandResult.Success("All highlighted!");
			}
			return TextCommandResult.Success($"There's nothing to highlight!");
		}

		TextCommandResult OnUnhi(TextCommandCallingArgs args)
		{
			IPlayer player = args.Caller.Player;

			if (_playerStates.TryGetValue(player.PlayerUID, out PlayerState? state))
			{
				if (state.Areas.Count > 0 && state.IsHighlighted)
				{
					Unhi(player, state);

					return TextCommandResult.Success("Lights out!");
				}
				return TextCommandResult.Success("Lights are already out!");
			}
			return TextCommandResult.Success("There's nothing to put out!");
		}

		void Remove(IPlayer player, PlayerState state, int index)
		{
			state.Areas.RemoveAt(index);

			if (state.IsHighlighted)
			{
				Hi(player, state.Areas);
			}
		}

		List<BlockPos> Hi(IPlayer player, List<ProtectiveArea> areas)
		{
			var highlighter = new Highlighter(Api, player);
			return highlighter.Hightlight(areas);
		}

		void Unhi(IPlayer player, PlayerState state)
		{
			foreach (var coverage in Enum.GetValues<Coverage>())
			{
				Api.World.HighlightBlocks(player, (int)coverage, [], []);
			}
			state.IsHighlighted = false;
		}
	}
}