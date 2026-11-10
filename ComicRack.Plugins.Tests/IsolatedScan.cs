using System;
using System.Linq;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;

namespace ComicRack.Plugins.Tests;

// Scans a script folder in an application domain of its own.
// The test process loads Roslyn to compile fixtures and has its own binding redirects, so what a fresh ComicRack
// would load, and whether the ComicRack output folder is complete, only shows in a separate domain.
public sealed class IsolatedScan : MarshalByRefObject
{
	[Serializable]
	public sealed class Result
	{
		public string[] Keys;

		public bool LoadedRoslyn;

		public string Console;

		public string Html;
	}

	// applicationFolder and configFile select the folder and the .config of another application, such as the built ComicRack.exe.
	internal static Result Run(ScriptsFolder scripts, string applicationFolder = null, string configFile = null, string comicInfoHtmlOf = null, string[] moreFolders = null)
	{
		AppDomainSetup setup = AppDomain.CurrentDomain.SetupInformation;
		if (applicationFolder != null)
		{
			setup = new AppDomainSetup { ApplicationBase = applicationFolder, ConfigurationFile = configFile };
		}
		AppDomain domain = AppDomain.CreateDomain("isolated scan", null, setup);
		try
		{
			var scan = (IsolatedScan)domain.CreateInstanceFromAndUnwrap(typeof(IsolatedScan).Assembly.Location, typeof(IsolatedScan).FullName);
			return scan.Scan(scripts.Scripts, scripts.Cache, comicInfoHtmlOf, moreFolders ?? new string[0]);
		}
		finally
		{
			AppDomain.Unload(domain);
		}
	}

	public Result Scan(string scripts, string cache, string comicInfoHtmlOf, string[] moreFolders)
	{
		PluginConsole.Install();
		PluginEngine engine = new PluginEngine(cache);
		engine.Initialize(new StubEnvironment(), scripts);
		foreach (string folder in moreFolders)
		{
			engine.Initialize(new StubEnvironment(), folder);
		}
		engine.CompleteInitialization();
		Command info = engine.GetCommands(PluginEngine.ScriptTypeComicInfoHtml).FirstOrDefault(c => c.Key == comicInfoHtmlOf);
		return new Result
		{
			Keys = engine.GetAllCommands().Select(c => c.Key).Distinct().ToArray(),
			Html = info?.Invoke(new object[] { new[] { new ComicBook { Series = "The Walking Dead" } } }) as string,
			LoadedRoslyn = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)),
			Console = PluginConsole.LinesMentioning("")
		};
	}
}
