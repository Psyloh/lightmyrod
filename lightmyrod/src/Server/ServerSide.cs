using HarmonyLib;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace LightMyRod.Server
{
	[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
	class ServerConfig(ConfigData data)
	{
		const string FILENAME = "lightmyrod-server.json";

		public ConfigData Data => data;

		public static ServerConfig Get(ILogger logger)
		{
			ConfigData data;
			try
			{
				data = ApiHelper.LoadConfig(FILENAME);
				data ??= GetDefault();
			}
			catch (Exception e)
			{
				logger.Warning("Configuration file corrupted - loading default settings! Please fix or delete the file...");
				logger.Error(e);

				return new(GetDefault());
			}
			ApiHelper.Api.StoreModConfig(data, FILENAME);

			return new(data);
		}

		static ConfigData GetDefault() => new()
		{
			MaxRadius = 40,
			ArtificialElevation = 5,
			ElevationAttractivenessMultiplier = 2,
			CenterProtectionOnRod = false,
			FixFireStartingAlgorithm = false,
			FireStartRadius = 1
		};
	}

	public class ServerSide: IDisposable
	{
		readonly ServerConfig _config;
		
		public ServerSide(ICoreServerAPI api, ILogger logger)
		{
			ApiHelper.Api = api;

			_config = ServerConfig.Get(logger);

			Patch_BEBehaviorAttractsLightning_Initialize.Data = _config.Data;
			Patch_BEBehaviorAttractsLightning_OnLightningStart.Data = _config.Data;
			Patch_ModSystemFireFromLightning_OnLightningImpactEnd.Data = _config.Data;

			var harmony = new Harmony(LMRModSystem.CHANNEL_ID);
			harmony.PatchAll();

			api.Event.DidPlaceBlock += Event_DidPlaceBlock;
			api.Event.BreakBlock += Event_BreakBlock;
			api.Event.DidBreakBlock += Event_DidBreakBlock;
			api.Event.PlayerJoin += Event_PlayerJoin;
		}

		private void Event_PlayerJoin(IServerPlayer byPlayer)
		{
			ApiHelper.SendData(_config.Data, byPlayer);
		}

		private void Event_DidPlaceBlock(IServerPlayer byPlayer, int oldblockId, BlockSelection blockSel, ItemStack withItemStack)
		{
			if (withItemStack.Class == EnumItemClass.Block && withItemStack.Block.Code == "lightningrod")
			{
				ApiHelper.Broadcast(new LightningRodPlaced { Pos = blockSel.Position });
			}
			else
			{
				ApiHelper.Broadcast(new BlockPlaced());
			}
		}
		private void Event_BreakBlock(IServerPlayer byPlayer, BlockSelection blockSel, ref float dropQuantityMultiplier, ref EnumHandling handling)
		{
			if (blockSel.Block.Code == "lightningrod")
			{
				ApiHelper.Broadcast(new LightningRodBroken { Pos = blockSel.Position });
			}
		}
		private void Event_DidBreakBlock(IServerPlayer byPlayer, int oldblockId, BlockSelection blockSel)
		{
			ApiHelper.Broadcast(new BlockBroken());
		}

		public void Dispose()
		{
			ApiHelper.Event.DidPlaceBlock += Event_DidPlaceBlock;
			ApiHelper.Event.BreakBlock -= Event_BreakBlock;
			ApiHelper.Event.DidBreakBlock -= Event_DidBreakBlock;

			GC.SuppressFinalize(this);
		}
	}

	[HarmonyPatch(typeof(BEBehaviorAttractsLightning), nameof(BEBehaviorAttractsLightning.Initialize))]
	static class Patch_BEBehaviorAttractsLightning_Initialize
	{
		public static ConfigData? Data;
		public static void Prefix(ref JsonObject properties)
		{
			properties = new JsonObject(new JObject
			{
				[nameof(Data.ArtificialElevation)] = Data?.ArtificialElevation,
				[nameof(Data.ElevationAttractivenessMultiplier)] = Data?.ElevationAttractivenessMultiplier
			});
		}
	}

	[HarmonyPatch(typeof(BEBehaviorAttractsLightning), "OnLightningStart")]
	static class Patch_BEBehaviorAttractsLightning_OnLightningStart
	{
		public static ConfigData? Data;
		public static void Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			foreach (var instruction in instructions)
			{
				//TODO: replace hardcoded maxRadius with our own
				//TODO: center protective area to rod if relevant
			}
		}
	}

	[HarmonyPatch(typeof(ModSystemFireFromLightning), "ModSystemFireFromLightning_OnLightningImpactEnd")]
	static class Patch_ModSystemFireFromLightning_OnLightningImpactEnd
	{
		public static ConfigData? Data;
		public static void Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			foreach (var instruction in instructions)
			{
				//TODO: fix the checked area if relevant
				//TODO: replace default check radius
			}
		}
	}

	static class ApiHelper
	{
		static ICoreServerAPI? _api;
		public static ICoreServerAPI Api
		{
			get { return _api!; }
			set { _api = value; }
		}
		public static IServerEventAPI Event => Api.Event;

		public static void Broadcast<T>(T message) => Api.Network.GetChannel(LMRModSystem.CHANNEL_ID).BroadcastPacket(message);
		public static ConfigData LoadConfig(string filename) => Api.LoadModConfig<ConfigData>(filename);
		public static void SendData(ConfigData data, IServerPlayer player) =>
			Api.Network.GetChannel(LMRModSystem.CHANNEL_ID).SendPacket(data, [player]);
	}
}