using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;

namespace LightMyRod.Client
{
	public class ClientConfig
	{
		public bool HighlightPartial { get; set; } = true;
		public byte[] PartialProtectionColor { get; set; } = [180, 40, 5, 150];
		public byte[] FullProtectionColor { get; set; } = [0, 255, 80, 80];
	}

	public class ModConfig(ConfigData data, ClientConfig config)
	{
		const string FILENAME = "lightmyrod-client.json";
		//TODO: replace these when transpiled
		public static float MaxRadius => 40;
		public static bool CenterProtectionOnRod => false;
		public static bool FixFireStartingAlgorithm => false;
		public static int FireStartRadius => 1;

		public bool HighlightPartial => config.HighlightPartial;
		public byte[] PartialProtectionColor => config.PartialProtectionColor;
		public byte[] FullProtectionColor => config.FullProtectionColor;

		public float ArtificialElevation => data.ArtificialElevation;

		public float GetRadiusFor(int yDiff) => yDiff * data.ElevationAttractivenessMultiplier;
		public int GetYDiffAt(float radius) => (int)Math.Ceiling(radius / data.ElevationAttractivenessMultiplier);
		public int GetMaxYDiff() => GetYDiffAt(data.MaxRadius);
		public int GetMaxOffset() => (int)Math.Ceiling(data.MaxRadius);

		public static ModConfig Get(ConfigData data, ILogger logger)
		{
			ClientConfig config;
			try
			{
				config = ApiHelper.LoadConfig(FILENAME);
				config ??= new();
			}
			catch (Exception e)
			{
				logger.Warning("Configuration file corrupted - loading default settings! Please fix or delete the file...");
				logger.Error(e);

				return new (data, new ClientConfig());
			}
			ApiHelper.Api.StoreModConfig(config, FILENAME);

			return new ModConfig(data, config);
		}
	}

	public class ClientSide : IDisposable
	{
		ModConfig? _config;
		ModConfig Config => _config!;

		Highlighter? _highlighter;
		Highlighter Highlighter => _highlighter!;

		PlayerState? _state;
		PlayerState State => _state!;

		public ClientSide(ICoreClientAPI api, ILogger logger)
		{
			ApiHelper.Api = api;
			//TODO: save and load states
			ApiHelper.SetHandler<ConfigData>(d =>
			{
				_config = ModConfig.Get(d, logger);
				_highlighter = new(Config);
				_state = new(Config);

				ApiHelper.SetHandler<BlockPlaced>(_ =>
				{
					if (State.BlockPlaced() && _state.IsHighlighted)
					{
						Highlight();
					}
				})
				.SetMessageHandler<BlockBroken>(_ =>
				{
					if (State.BlockRemoved() && _state.IsHighlighted)
					{
						Highlight();
					}
				})
				//TODO: define behavior when rod added
				.SetMessageHandler<LightningRodPlaced>(p => api.SendChatMessage($"Rod placed at {p.Pos}"))
				.SetMessageHandler<LightningRodBroken>(b =>
				{
					var index = _state.Rods.IndexOf(r => r.Position == b.Pos);
					if (index >= 0)
					{
						Remove(index);
					}
				});
			});

			api.ChatCommands.GetOrCreate("lightmyrod").WithAlias("lmr")
				.RequiresPlayer()
				.BeginSub("test")
					.HandleWith(OnTest)
				.EndSub();

			api.ChatCommands.GetOrCreate("lightmyrod").WithAlias("lmr")
				.RequiresPrivilege(Privilege.chat)
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
				.EndSub()
				.BeginSub("walk")
				.EndSub();
		}

		TextCommandResult OnTest(TextCommandCallingArgs args)
		{
			var player = ApiHelper.Player;
			var playerPos = player.Entity.Pos;
			var chunkSize = GlobalConstants.ChunkSize;
			FastVec2i chunk2D = new((int)Math.Floor(playerPos.X / chunkSize), (int)Math.Floor(playerPos.Z / chunkSize));
			FastVec2i start = new(chunk2D.X - 5, chunk2D.Z - 5);
			FastVec2i end = new(chunk2D.X + 5, chunk2D.Z + 5);

			int rodId = ApiHelper.GetBlock("lightningrod")!.Id;
			var mapSize = ApiHelper.MapSize;

			List<BlockPos> rods = [];
			for (var x = start.X; x <= end.X; x++)
			{
				for (var z = start.Z; z <= end.Z; z++)
			{
					for (var y = mapSize.Y / chunkSize - 1; y >= 0; y--)
					{
						var chunk = ApiHelper.Api.World.ChunkProvider.GetChunk(x, y, z);
						if (chunk.Empty)
						{
							continue;
						}

						chunk.Unpack();
						if (!chunk.Data.ContainsBlock(rodId))
						{
							continue;
						}

						foreach (var (pos, entity) in chunk.BlockEntities)
						{
							if (entity.Block.Id == rodId)
							{
								rods.Add(pos);
							}
						}
					}
				}
			}
			player.ShowChatNotification($"{rods.Count}");

			return TextCommandResult.Success($"Done");
		}

		TextCommandResult OnAdd(TextCommandCallingArgs args)
		{
			BlockSelection? selected = ApiHelper.Player.CurrentBlockSelection;
			if (selected is null || selected.Block.Code != "lightningrod")
			{
				return TextCommandResult.Success($"You need to be aiming at a lightning rod!");
			}

			var rod = State.TryAdd(selected.Position);
			if (rod is null)
			{
				return TextCommandResult.Success($"That one is already on the list!");
			}

			if (State.IsHighlighted && !rod.Covered)
			{
				Highlight();
			}
			return TextCommandResult.Success($"Rod at {ApiHelper.GetLocalPosition(rod.Position)} added to the list!");
		}

		TextCommandResult OnAddAll(TextCommandCallingArgs args)
		{
			var player = ApiHelper.Player;
			var playerPos = player.Entity.Pos.AsBlockPos;
			var start = new BlockPos(playerPos.X - 128, 1, playerPos.Z - 128);
			var end = new BlockPos(playerPos.X + 128, ApiHelper.MapSizeY, playerPos.Z + 128);

			player.ShowChatNotification($"Searching...");

			List<BlockPos> positions = [];
			ApiHelper.WalkBlocks(start, end, (block, x, y, z) =>
			{
				if (block.Code == "lightningrod")
				{
					var pos = new BlockPos(x, y, z);
					positions.Add(pos);

					player.ShowChatNotification($"Found at {pos}");
				}
			});
			if (positions.Count == 0)
			{
				return TextCommandResult.Success($"No rod spotted!");
			}

			var added = State.TryAdd(positions);
			if (added != 0 && State.IsHighlighted)
			{
				Highlight();
			}
			return TextCommandResult.Success($"{added} rods registered!");
		}

		//TODO: multi => maybe set a distance at which stuff got unhi
		//TODO: perfs => highlight through threads, try Stopwatch different parts of algo

		TextCommandResult OnList(TextCommandCallingArgs args)
		{
			if (State.IsEmpty)
			{
				return TextCommandResult.Success($"List empty!");
			}

			foreach (var (index, rod) in State.IndexedRods)
			{
				ApiHelper.Player.ShowChatNotification($"{index}: {ApiHelper.GetLocalPosition(rod.Position)}");
			}
			return TextCommandResult.Success();
		}

		TextCommandResult OnClear(TextCommandCallingArgs args)
		{
			if (!State.IsEmpty)
			{
				Highlighter.Unhi(ApiHelper.Player);

				State.Clear();
			}
			return TextCommandResult.Success($"All cleared!");
		}

		TextCommandResult OnRemove(TextCommandCallingArgs args)
		{
			var index = (int)args.Parsers[0].GetValue();

			if (Remove(index))
			{
				return TextCommandResult.Success($"Rod unlisted!");
			}
			return TextCommandResult.Success($@"Wrong index : use "".lmr list"" to choose an index from!");
		}

		TextCommandResult OnHi(TextCommandCallingArgs args)
		{
			var player = ApiHelper.Player;
			if (!State.IsHighlighted)
			{
				State.IsHighlighted = true;

				if (!State.IsEmpty)
				{
					Highlight();

					var covered = State.CoveredRods;
					if (covered.Length > 0)
					{
						player.ShowChatNotification($"Those rods have blocks above them :");

						foreach (var rod in covered)
						{
							player.ShowChatNotification($"{rod.LocalPosition}");
						}
					}
				}
			}
			return TextCommandResult.Success($"Highlight is on!");
		}

		TextCommandResult OnUnhi(TextCommandCallingArgs args)
		{
			if (State.IsHighlighted)
			{
				State.IsHighlighted = false;

				if (!State.IsEmpty)
				{
					Highlighter.Unhi(ApiHelper.Player);
				}
			}
			return TextCommandResult.Success("Highlight is off!");
		}

		bool Remove(int index)
		{
			var removed = State.TryRemove(index);
			if (removed && State.IsHighlighted)
			{
				Highlight();
			}
			return removed;
		}

		void Highlight()
		{
			Highlighter.Hightlight(State, ApiHelper.Player);
		}

		public void Dispose()
		{

			GC.SuppressFinalize(this);
		}
	}

	static class ApiHelper
	{
		static ICoreClientAPI? _api;
		public static ICoreClientAPI Api
		{
			get { return _api!; }
			set { _api = value; }
		}
		public static IClientPlayer Player => Api.World.Player;
		public static Vec3i MapSize => Api.World.BlockAccessor.MapSize;
		public static int MapSizeY => Api.World.BlockAccessor.MapSizeY;

		public static IWorldChunk GetChunk(BlockPos pos) => Api.World.BlockAccessor.GetChunkAtBlockPos(pos);
		public static void WalkBlocks(BlockPos min, BlockPos max, Action<Block, int, int, int> onBlock) =>
			Api.World.BlockAccessor.WalkBlocks(min, max, onBlock);
		public static Vec3i GetLocalPosition(BlockPos pos) => pos.ToLocalPosition(Api);
		public static int GetRainMapHeight(BlockPos pos) => Api.World.BlockAccessor.GetRainMapHeightAt(pos);
		public static bool IsRainMap(BlockPos pos) => GetRainMapHeight(pos) == pos.Y;
		public static void UnhiBlocks(IPlayer player, int channel) => Api.World.HighlightBlocks(player, channel, [], []);
		public static void HighlightBlocks(IPlayer player, int channel, List<BlockPos> positions, List<int> colors) =>
			Api.World.HighlightBlocks(player, channel, positions, colors);
		public static IClientNetworkChannel SetHandler<T>(NetworkServerMessageHandler<T> handler) =>
			Api.Network.GetChannel(LMRModSystem.CHANNEL_ID).SetMessageHandler(handler);
		public static ClientConfig LoadConfig(string filename) => Api.LoadModConfig<ClientConfig>(filename);
		public static Block? GetBlock(string name) => Api.World.GetBlock(name);
	}
}
