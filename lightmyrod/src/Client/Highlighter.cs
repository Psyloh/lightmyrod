using LightMyRod.Client.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace LightMyRod.Client
{
	struct Visual(BlockPos origin, MeshRef mesh)
	{
		public readonly BlockPos Origin => origin;
		public readonly MeshRef Mesh => mesh;
	}
	//TODO: Study more of HighlightSystem to check stuff about anchoring
	public class Highlighter(ModConfig config) : IRenderer, IDisposable
	{
		public double RenderOrder => 0.89;
		public int RenderRange => 256;

		readonly Dictionary<Coverage, Visual> _visuals = [];

		public async Task Highlight(Registry registry)
		{
			var meshInfos = registry.MeshInfos;
			ApiHelper.Api.Event.EnqueueMainThreadTask(() => ApiHelper.Api.Event.RegisterRenderer(this, EnumRenderStage.OIT), "lmr");

			var sw = Stopwatch.StartNew();
			foreach (var (coverage, info) in meshInfos)
			{
				if (info.Cubes.Length == 0) continue;
				var cubes = info.Cubes;
				var origin = info.Origin;
				MeshData mesh = new(cubes.Length * 4 * 6, cubes.Length * 6 * 6, false, false, true, false);
				var shadings = CubeMeshUtil.DefaultBlockSideShadingsByFacing;
				var color = GetColor(coverage);

				foreach (var cube in cubes)
				{
					if (cube.Facing == 0)
					{
						continue;
					}

					foreach (var face in BlockFacing.ALLFACES)
					{
						if ((cube.Facing & face.Flag) != 0)
						{
							var center = new Vec3f
							{
								X = cube.Position.X - origin.X + 0.5f,
								Y = cube.Position.InternalY - origin.Y + 0.5f,
								Z = cube.Position.Z - origin.Z + 0.5f
							};
							ModelCubeUtilExt.AddFaceSkipTex(mesh, face, center, Vec3f.One, color, shadings[face.Index]);
						}
					}
				}
				sw.Stop();
				ApiHelper.ModLogger.Error($"Mesh data ready : {sw.Elapsed} | {mesh.VerticesCount} vertices");
				ApiHelper.Api.Event.EnqueueMainThreadTask(() => _visuals[coverage] = new(origin, ApiHelper.Api.Render.UploadMesh(mesh)), "lmr");
			}
		}
		//TODO: try to check if we could make targeted updates on block placed/broken events
		public void Update(PlayerState state)
		{

		}

		int GetColor(Coverage coverage) => coverage switch
		{
			Coverage.Partial => ColorUtil.ColorFromRgba(config.PartialProtectionColor),
			Coverage.Full => ColorUtil.ColorFromRgba(config.FullProtectionColor),
			_ => throw new NotImplementedException()
		};

		public void Unhi()
		{
			ApiHelper.Api.Event.UnregisterRenderer(this, EnumRenderStage.OIT);

			foreach (var (coverage, visual) in _visuals)
			{
				_visuals.Remove(coverage);
				visual.Mesh.Dispose();
			}
		}

		public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
		{
			if (_visuals.Count == 0)
			{
				return;
			}

			var highlighter = ShaderPrograms.Blockhighlights;
			highlighter.Use();

			foreach (var context in _visuals.Values)
			{
				ApiHelper.Api.Render.GlPushMatrix();
				ApiHelper.Api.Render.GlLoadMatrix(ApiHelper.Api.Render.CameraMatrixOrigin);

				var cameraPos = ApiHelper.Player.Entity.CameraPos;
				ApiHelper.Api.Render.GlTranslate(context.Origin.X - cameraPos.X, context.Origin.Y - cameraPos.Y, context.Origin.Z - cameraPos.Z);

				highlighter.ModelViewMatrix = ApiHelper.Api.Render.CurrentModelviewMatrix;
				highlighter.ProjectionMatrix = ApiHelper.Api.Render.CurrentProjectionMatrix;

				ApiHelper.Api.Render.RenderMesh(context.Mesh);
				ApiHelper.Api.Render.GlPopMatrix();
			}
			highlighter.Stop();
		}

		public void Dispose()
		{
			Unhi();

			GC.SuppressFinalize(this);
		}
	}
}