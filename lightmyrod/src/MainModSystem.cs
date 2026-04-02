using LightMyRod.Client;
using LightMyRod.Server;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace LightMyRod
{
	public class LMRModSystem : ModSystem
	{
		ClientSide? _client;
		public ClientSide Client => _client!;

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
			_client = new(api, Mod.Logger);
		}

		public override void StartServerSide(ICoreServerAPI api)
		{
			_server = new(api, Mod.Logger);
		}

		public override void Dispose()
		{
			_client?.Dispose();
			_server?.Dispose();
		}
	}

	[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
	public class ConfigData
	{
		public required float MaxRadius { get; set; }
		public required float ArtificialElevation { get; set; }
		public required float ElevationAttractivenessMultiplier { get; set; }
		public required bool CenterProtectionOnRod { get; set; }
		public required bool FixFireStartingAlgorithm { get; set; }
		public required int FireStartRadius { get; set; }
	}

	[ProtoContract]
	public class BlockPlaced {}

	[ProtoContract]
	public class BlockBroken {}

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