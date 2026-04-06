using HarmonyLib;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
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
			FixFireStartingAlgorithm = false
		};
	}

	public class ServerSide: IDisposable
	{
		readonly Mod _mod;
		readonly ServerConfig _config;
		readonly Harmony _patcher;

		public ServerSide(ICoreServerAPI api, Mod mod)
		{
			ApiHelper.Api = api;

			_mod = mod;
			_config = ServerConfig.Get(mod.Logger);

			Patch_BEBehaviorAttractsLightning_Initialize.Data = _config.Data;
			Patch_BEBehaviorAttractsLightning_OnLightningStart.Data = _config.Data;
			Patch_ModSystemFireFromLightning_OnLightningImpactEnd.Data = _config.Data;

			_patcher = new Harmony(mod.Info.ModID);
			_patcher.PatchAll();

			api.ChatCommands.Create("test").RequiresPrivilege(Privilege.chat).HandleWith(Test);

			api.Event.DidPlaceBlock += Event_DidPlaceBlock;
			api.Event.BreakBlock += Event_BreakBlock;
			api.Event.DidBreakBlock += Event_DidBreakBlock;
			api.Event.PlayerJoin += Event_PlayerJoin;
		}

		TextCommandResult Test(TextCommandCallingArgs args)
		{
			var original = AccessTools.Method(typeof(BEBehaviorAttractsLightning), "OnLightningStart");
			_patcher.Patch(original);
			return TextCommandResult.Success();
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

			_patcher.UnpatchAll(_mod.Info.ModID);

			GC.SuppressFinalize(this);
		}
	}
	//TODO: think about optimizing hot config changes
	//TODO: deal with the case client-side only (Config)
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
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			if (Data is null)
			{
				return instructions;
			}

			var matcher = new CodeMatcher(instructions, generator);
			matcher.MatchStartForward(
				new(i => i.IsStloc() && i.operand is LocalBuilder lb && lb.LocalIndex == 4),
				new(OpCodes.Ldc_R4),
				new(i => i.IsLdloc() && i.operand is LocalBuilder lb && lb.LocalIndex == 4)
			)
			.Advance().SetOperandAndAdvance(Data.MaxRadius);

			if (Data.CenterProtectionOnRod)
			{
				matcher.MatchEndForward(
					new CodeMatch(i => i.opcode == OpCodes.Ldfld),
					new(i => i.opcode == OpCodes.Conv_R8)
				)
				.InsertAfterAndAdvance(
					new CodeInstruction(OpCodes.Ldc_R8, 0.5),
					new CodeInstruction(OpCodes.Add)
				)
				.MatchEndForward(
					new CodeMatch(i => i.opcode == OpCodes.Ldfld),
					new(i => i.opcode == OpCodes.Conv_R8)
				)
				.InsertAfter(
					new CodeInstruction(OpCodes.Ldc_R8, 0.5),
					new CodeInstruction(OpCodes.Add)
				);
			}
			return matcher.InstructionEnumeration();
		}
	}
	[HarmonyPatch(typeof(ModSystemFireFromLightning), "ModSystemFireFromLightning_OnLightningImpactEnd")]
	static class Patch_ModSystemFireFromLightning_OnLightningImpactEnd
	{
		public static ConfigData? Data;
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			if (Data is null || !Data.FixFireStartingAlgorithm)
			{
				return instructions;
			}

			var rand = AccessTools.Method(typeof(Random), nameof(Random.Next), [typeof(int)]);
			var matcher = new CodeMatcher(instructions, generator);
			matcher = matcher.MatchStartForward(
				new(i => i.LoadsConstant()),
				new(i => i.Calls(rand)),
				new(i => i.LoadsConstant())
			)
			.SetOpcodeAndAdvance(OpCodes.Ldc_I4_3)
			.Advance(4).SetOpcodeAndAdvance(OpCodes.Ldc_I4_3)
			.Advance(4).SetOpcodeAndAdvance(OpCodes.Ldc_I4_3);

			return matcher.InstructionEnumeration();
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