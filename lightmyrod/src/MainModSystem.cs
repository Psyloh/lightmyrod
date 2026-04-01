using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;

namespace LightMyRod
{
	public class ModConfig
	{
		public float MaxRadius{ get; set; } = 40;
		public float ArtificialElevation { get; set; } = 5;
		public float ElevationAttractivenessMultiplier { get; set; } = 2;
		public bool CenterAreaOnRod { get; set; } = false;
		public bool ShowPartiallyProtected { get; set; } = true;
		public byte[] PartiallyProtectedColor { get; set; } = [180, 40, 5, 150];
		public byte[] FullyProtectedColor { get; set; } = [0, 255, 80, 80];

		public float GetRadiusFor(int yDiff) => yDiff * ElevationAttractivenessMultiplier;
		public int GetYDiffAt(float radius) => (int)Math.Ceiling(radius / ElevationAttractivenessMultiplier);
		public int GetMaxYDiff() => GetYDiffAt(MaxRadius);
		public int GetMaxOffset() => (int)Math.Ceiling(MaxRadius);
	}

	public class MainModSystem : ModSystem
	{
		//TODO: get config from file
		ModConfig? _config;
		ModConfig Config
		{
			get
			{
				_config ??= new();
				return _config;
			}
		}
		Highlighter? _highlighter;
		Highlighter Highlighter
		{
			get
			{
				_highlighter ??= new(Config);
				return _highlighter;
			}
		}
		readonly Dictionary<string, PlayerState> _playerStates = [];
		//TODO: save and load states

		public override void StartServerSide(ICoreServerAPI api)
		{
			ApiHelper.Api = api;

			api.Event.DidPlaceBlock += Event_DidPlaceBlock;
			api.Event.BreakBlock += Event_BreakBlock;
			api.Event.DidBreakBlock += Event_DidBreakBlock;

			api.ChatCommands.GetOrCreate("lightmyrod").WithAlias("lmr")
				.RequiresPrivilege(Privilege.chat)
				.RequiresPlayer()
				.BeginSub("list").WithAlias("l")
					.HandleWith(OnList)
				.EndSub()
				.BeginSub("add").WithAlias("a")
					.HandleWith(OnAdd)
				.EndSub()
				.BeginSub("addall").WithAlias("!")
					.HandleWith(OnAddAll)
				.EndSub()
				.BeginSub("clear").WithAlias("c")
					.HandleWith(OnClear)
				.EndSub()
				.BeginSub("remove").WithAlias("rem").WithAlias("r")
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

		public override void Dispose()
		{
			if (ApiHelper.Api is ICoreServerAPI sapi)
			{
				sapi.Event.DidPlaceBlock += Event_DidPlaceBlock;
				sapi.Event.BreakBlock -= Event_BreakBlock;
				sapi.Event.DidBreakBlock -= Event_DidBreakBlock;
			}
		}

		private void Event_DidPlaceBlock(IServerPlayer byPlayer, int oldblockId, BlockSelection blockSel, ItemStack withItemStack)
		{
			foreach (var state in _playerStates.Values)
			{
				if (state.BlockPlaced() && state.IsHighlighted)
				{
					Highlight(state);
				}
			}
		}

		private void Event_BreakBlock(IServerPlayer byPlayer, BlockSelection blockSel, ref float dropQuantityMultiplier, ref EnumHandling handling)
		{
			if (blockSel.Block.Code != "lightningrod")
			{
				return;
			}

			foreach (var state in _playerStates.Values)
			{
				var index = state.Rods.IndexOf(r => r.Position == blockSel.Position);
				if (index >= 0)
				{
					Remove(index, state);
				}
			}
		}

		private void Event_DidBreakBlock(IServerPlayer byPlayer, int oldblockId, BlockSelection blockSel)
		{
			foreach (var state in _playerStates.Values)
			{
				if (state.BlockRemoved() && state.IsHighlighted)
				{
					Highlight(state);
				}
			}
		}

		PlayerState GetOrCreateState(IPlayer player) => GetOrCreateState(player.PlayerUID);
		PlayerState GetOrCreateState(string uid)
		{
			if (!_playerStates.TryGetValue(uid, out PlayerState? state))
			{
				state = new(uid, Config);
				_playerStates.Add(uid, state);
			}
			return state;
		}

		static void SendMessage(IPlayer player, string message)
		{
			if (player is IServerPlayer splayer)
			{
				splayer.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.OwnMessage);
			}
		}

		TextCommandResult OnAdd(TextCommandCallingArgs args)
		{
			IPlayer player = args.Caller.Player;
			BlockSelection? selected = player.CurrentBlockSelection;
			if (selected is null || selected.Block.Code != "lightningrod")
			{
				return TextCommandResult.Success($"You need to be aiming at a lightning rod!");
			}

			var state = GetOrCreateState(player);
			var rod = state.TryAdd(selected.Position);
			if (rod is null)
			{
				return TextCommandResult.Success($"That one is already on the list!");
			}
			
			if (state.IsHighlighted && !rod.Covered)
			{
				Highlight(state, player);
			}
			return TextCommandResult.Success($"Rod at {ApiHelper.GetLocalPosition(rod.Position)} added to the list!");
		}

		TextCommandResult OnAddAll(TextCommandCallingArgs args)
		{
			var player = args.Caller.Player;
			var playerPos = player.Entity.Pos.AsBlockPos;
			var start = new BlockPos(playerPos.X - 128, 1, playerPos.Z - 128);
			var end = new BlockPos(playerPos.X + 128, ApiHelper.MapSizeY, playerPos.Z + 128);

			SendMessage(player, $"Searching...");

			List<BlockPos> positions = [];
			ApiHelper.WalkBlocks(start, end, (block, x, y, z) =>
			{
				if (block.Code == "lightningrod")
				{
					var pos = new BlockPos(x, y, z);
					positions.Add(pos);

					SendMessage(player, $"Found at {pos}");
				}
			});
			if (positions.Count == 0)
			{
				return TextCommandResult.Success($"No rod spotted!");
			}

			var state = GetOrCreateState(player);
			var added = state.TryAdd(positions);
			if (added != 0 && state.IsHighlighted)
			{
				Highlight(state, player);
			}
			return TextCommandResult.Success($"{added} rods registered!");
		}

		//TODO: multi => check if sendmessage is unicast
		//TODO: multi => check if people can deal with same rods
		//TODO: multi => maybe set a distance at which stuff got unhi
		//TODO: perfs => highlight through threads, try Stopwatch different parts of algo

		TextCommandResult OnList(TextCommandCallingArgs args)
		{
			var player = args.Caller.Player;
			var state = GetOrCreateState(player);
			if (state.IsEmpty)
			{
				return TextCommandResult.Success($"List empty!");
			}

			foreach (var (index, rod) in state.IndexedRods)
			{
				SendMessage(player, $"{index}: {ApiHelper.GetLocalPosition(rod.Position)}");
			}
			return TextCommandResult.Success();
		}
		
		TextCommandResult OnClear(TextCommandCallingArgs args)
		{
			IPlayer player = args.Caller.Player;

			var state = GetOrCreateState(player);
			if (!state.IsEmpty)
			{
				Highlighter.Unhi(player);

				state.Clear();
			}
			return TextCommandResult.Success($"All cleared!");
		}

		TextCommandResult OnRemove(TextCommandCallingArgs args)
		{
			IPlayer player = args.Caller.Player;
			var index = (int)args.Parsers[0].GetValue();

			var state = GetOrCreateState(player);
			if (Remove(index, state, player))
			{
				return TextCommandResult.Success($"Rod unlisted!");
			}
			return TextCommandResult.Success($@"Wrong index : use ""/lmr list"" to choose an index from!");
		}

		TextCommandResult OnHi(TextCommandCallingArgs args)
		{
			var player = args.Caller.Player;
			var state = GetOrCreateState(player);
			if (!state.IsHighlighted)
			{
				state.IsHighlighted = true;

				if (!state.IsEmpty)
				{
					Highlight(state, player);

					var covered = state.CoveredRods;
					if (covered.Length > 0)
					{
						SendMessage(player, $"Those rods have blocks above them :");

						foreach (var rod in covered)
						{
							SendMessage(player, $"{rod.LocalPosition}");
						}
					}
				}
			}
			return TextCommandResult.Success($"Highlight is on!");
		}

		TextCommandResult OnUnhi(TextCommandCallingArgs args)
		{
			IPlayer player = args.Caller.Player;
			var state = GetOrCreateState(player);
			if (state.IsHighlighted)
			{
				state.IsHighlighted = false;

				if (!state.IsEmpty)
				{
					Highlighter.Unhi(player);
				}
			}
			return TextCommandResult.Success("Highlight is off!");
		}

		bool Remove(int index, PlayerState state, IPlayer? player = null)
		{
			var removed = state.TryRemove(index);
			if (removed && state.IsHighlighted)
			{
				Highlight(state, player);
			}
			return removed;
		}

		void Highlight(PlayerState state, IPlayer? player = null)
		{
			Highlighter.Hightlight(state, player ?? state.GetPlayer());
		}
	}

	public static class ApiHelper
	{
		static ICoreServerAPI? _api;
		public static ICoreServerAPI Api
		{
			get
			{
				if (_api is null) throw new Exception("Api is null x_x");

				return _api;
			}
			set
			{
				_api = value;
			}
		}
		public static int MapSizeY => Api.WorldManager.MapSizeY;

		public static void WalkBlocks(BlockPos min, BlockPos max, Action<Block, int, int, int> onBlock) =>
			Api.World.BlockAccessor.WalkBlocks(min, max, onBlock);
		public static Vec3i GetLocalPosition(BlockPos pos) => pos.ToLocalPosition(Api);
		public static IPlayer GetPlayer(string uid) => Api.World.PlayerByUid(uid);
		public static int GetRainMapHeight(BlockPos pos) => Api.World.BlockAccessor.GetRainMapHeightAt(pos);
		public static bool IsRainMap(BlockPos pos) => GetRainMapHeight(pos) == pos.Y;
		public static void UnhiBlocks(IPlayer player, int channel) => Api.World.HighlightBlocks(player, channel, [], []);
		public static void HighlightBlocks(IPlayer player, int channel, List<BlockPos> positions, List<int> colors) =>
			Api.World.HighlightBlocks(player, channel, positions, colors);
	}
}