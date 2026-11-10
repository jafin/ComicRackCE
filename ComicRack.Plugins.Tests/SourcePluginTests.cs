using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;

namespace ComicRack.Plugins.Tests;

public class SourcePluginTests
{
	private static readonly object[] NoBooks = { new ComicBook[0] };

	private static string Returning(string key, string expression)
	{
		return Source.Plugin(key, "IComicInfoHtml", "public string Execute(ComicBook[] books) { return " + expression + "; }");
	}

	private static object Run(LoadedPlugins loaded, string key) => loaded.Command(key, PluginEngine.ScriptTypeComicInfoHtml).Invoke(NoBooks);

	private static string[] CachedAssemblies(ScriptsFolder scripts) => Directory.Exists(scripts.Cache) ? Directory.GetFiles(scripts.Cache, "*.dll") : new string[0];

	[Test]
	public async Task Folder_with_a_csplugin_file_is_compiled_and_its_plugin_classes_register()
	{
		using var scripts = new ScriptsFolder();
		string marker = scripts.Write(@"Scripts\Src\Mine.csplugin", "");
		scripts.Write(@"Scripts\Src\Plugin.cs", Source.Plugin("FromSource", "IComicInfoHtml", Source.Context + "public string Execute(ComicBook[] books) { return context.ScriptPath + \"|\" + context.ComicRack.CommandPath; }"));
		scripts.Write(@"Scripts\Src\Helpers\Second.cs", Source.Plugin("SecondClass", "IStartup", "public void Execute() { }", className: "Second"));
		string folder = Path.GetDirectoryName(marker);

		var loaded = scripts.Load();

		await Assert.That(Run(loaded, "FromSource")).IsEqualTo(folder + "|" + folder);
		await Assert.That(loaded.Hooks("SecondClass")).IsEquivalentTo(new[] { PluginEngine.ScriptTypeStartup });
		await Assert.That(CachedAssemblies(scripts).Select(Path.GetFileName).Single()).StartsWith("Mine-");
	}

	[Test]
	public async Task Config_of_a_source_plugin_is_saved_in_the_source_folder()
	{
		using var scripts = new ScriptsFolder();
		string marker = scripts.Write(@"Scripts\Src\Mine.csplugin", "");
		scripts.Write(@"Scripts\Src\Plugin.cs", Returning("Cfg", "null"));
		Command command = scripts.Load().Command("Cfg", PluginEngine.ScriptTypeComicInfoHtml);

		command.SaveConfig("kept");

		await Assert.That(File.ReadAllText(marker + "-Cfg.config")).IsEqualTo("kept");
	}

	[Test]
	public async Task Unchanged_source_plugin_is_loaded_from_the_cache_without_compiling_again()
	{
		using var scripts = new ScriptsFolder();
		scripts.Write(@"Scripts\Src\Mine.csplugin", "");
		scripts.Write(@"Scripts\Src\Plugin.cs", Returning("Cached", "\"same\""));
		scripts.Load();
		string cached = CachedAssemblies(scripts).Single();
		DateTime compiledAt = File.GetLastWriteTimeUtc(cached);

		var loaded = scripts.Load();

		await Assert.That(Run(loaded, "Cached")).IsEqualTo("same");
		await Assert.That(CachedAssemblies(scripts)).IsEquivalentTo(new[] { cached });
		await Assert.That(File.GetLastWriteTimeUtc(cached)).IsEqualTo(compiledAt);
		string[] console = loaded.ConsoleLinesAboutThisFixture.Split('\n');
		await Assert.That(console.Count(line => line.StartsWith("Compiling source plugin"))).IsEqualTo(1);
		await Assert.That(console.Count(line => line.Contains("is unchanged"))).IsEqualTo(1);
	}

	[Test]
	public async Task Source_plugins_with_the_same_name_in_different_folders_keep_their_own_cache_entries()
	{
		using var scripts = new ScriptsFolder();
		scripts.Write(@"Scripts\One\Mine.csplugin", "");
		scripts.Write(@"Scripts\One\Plugin.cs", Returning("First", "\"one\""));
		scripts.Write(@"Scripts\Two\Mine.csplugin", "");
		scripts.Write(@"Scripts\Two\Plugin.cs", Returning("Second", "\"two\""));
		scripts.Load();
		string[] cached = CachedAssemblies(scripts);

		var loaded = scripts.Load();

		await Assert.That(cached.Length).IsEqualTo(2);
		await Assert.That(Run(loaded, "First")).IsEqualTo("one");
		await Assert.That(Run(loaded, "Second")).IsEqualTo("two");
		await Assert.That(CachedAssemblies(scripts)).IsEquivalentTo(cached);
		string[] console = loaded.ConsoleLinesAboutThisFixture.Split('\n');
		await Assert.That(console.Count(line => line.StartsWith("Compiling source plugin"))).IsEqualTo(2);
		await Assert.That(console.Count(line => line.Contains("is unchanged"))).IsEqualTo(2);
	}

	[Test]
	[Arguments("source")]
	[Arguments("marker")]
	[Arguments("reference")]
	public async Task Source_plugin_is_compiled_again_when_one_of_its_inputs_changes(string input)
	{
		using var scripts = new ScriptsFolder();
		string name = "Dep" + Guid.NewGuid().ToString("N");
		scripts.CompileTo($@"Scripts\Src\lib\{name}.dll", name, "public static class Dep { public const string Text = \"old\"; public static string Now() { return \"old\"; } }");
		string marker = scripts.Write(@"Scripts\Src\Mine.csplugin", $"[Plugin]\nReferences=lib/{name}.dll\n");
		string source = scripts.Write(@"Scripts\Src\Plugin.cs", Returning("Changing", "\"first \" + Dep.Text"));
		object before = Run(scripts.Load(), "Changing");
		string firstAssembly = CachedAssemblies(scripts).Single();

		switch (input)
		{
			case "source":
				File.WriteAllText(source, Returning("Changing", "\"second \" + Dep.Text"));
				break;
			case "marker":
				File.AppendAllText(marker, "LangVersion=7.3\n");
				break;
			case "reference":
				// The first version is loaded and locked, and a constant is copied into the plugin at compile time.
				File.Move(scripts.PathTo($@"Scripts\Src\lib\{name}.dll"), scripts.PathTo($@"Old\{name}.dll"));
				scripts.CompileTo($@"Scripts\Src\lib\{name}.dll", name, "public static class Dep { public const string Text = \"new\"; public static string Now() { return \"new\"; } }");
				break;
		}
		object after = Run(scripts.Load(), "Changing");

		await Assert.That(before).IsEqualTo("first old");
		await Assert.That(after).IsEqualTo(input == "source" ? "second old" : input == "reference" ? "first new" : "first old");
		await Assert.That(CachedAssemblies(scripts).Count(file => file != firstAssembly)).IsEqualTo(1);
	}

	[Test]
	public async Task Compile_error_is_logged_with_file_and_line_and_only_that_plugin_is_skipped()
	{
		using var scripts = new ScriptsFolder();
		scripts.Write(@"Scripts\Broken\Broken.csplugin", "");
		string broken = scripts.Write(@"Scripts\Broken\Plugin.cs", "using cYo.Projects.ComicRack.Plugins;\n[Plugin(\"Broken\")]\npublic class Plugin : IStartup\n{\n\tpublic void Execute() { int x = \"text\"; }\n}\n");
		scripts.Write(@"Scripts\Good\Good.csplugin", "");
		scripts.Write(@"Scripts\Good\Plugin.cs", Returning("GoodSource", "\"ok\""));
		scripts.Compile(@"Scripts\Compiled", Returning("GoodCompiled", "\"ok\""));

		var loaded = scripts.Load();

		await Assert.That(loaded.Engine.GetAllCommands().Select(c => c.Key)).IsEquivalentTo(new[] { "GoodSource", "GoodCompiled" });
		await Assert.That(loaded.ConsoleLinesAboutThisFixture).Contains(broken + "(5,34): error CS0029: Cannot implicitly convert type 'string' to 'int'");
		await Assert.That(CachedAssemblies(scripts).Select(Path.GetFileName).Any(file => file.StartsWith("Broken-"))).IsFalse();
	}

	[Test]
	public async Task Compile_warning_is_logged_once_with_file_and_line_and_the_plugin_still_loads()
	{
		using var scripts = new ScriptsFolder();
		scripts.Write(@"Scripts\Warn\Warn.csplugin", "");
		string source = scripts.Write(@"Scripts\Warn\Plugin.cs", "using cYo.Projects.ComicRack.Plugins;\n[Plugin(\"Warned\")]\npublic class Plugin : IStartup\n{\n\tpublic void Execute() { int unused; }\n}\n");

		var first = scripts.Load();
		string[] firstConsole = first.ConsoleLinesAboutThisFixture.Split('\n');
		var second = scripts.Load();
		string[] secondConsole = second.ConsoleLinesAboutThisFixture.Split('\n');

		await Assert.That(first.Command("Warned", PluginEngine.ScriptTypeStartup)).IsNotNull();
		int compiling = Array.FindIndex(firstConsole, line => line.StartsWith("Compiling source plugin"));
		await Assert.That(firstConsole[compiling + 1]).StartsWith(source + "(5,");
		await Assert.That(firstConsole[compiling + 1]).Contains("warning CS0168");
		await Assert.That(firstConsole[compiling + 2]).EndsWith("compiled with 1 warning(s)");
		await Assert.That(secondConsole.Count(line => line.Contains("warning CS0168"))).IsEqualTo(1);
		await Assert.That(secondConsole.Count(line => line.Contains("compiled with"))).IsEqualTo(1);
	}

	[Test]
	public async Task Reference_of_the_csplugin_file_is_compiled_against_and_resolves_when_the_plugin_runs()
	{
		using var scripts = new ScriptsFolder();
		string name = "Dep" + Guid.NewGuid().ToString("N");
		string dependency = scripts.CompileTo($@"Scripts\Src\lib\{name}.dll", name, "namespace Dep { public static class Info { public static string Text() { return \"third party\"; } } }");
		scripts.Write(@"Scripts\Src\Mine.csplugin", $"[Plugin]\nReferences = lib/{name}.dll\n");
		scripts.Write(@"Scripts\Src\Plugin.cs", Returning("WithReference", "Dep.Info.Text()"));
		var loaded = scripts.Load();
		bool loadedByTheScan = ScriptsFolder.IsExecutionLoaded(dependency);

		object text = Run(loaded, "WithReference");

		await Assert.That(loadedByTheScan).IsFalse();
		await Assert.That(text).IsEqualTo("third party");
		await Assert.That(ScriptsFolder.IsExecutionLoaded(dependency)).IsTrue();
	}

	[Test]
	public async Task Reference_that_the_package_installer_moved_out_of_its_subfolder_is_still_found()
	{
		using var scripts = new ScriptsFolder();
		string name = "Dep" + Guid.NewGuid().ToString("N");
		scripts.CompileTo($@"Scripts\Src\{name}.dll", name, "namespace Dep { public static class Info { public static string Text() { return \"flattened\"; } } }");
		scripts.Write(@"Scripts\Src\Mine.csplugin", $"[Plugin]\nReferences=lib/{name}.dll\n");
		scripts.Write(@"Scripts\Src\Plugin.cs", Returning("Flattened", "Dep.Info.Text()"));

		await Assert.That(Run(scripts.Load(), "Flattened")).IsEqualTo("flattened");
	}

	[Test]
	public async Task Missing_reference_is_reported_and_the_plugin_is_skipped()
	{
		using var scripts = new ScriptsFolder();
		string marker = scripts.Write(@"Scripts\Src\Mine.csplugin", "[Plugin]\nReferences=lib/NotThere.dll\n");
		scripts.Write(@"Scripts\Src\Plugin.cs", Returning("NoReference", "\"x\""));

		var loaded = scripts.Load();

		await Assert.That(loaded.Engine.GetAllCommands()).IsEmpty();
		await Assert.That(loaded.ConsoleLinesAboutThisFixture).Contains("Failed to load source plugin '" + marker + "': the reference 'lib/NotThere.dll' does not exist");
	}

	[Test]
	public async Task Framework_reference_of_the_csplugin_file_is_available_to_the_source()
	{
		using var scripts = new ScriptsFolder();
		scripts.Write(@"Scripts\Src\Mine.csplugin", "[Plugin]\nFrameworkReferences=System.Web\n");
		scripts.Write(@"Scripts\Src\Plugin.cs", Returning("Web", "System.Web.HttpUtility.UrlEncode(\"a b\")"));

		await Assert.That(Run(scripts.Load(), "Web")).IsEqualTo("a+b");
	}

	[Test]
	public async Task Source_wins_over_the_dlls_in_its_folder()
	{
		using var scripts = new ScriptsFolder();
		scripts.Write(@"Scripts\Src\Mine.csplugin", "");
		scripts.Write(@"Scripts\Src\Plugin.cs", Returning("Current", "\"from source\""));
		scripts.Write(@"Scripts\Src\obj\Generated.cs", "this file is build output and does not compile");
		string stale = scripts.Compile(@"Scripts\Src\bin\Debug", Returning("Stale", "\"from stale dll\""));
		string beside = scripts.Compile(@"Scripts\Src", Returning("Beside", "\"from dll beside the sources\""));

		var loaded = scripts.Load();

		await Assert.That(loaded.Engine.GetAllCommands().Select(c => c.Key)).IsEquivalentTo(new[] { "Current" });
		await Assert.That(Run(loaded, "Current")).IsEqualTo("from source");
		await Assert.That(ScriptsFolder.IsExecutionLoaded(stale)).IsFalse();
		await Assert.That(ScriptsFolder.IsExecutionLoaded(beside)).IsFalse();
	}

	[Test]
	public async Task Exception_in_a_source_plugin_has_the_source_file_and_line_in_its_stack_trace()
	{
		using var scripts = new ScriptsFolder();
		scripts.Write(@"Scripts\Src\Mine.csplugin", "");
		string source = scripts.Write(@"Scripts\Src\Plugin.cs", "using System;\nusing cYo.Projects.ComicRack.Plugins;\n[Plugin(\"Traced\")]\npublic class Plugin : IStartup\n{\n\tpublic void Execute()\n\t{\n\t\tthrow new InvalidOperationException(\"traced\");\n\t}\n}\n");
		Command command = scripts.Load().Command("Traced", PluginEngine.ScriptTypeStartup);

		var thrown = await Assert.That(() => command.Invoke(null)).ThrowsExactly<InvalidOperationException>();

		await Assert.That(thrown.StackTrace).Contains(source + ":line 8");
	}

	[Test]
	public async Task Roslyn_is_loaded_only_when_a_source_plugin_has_to_be_compiled()
	{
		using var compiledOnly = new ScriptsFolder();
		compiledOnly.Compile(@"Scripts\Mine", Returning("Compiled", "\"x\""));
		using var withSource = new ScriptsFolder();
		withSource.Write(@"Scripts\Src\Mine.csplugin", "");
		withSource.Write(@"Scripts\Src\Plugin.cs", Returning("FromSource", "\"x\""));

		IsolatedScan.Result noSourcePlugin = IsolatedScan.Run(compiledOnly);
		IsolatedScan.Result firstStart = IsolatedScan.Run(withSource);
		IsolatedScan.Result secondStart = IsolatedScan.Run(withSource);

		await Assert.That(noSourcePlugin.Keys).IsEquivalentTo(new[] { "Compiled" });
		await Assert.That(noSourcePlugin.LoadedRoslyn).IsFalse();
		await Assert.That(firstStart.Keys).IsEquivalentTo(new[] { "FromSource" });
		await Assert.That(firstStart.LoadedRoslyn).IsTrue();
		await Assert.That(secondStart.Keys).IsEquivalentTo(new[] { "FromSource" });
		await Assert.That(secondStart.LoadedRoslyn).IsFalse();
	}
}
