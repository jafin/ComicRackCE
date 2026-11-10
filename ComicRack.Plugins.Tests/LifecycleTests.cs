using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;

namespace ComicRack.Plugins.Tests;

public class LifecycleTests
{
	private static readonly object[] NoBooks = { new ComicBook[0] };

	// A constructor that leaves one "x" in created.txt of the plugin folder each time it runs.
	private const string CountedConstructor = "public Plugin(IPluginContext context) { System.IO.File.AppendAllText(System.IO.Path.Combine(context.ScriptPath, \"created.txt\"), \"x\"); }\n";

	private static string Created(string dll)
	{
		string file = Path.Combine(Path.GetDirectoryName(dll), "created.txt");
		return File.Exists(file) ? File.ReadAllText(file) : "";
	}

	[Test]
	public async Task One_instance_of_the_class_is_created_and_shared_by_all_its_hooks()
	{
		using var scripts = new ScriptsFolder();
		scripts.Compile(@"Scripts\Mine", Source.Plugin("Shared", "IComicInfoHtml, IQuickOpenHtml", """
			private static int created;
			private readonly string id;
			public Plugin() { created++; id = "instance " + created; }
			string IComicInfoHtml.Execute(ComicBook[] books) { return id; }
			string IQuickOpenHtml.Execute(ComicBook[] books) { return id; }
			"""));

		var loaded = scripts.Load();

		await Assert.That(loaded.Command("Shared", PluginEngine.ScriptTypeComicInfoHtml).Invoke(NoBooks)).IsEqualTo("instance 1");
		await Assert.That(loaded.Command("Shared", PluginEngine.ScriptTypeQuickOpenHtml).Invoke(NoBooks)).IsEqualTo("instance 1");
		await Assert.That(loaded.Command("Shared", PluginEngine.ScriptTypeComicInfoHtml).Invoke(NoBooks)).IsEqualTo("instance 1");
	}

	[Test]
	public async Task Scan_creates_no_plugin_class()
	{
		using var scripts = new ScriptsFolder();
		string dll = scripts.Compile(@"Scripts\Mine", Source.Plugin("Lazy", "IStartup, IShutdown, IConfig, IDisposable", CountedConstructor + """
			void IStartup.Execute() { }
			bool IShutdown.Execute(bool userIsClosing) { return true; }
			void IConfig.Execute() { }
			public void Dispose() { }
			""", attribute: "Image = \"icon.png\""));

		var loaded = scripts.Load();

		await Assert.That(loaded.Hooks("Lazy")).IsEquivalentTo(new[] { PluginEngine.ScriptTypeStartup, PluginEngine.ScriptTypeShutdown });
		await Assert.That(Created(dll)).IsEqualTo("");
	}

	[Test]
	public async Task Disabled_plugin_is_never_created()
	{
		using var scripts = new ScriptsFolder();
		string dll = scripts.Compile(@"Scripts\Mine", Source.Plugin("Off", "IStartup, IShutdown", CountedConstructor + """
			void IStartup.Execute() { }
			bool IShutdown.Execute(bool userIsClosing) { return true; }
			""", attribute: "Enabled = false"));
		var loaded = scripts.Load();

		loaded.Engine.Invoke(PluginEngine.ScriptTypeStartup, new object[0]);
		loaded.Engine.Invoke(PluginEngine.ScriptTypeShutdown, new object[] { true });
		loaded.Engine.Dispose();

		await Assert.That(loaded.Hooks("Off")).IsEquivalentTo(new[] { PluginEngine.ScriptTypeStartup, PluginEngine.ScriptTypeShutdown });
		await Assert.That(Created(dll)).IsEqualTo("");
	}

	[Test]
	public async Task Constructor_runs_once_on_the_first_hook_with_the_environment_and_the_plugin_folder()
	{
		using var scripts = new ScriptsFolder();
		string dll = scripts.Compile(@"Scripts\Mine", Source.Plugin("Loaded", "IComicInfoHtml, IQuickOpenHtml, IStartup", """
			private readonly string seen;
			public Plugin(IPluginContext context)
			{
				seen = context.ComicRack.CommandPath + "|" + context.ScriptPath;
				System.IO.File.AppendAllText(System.IO.Path.Combine(context.ScriptPath, "created.txt"), "x");
			}
			string IComicInfoHtml.Execute(ComicBook[] books) { return seen; }
			string IQuickOpenHtml.Execute(ComicBook[] books) { return seen; }
			public void Execute() { }
			"""));
		string folder = Path.GetDirectoryName(dll);
		var loaded = scripts.Load();

		loaded.Engine.Invoke(PluginEngine.ScriptTypeStartup, new object[0]);
		object seenByInfo = loaded.Command("Loaded", PluginEngine.ScriptTypeComicInfoHtml).Invoke(NoBooks);
		object seenByQuickOpen = loaded.Command("Loaded", PluginEngine.ScriptTypeQuickOpenHtml).Invoke(NoBooks);

		await Assert.That(seenByInfo).IsEqualTo(folder + "|" + folder);
		await Assert.That(seenByQuickOpen).IsEqualTo(folder + "|" + folder);
		await Assert.That(Created(dll)).IsEqualTo("x");
	}

	[Test]
	public async Task Constructor_with_the_context_wins_over_the_one_without_parameters()
	{
		using var scripts = new ScriptsFolder();
		scripts.Compile(@"Scripts\Mine", Source.Plugin("Both", "IComicInfoHtml", """
			private readonly string used;
			public Plugin() { used = "no parameters"; }
			public Plugin(IPluginContext context) { used = "context"; }
			public string Execute(ComicBook[] books) { return used; }
			"""));

		object used = scripts.Load().Command("Both", PluginEngine.ScriptTypeComicInfoHtml).Invoke(NoBooks);

		await Assert.That(used).IsEqualTo("context");
	}

	[Test]
	public async Task Constructor_that_throws_fails_every_hook_with_its_exception_and_runs_only_once()
	{
		using var scripts = new ScriptsFolder();
		string dll = scripts.Compile(@"Scripts\Mine", Source.Types("""
			[Plugin("Failing")]
			public class Plugin : IStartup, IShutdown
			{
				public Plugin(IPluginContext context)
				{
					System.IO.File.AppendAllText(System.IO.Path.Combine(context.ScriptPath, "created.txt"), "x");
					throw new FormatException("no database zq-constructor");
				}
				public void Execute() { }
				public bool Execute(bool userIsClosing) { return true; }
			}
			[Plugin("Working")]
			public class Working : IComicInfoHtml { public string Execute(ComicBook[] books) { return "works"; } }
			"""));
		var loaded = scripts.Load();
		Command startup = loaded.Command("Failing", PluginEngine.ScriptTypeStartup);
		Command shutdown = loaded.Command("Failing", PluginEngine.ScriptTypeShutdown);

		var first = await Assert.That(() => startup.Invoke(null)).ThrowsExactly<FormatException>();
		var otherHook = await Assert.That(() => shutdown.Invoke(new object[] { true })).ThrowsExactly<FormatException>();
		var again = await Assert.That(() => startup.Invoke(null)).ThrowsExactly<FormatException>();

		await Assert.That(first.Message).IsEqualTo("no database zq-constructor");
		await Assert.That(first.StackTrace).Contains("Fixture.Plugin..ctor");
		await Assert.That(otherHook).IsSameReferenceAs(first);
		await Assert.That(again).IsSameReferenceAs(first);
		await Assert.That(Created(dll)).IsEqualTo("x");
		await Assert.That(PluginConsole.LinesMentioning("zq-constructor").Split('\n').Distinct()).IsEquivalentTo(new[] { "Failing: constructor failed: no database zq-constructor" });
		await Assert.That(loaded.Command("Working", PluginEngine.ScriptTypeComicInfoHtml).Invoke(NoBooks)).IsEqualTo("works");
	}

	[Test]
	public async Task Exception_of_a_hook_reaches_the_caller_unwrapped_and_is_logged()
	{
		using var scripts = new ScriptsFolder();
		scripts.Compile(@"Scripts\Mine", Source.Plugin("Thrower", "IStartup", "public void Execute() { throw new FormatException(\"bad issue number zq-thrown\"); }"));
		Command command = scripts.Load().Command("Thrower", PluginEngine.ScriptTypeStartup);

		var thrown = await Assert.That(() => command.Invoke(null)).ThrowsExactly<FormatException>();

		await Assert.That(thrown.Message).IsEqualTo("bad issue number zq-thrown");
		await Assert.That(thrown.StackTrace).Contains("Fixture.Plugin.Execute");
		await Assert.That(PluginConsole.LinesMentioning("zq-thrown")).IsEqualTo("Thrower.Startup failed: bad issue number zq-thrown");
	}

	[Test]
	public async Task Icon_missing_from_the_plugin_folder_is_taken_from_the_embedded_resources_of_the_plugin_dll()
	{
		using var scripts = new ScriptsFolder();
		string png = scripts.PathTo(@"Resources\icon.png");
		using (var bitmap = new Bitmap(5, 3))
		{
			bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
		}
		scripts.CompileTo(@"Scripts\Mine\WithIcon.dll", "WithIcon" + Guid.NewGuid().ToString("N"), Source.Plugin("Tool", "IBooks", "public void Execute(ComicBook[] books) { }", attribute: "Image = \"icon.png\""), embeddedResourceFile: png);

		Command tool = scripts.Load().Command("Tool", PluginEngine.ScriptTypeBooks);

		await Assert.That(tool.CommandImage).IsNotNull();
		await Assert.That(tool.CommandImage.Size).IsEqualTo(new Size(5, 3));
	}

	[Test]
	public async Task Icon_in_the_plugin_folder_wins_over_the_embedded_one()
	{
		using var scripts = new ScriptsFolder();
		string embedded = scripts.PathTo(@"Resources\icon.png");
		using (var bitmap = new Bitmap(5, 3))
		{
			bitmap.Save(embedded, System.Drawing.Imaging.ImageFormat.Png);
		}
		using (var bitmap = new Bitmap(9, 9))
		{
			bitmap.Save(scripts.PathTo(@"Scripts\Mine\icon.png"), System.Drawing.Imaging.ImageFormat.Png);
		}
		scripts.CompileTo(@"Scripts\Mine\WithIcon.dll", "WithIcon" + Guid.NewGuid().ToString("N"), Source.Plugin("Tool", "IBooks", "public void Execute(ComicBook[] books) { }", attribute: "Image = \"icon.png\""), embeddedResourceFile: embedded);

		Command tool = scripts.Load().Command("Tool", PluginEngine.ScriptTypeBooks);

		await Assert.That(tool.CommandImage.Size).IsEqualTo(new Size(9, 9));
	}

	[Test]
	public async Task Command_with_an_icon_that_exists_nowhere_still_loads_without_an_image()
	{
		using var scripts = new ScriptsFolder();
		scripts.Compile(@"Scripts\Mine", Source.Plugin("Tool", "IBooks", "public void Execute(ComicBook[] books) { }", attribute: "Image = \"icon.png\""));

		Command tool = scripts.Load().Command("Tool", PluginEngine.ScriptTypeBooks);

		await Assert.That(tool).IsNotNull();
		await Assert.That(tool.CommandImage).IsNull();
	}
}
