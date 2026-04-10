using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace LightMyRod.Client
{
	public class ClientConfig
	{
		public bool ShowPartial { get; set; } = true;
		public byte[] PartialProtectionColor { get; set; } = [180, 40, 5, 150];
		public byte[] FullProtectionColor { get; set; } = [0, 255, 80, 80];
		public int MaxWalkRadius { get; set; } = 5;
	}
	
	public class ModConfig(ConfigData data, ClientConfig config)
	{
		const string FILENAME = "lightmyrod-client.json";

		public bool ShowPartial => config.ShowPartial;
		public byte[] PartialProtectionColor => config.PartialProtectionColor;
		public byte[] FullProtectionColor => config.FullProtectionColor;
		public int MaxWalkRadius => config.MaxWalkRadius;

		public float MaxRadius => data.MaxRadius;
		public bool CenterProtectionOnRod => data.CenterProtectionOnRod;
		public float ArtificialElevation => data.Vanilla.ArtificialElevation;
		public float ElevationAttractivenessMultiplier => data.Vanilla.ElevationAttractivenessMultiplier;
		public bool FixFireStartingAlgorithm => data.FixFireStartingAlgorithm;

		public int ElevationOffset => (int)Math.Ceiling(ArtificialElevation);
		public int MaxYOffset => GetYOffsetAt(data.MaxRadius);
		public int MaxRadiusOffset => (int)Math.Ceiling(data.MaxRadius);

		public int GetYOffsetAt(float radius) => (int)Math.Ceiling(radius / ElevationAttractivenessMultiplier);
		public float GetRadiusFor(int yOffset)
		{
			var decimalPart = ArtificialElevation % 1;
			if (decimalPart == 0)
			{
				return yOffset * ElevationAttractivenessMultiplier;
			}
			return (yOffset - 1 + decimalPart) * ElevationAttractivenessMultiplier;
		}

		public static ModConfig Get(ConfigData data)
		{
			ClientConfig config;
			try
			{
				config = ApiHelper.LoadConfig(FILENAME);
				config ??= new();
			}
			catch (Exception e)
			{
				ApiHelper.ModLogger.Warning("Configuration file corrupted - loading default settings! Please fix or delete the file...");
				ApiHelper.ModLogger.Error(e);

				return new (data, new ClientConfig());
			}
			ApiHelper.Api.StoreModConfig(config, FILENAME);

			return new ModConfig(data, config);
		}
	}

	public class ClientSide : IDisposable
	{
		ModConfig? _config;
		PlayerState? _state;
		PlayerState State => _state!;

		public ClientSide(ICoreClientAPI api, Mod mod)
		{
			ApiHelper.Api = api;
			ApiHelper.Mod = mod;

			api.Event.LevelFinalize += Event_LevelFinalize;

			//TODO: save and load states
			ApiHelper.SetMessageHandler<ConfigData>(Initialize);
		}

		void Event_LevelFinalize()
		{
			if (ApiHelper.Channel is null)
			{
				Initialize(ConfigData.GetDefault(ApiHelper.Api));
			}
		}

		void Initialize(ConfigData configData)
		{
			_config = ModConfig.Get(configData);
			_state = new(new(_config), _config);

			ApiHelper.Api.ChatCommands.GetOrCreate("lightmyrod").WithAlias("lmr")
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
					.WithArgs(ApiHelper.Api.ChatCommands.Parsers.Int("index"))
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
			return TextCommandResult.Success($"Rod at {ApiHelper.GetLocalPosition(rod.Value.Position)} added to the list!");
		}

		TextCommandResult OnAddAll(TextCommandCallingArgs args)
		{
			var player = ApiHelper.Player;
			var playerPos = player.Entity.Pos;
			var chunkSize = GlobalConstants.ChunkSize;
			FastVec2i playerChunk = new((int)Math.Floor(playerPos.X / chunkSize), (int)Math.Floor(playerPos.Z / chunkSize));
			FastVec2i start = playerChunk - 5;
			FastVec2i end = playerChunk + 5;

			int rodId = ApiHelper.GetBlock("lightningrod")!.Id;
			var mapSizeY = ApiHelper.MapSizeY;

			List<BlockPos> rods = [];
			for (var x = start.X; x <= end.X; x++)
			{
				for (var z = start.Z; z <= end.Z; z++)
				{
					for (var y = 0; y < mapSizeY / chunkSize; y++)
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

								player.ShowChatNotification($"Found at {pos}");
							}
						}
					}
				}
			}

			if (rods.Count == 0)
			{
				return TextCommandResult.Success($"No rod spotted!");
			}

			var added = State.TryAddRange(rods);
			return TextCommandResult.Success($"{added} rods registered!");
		}
		//TODO: multi => test highlighted blocks behavior when I'm far away
		TextCommandResult OnList(TextCommandCallingArgs args)
		{
			var rods = State.IndexedRods;
			if (!rods.Any())
			{
				return TextCommandResult.Success($"List empty!");
			}

			foreach (var (index, rod) in rods)
			{
				ApiHelper.Player.ShowChatNotification($"{index}: {ApiHelper.GetLocalPosition(rod.Position)}");
			}
			return TextCommandResult.Success();
		}

		TextCommandResult OnClear(TextCommandCallingArgs args)
		{
			State.Clear();
			return TextCommandResult.Success($"All cleared!");
		}

		TextCommandResult OnRemove(TextCommandCallingArgs args)
		{
			var index = (int)args.Parsers[0].GetValue();
			if (State.TryRemove(index))
			{
				return TextCommandResult.Success($"Rod unlisted!");
			}
			return TextCommandResult.Success($@"Wrong index : use "".lmr list"" to choose an index from!");
		}

		TextCommandResult OnHi(TextCommandCallingArgs args)
		{
			State.Highlight();

			var player = ApiHelper.Player;
			var inactive = State.InactiveRods;
			if (inactive.Any())
			{
				player.ShowChatNotification($"Those rods are inactive (should have blocks above them) :");

				foreach (var rod in inactive)
				{
					player.ShowChatNotification($"{rod.LocalPosition}");
				}
			}
			return TextCommandResult.Success($"Highlight is on!");
		}

		TextCommandResult OnUnhi(TextCommandCallingArgs args)
		{
			State.Unhi();
			
			return TextCommandResult.Success("Highlight is off!");
		}

		public void Dispose()
		{
			ApiHelper.Event.LevelFinalize += Event_LevelFinalize;

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

		static Mod? _mod;
		public static Mod Mod
		{
			get { return _mod!; }
			set { _mod = value; }
		}

		public static ILogger Logger => Api.Logger;
		public static ILogger ModLogger => Mod.Logger;

		public static IClientPlayer Player => Api.World.Player;
		public static IClientEventAPI Event => Api.Event;
		public static int MapSizeY => Api.World.MapSizeY;
		public static IClientNetworkChannel Channel => Api.Network.GetChannel(LMRModSystem.CHANNEL_ID);

		public static Vec3i GetLocalPosition(BlockPos pos) => pos.ToLocalPosition(Api);
		public static int GetRainMapHeight(BlockPos pos) => Api.World.BlockAccessor.GetRainMapHeightAt(pos);
		public static int GetRainMapHeight(FastVec2i coords) => Api.World.BlockAccessor.GetRainMapHeightAt(coords.X, coords.Z);
		public static bool IsRainMap(BlockPos pos) => GetRainMapHeight(pos) == pos.Y;
		public static IClientNetworkChannel SetMessageHandler<T>(NetworkServerMessageHandler<T> handler) => Channel.SetMessageHandler(handler);
		public static ClientConfig LoadConfig(string filename) => Api.LoadModConfig<ClientConfig>(filename);
		public static Block? GetBlock(string name) => Api.World.GetBlock(name);
		public static void MainThreadTask(Action action, string code) => Api.Event.EnqueueMainThreadTask(action, code);
		public static void ChatMessage(string message) => Player.ShowChatNotification(message);
		public static void ChatMessageFromParallel(string message) => MainThreadTask(() => ChatMessage(message), "chatMessage");
	}
}
