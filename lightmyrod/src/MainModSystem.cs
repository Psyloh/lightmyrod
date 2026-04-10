using LightMyRod.Client;
using LightMyRod.Server;
using ProtoBuf;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace LightMyRod
{
	public class LMRModSystem : ModSystem
	{
		ClientSide? _client;
		ServerSide? _server;

		public const string CHANNEL_ID = "lightmyrod";

		public override void Start(ICoreAPI api)
		{
			api.Network.RegisterChannel(CHANNEL_ID).
				RegisterMessageType<ConfigData>().
				RegisterMessageType<BlockBroken>().
				RegisterMessageType<BlockPlaced>().
				RegisterMessageType<LightningRodBroken>().
				RegisterMessageType<LightningRodPlaced>();
		}

		public override void StartClientSide(ICoreClientAPI api)
		{
			_client = new(api, Mod);
		}

		public override void StartServerSide(ICoreServerAPI api)
		{
			_server = new(api, Mod);
		}

		public override void Dispose()
		{
			_client?.Dispose();
			_server?.Dispose();
		}
	}

	[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
	public class VanillaConfigData
	{
		public required float ArtificialElevation { get; set; }
		public required float ElevationAttractivenessMultiplier { get; set; }

		public static VanillaConfigData GetDefault(ICoreAPI api)
		{
			var block = api.World.GetBlock("lightningrod");
			var behaviorType = block?.BlockEntityBehaviors.FirstOrDefault(bht => bht.Name == "AttractsLightning");
			var config = new VanillaConfigData
			{
				ArtificialElevation = 1,
				ElevationAttractivenessMultiplier = 1,
			};
			return behaviorType?.properties?.AsObject(config) ?? config;
		}
	}

	[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
	public class ConfigData
	{
		public required float MaxRadius { get; set; }
		public required VanillaConfigData Vanilla {  get; set; }
		public required bool CenterProtectionOnRod { get; set; }
		public required bool FixFireStartingAlgorithm { get; set; }

		public static ConfigData GetDefault(ICoreAPI api) => new()
		{
			MaxRadius = 40,
			Vanilla = VanillaConfigData.GetDefault(api),
			CenterProtectionOnRod = false,
			FixFireStartingAlgorithm = false
		};
	}

	[ProtoContract]
	public class BlockPlaced
	{
		public static readonly BlockPlaced Instance = new();
	}

	[ProtoContract]
	public class BlockBroken
	{
		public static readonly BlockBroken Instance = new();
	}

	[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
	public class LightningRodPlaced
	{
		public required BlockPos Pos { get; set; }
	}

	[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
	public class LightningRodBroken
	{
		public required BlockPos Pos { get; set; }
	}
}