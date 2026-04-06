using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using static System.Net.Mime.MediaTypeNames;

namespace LightMyRod.Client
{
	public class ClientConfig
	{
		public bool ShowPartial { get; set; } = true;
		public byte[] PartialProtectionColor { get; set; } = [180, 40, 5, 150];
		public byte[] FullProtectionColor { get; set; } = [0, 255, 80, 80];
		public int WalkRadius { get; set; } = 5;
	}

	public class ModConfig(ConfigData data, ClientConfig config)
	{
		const string FILENAME = "lightmyrod-client.json";

		public bool ShowPartial => config.ShowPartial;
		public byte[] PartialProtectionColor => config.PartialProtectionColor;
		public byte[] FullProtectionColor => config.FullProtectionColor;
		public int WalkRadius => config.WalkRadius;

		public float MaxRadius => data.MaxRadius;
		public bool CenterProtectionOnRod => data.CenterProtectionOnRod;
		public float ArtificialElevation => data.ArtificialElevation;
		public bool FixFireStartingAlgorithm => data.FixFireStartingAlgorithm;

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

		public ClientSide(ICoreClientAPI api, Mod mod)
		{
			ApiHelper.Api = api;
			//TODO: save and load states
			ApiHelper.SetHandler<ConfigData>(d =>
			{
				_config = ModConfig.Get(d, mod.Logger);
				_state = new(Config);
				_highlighter = new(Config, _state);

				ApiHelper.Api.Event.RegisterRenderer(_renderer, EnumRenderStage.OIT);

				ApiHelper.SetHandler<BlockPlaced>(_ =>
				{
					if (State.BlockPlaced())
					{
						Highlighter.Highlight();
					}
				})
				.SetMessageHandler<BlockBroken>(_ =>
				{
					if (State.BlockRemoved())
					{
						Highlighter.Highlight();
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

				api.ChatCommands.GetOrCreate("test")
					.HandleWith(OnTest);

				api.ChatCommands.GetOrCreate("lightmyrod").WithAlias("lmr")
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
			});
		}

		class Renderer : IRenderer
		{
			public double RenderOrder => 0.89;
			public int RenderRange => 200;

			public void Dispose()
			{
				if (Context != null)
				{
					ApiHelper.Api.Render.DeleteMesh(Context.MeshRef);
				}
			}

			public HighlightContext? Context;

			public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
			{
				if (Context == null)
				{
					return;
				}
				
				var highlighter = ShaderPrograms.Blockhighlights;
				highlighter.Use();

				ApiHelper.Api.Render.GlPushMatrix();
				ApiHelper.Api.Render.GlLoadMatrix(ApiHelper.Api.Render.CameraMatrixOrigin);

				var cameraPos = ApiHelper.Player.Entity.CameraPos;
				ApiHelper.Api.Render.GlTranslate(Context.Pos.X - cameraPos.X, Context.Pos.Y - cameraPos.Y, Context.Pos.Z - cameraPos.Z);

				highlighter.ModelViewMatrix = ApiHelper.Api.Render.CurrentModelviewMatrix;
				highlighter.ProjectionMatrix = ApiHelper.Api.Render.CurrentProjectionMatrix;

				ApiHelper.Api.Render.RenderMesh(Context.MeshRef);
				ApiHelper.Api.Render.GlPopMatrix();

				highlighter.Stop();
			}
		}

		Renderer _renderer = new();

		TextCommandResult OnTest(TextCommandCallingArgs args)
		{
			MeshData mesh = new(4 * 6, 6 * 6, false, false, true, false);
			var shadings = CubeMeshUtil.DefaultBlockSideShadingsByFacing;
			var red = ColorUtil.ColorFromRgba([255, 0, 0, 150]);

			foreach (var facing in BlockFacing.ALLFACES)
			{
				ModelCubeUtilExt.AddFaceSkipTex(mesh, facing, Vec3f.Zero, Vec3f.One, red, shadings[facing.Index]);
			}

			var pos = ApiHelper.Player.Entity.Pos.AsBlockPos;
			_renderer.Context = new (pos, ApiHelper.Api.Render.UploadMesh(mesh));

			return TextCommandResult.Success();
		}

		class HighlightContext(BlockPos pos, MeshRef meshRef)
		{
			public BlockPos Pos => pos;
			public MeshRef MeshRef = meshRef;
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

			if (!rod.Covered)
			{
				Highlighter.Highlight();
			}
			return TextCommandResult.Success($"Rod at {ApiHelper.GetLocalPosition(rod.Position)} added to the list!");
		}

		TextCommandResult OnAddAll(TextCommandCallingArgs args)
		{
			var player = ApiHelper.Player;
			var playerPos = player.Entity.Pos;
			var chunkSize = GlobalConstants.ChunkSize;
			FastVec2i chunk2D = new((int)Math.Floor(playerPos.X / chunkSize), (int)Math.Floor(playerPos.Z / chunkSize));
			FastVec2i start = new(chunk2D.X - 5, chunk2D.Z - 5);
			FastVec2i end = new(chunk2D.X + 5, chunk2D.Z + 5);

			int rodId = ApiHelper.GetBlock("lightningrod")!.Id;
			var mapSize = ApiHelper.MapSize;

			player.ShowChatNotification($"Searching...");

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

			var added = State.TryAdd(rods);
			if (added != 0)
			{
				Highlighter.Highlight();
			}
			return TextCommandResult.Success($"{added} rods registered!");
		}

		//TODO: multi => test highlighted blocks behavior when I'm far away
		//TODO: perfs => search for a way to highlight through threads, try Stopwatch different parts of algo

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
				Highlighter.Unhi();

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
		//TODO: refacto highliter.hi/unhi to set state, replace default highlight with Update
		TextCommandResult OnHi(TextCommandCallingArgs args)
		{
			var player = ApiHelper.Player;
			if (!State.IsHighlighted)
			{
				State.IsHighlighted = true;

				if (!State.IsEmpty)
				{
					Task.Run(() => Highlighter.Highlight());

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
					Highlighter.Unhi();
				}
			}
			return TextCommandResult.Success("Highlight is off!");
		}

		bool Remove(int index)
		{
			var removed = State.TryRemove(index);
			if (removed)
			{
				Highlighter.Highlight();
			}
			return removed;
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
		public static Vec3i GetLocalPosition(BlockPos pos) => pos.ToLocalPosition(Api);
		public static int GetRainMapHeight(BlockPos pos) => Api.World.BlockAccessor.GetRainMapHeightAt(pos);
		public static bool IsRainMap(BlockPos pos) => GetRainMapHeight(pos) == pos.Y;
		public static void UnhiBlocks(int channel) => Api.World.HighlightBlocks(Player, channel, [], []);
		public static void HighlightBlocks(int channel, List<BlockPos> positions, List<int> colors) =>
			MainThreadTask(() => Api.World.HighlightBlocks(Player, channel, positions, colors), "highlight");
		public static IClientNetworkChannel SetHandler<T>(NetworkServerMessageHandler<T> handler) =>
			Api.Network.GetChannel(LMRModSystem.CHANNEL_ID).SetMessageHandler(handler);
		public static ClientConfig LoadConfig(string filename) => Api.LoadModConfig<ClientConfig>(filename);
		public static Block? GetBlock(string name) => Api.World.GetBlock(name);
		public static void MainThreadTask(Action action, string code) => Api.Event.EnqueueMainThreadTask(action, code);
		public static void ChatMessage(string message) => Player.ShowChatNotification(message);
		public static void ChatMessageFromParallel(string message) => MainThreadTask(() => ChatMessage(message), "chatMessage");
	}
}
