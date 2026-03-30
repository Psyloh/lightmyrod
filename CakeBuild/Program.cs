using Cake.Common;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Clean;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Core;
using Cake.Frosting;
using Cake.Json;
using Vintagestory.API.Common;

namespace CakeBuild
{
	public static class Program
	{
		public static int Main(string[] args)
		{
			return new CakeHost()
				.UseContext<BuildContext>()
				.Run(args);
		}
	}

	public class BuildContext : FrostingContext
	{
		public const string ProjectName = "lightmyrod";
		public string BuildConfiguration { get; }
		public string Version { get; }
		public string Name { get; }

		public BuildContext(ICakeContext context)
			: base(context)
		{
			BuildConfiguration = context.Argument("configuration", "Release");
			var modInfo = context.DeserializeJsonFromFile<ModInfo>($"../{ProjectName}/resources/modinfo.json");
			Version = modInfo.Version;
			Name = modInfo.ModID;
		}
	}

	[TaskName("Build")]
	public sealed class BuildTask : FrostingTask<BuildContext>
	{
		public override void Run(BuildContext context)
		{
			context.DotNetClean($"../{BuildContext.ProjectName}/{BuildContext.ProjectName}.csproj",
				new DotNetCleanSettings
				{
					Configuration = context.BuildConfiguration
				});


			context.DotNetPublish($"../{BuildContext.ProjectName}/{BuildContext.ProjectName}.csproj",
				new DotNetPublishSettings
				{
					Configuration = context.BuildConfiguration
				});
		}
	}

	[TaskName("Package")]
	[IsDependentOn(typeof(BuildTask))]
	public sealed class PackageTask : FrostingTask<BuildContext>
	{
		public override void Run(BuildContext context)
		{
			context.EnsureDirectoryExists("../Releases");
			context.CleanDirectory("../Releases");
			context.EnsureDirectoryExists($"../Releases/{context.Name}");
			context.CopyFiles($"../{BuildContext.ProjectName}/bin/{context.BuildConfiguration}/Mods/mod/publish/*", $"../Releases/{context.Name}");
			context.CopyFile($"../{BuildContext.ProjectName}/resources/modinfo.json", $"../Releases/{context.Name}/modinfo.json");
			if (context.FileExists($"../{BuildContext.ProjectName}/resources/modicon.png"))
			{
				context.CopyFile($"../{BuildContext.ProjectName}/resources/modicon.png", $"../Releases/{context.Name}/modicon.png");
			}
			context.Zip($"../Releases/{context.Name}", $"../Releases/{context.Name}_{context.Version}.zip");
		}
	}

	[TaskName("Default")]
	[IsDependentOn(typeof(PackageTask))]
	public class DefaultTask : FrostingTask
	{
	}
}