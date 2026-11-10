using System.IO;
using System.Linq;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Plugins;

namespace ComicRack.Plugins.Tests;

public class LoadFailureTests
{
	private static string Survivor(string key) => Source.Plugin(key, "IStartup", "public void Execute() { }");

	// The runtime loads one assembly per identity, which is the plugin name and a hash of its source, so two tests must not compile the same source under the same name.
	private static string UniqueName() => "Src" + System.Guid.NewGuid().ToString("N");

	[Test]
	public async Task Source_plugin_that_does_not_compile_is_recorded_with_its_first_error()
	{
		using var scripts = new ScriptsFolder();
		string marker = scripts.Write(@"Scripts\Broken\Broken.csplugin", "");
		string source = scripts.Write(@"Scripts\Broken\Plugin.cs", "using cYo.Projects.ComicRack.Plugins;\n[Plugin(\"Broken\")]\npublic class Plugin : IStartup\n{\n\tpublic void Execute() { int x = \"text\"; int y = \"more\"; }\n}\n");
		scripts.Write(@"Scripts\Good\Good.csplugin", "");
		scripts.Write(@"Scripts\Good\Plugin.cs", Survivor("Good"));

		PluginLoadFailure failure = scripts.Load().Engine.LoadFailures.Single();

		await Assert.That(failure.Kind).IsEqualTo(PluginKind.CSharpSource);
		await Assert.That(failure.File).IsEqualTo(marker);
		await Assert.That(failure.Name).IsEqualTo("Broken");
		await Assert.That(failure.Reason).StartsWith(source + "(5,").And.Contains("error CS0029").And.EndsWith("(and 1 more)");
	}

	[Test]
	public async Task Source_plugin_with_a_missing_reference_is_recorded()
	{
		using var scripts = new ScriptsFolder();
		scripts.Write(@"Scripts\Src\Mine.csplugin", "[Plugin]\nReferences=lib/NotThere.dll\n");
		scripts.Write(@"Scripts\Src\Plugin.cs", Survivor("NoReference"));

		PluginLoadFailure failure = scripts.Load().Engine.LoadFailures.Single();

		await Assert.That(failure.Kind).IsEqualTo(PluginKind.CSharpSource);
		await Assert.That(failure.Name).IsEqualTo("Mine");
		await Assert.That(failure.Reason).IsEqualTo("the reference 'lib/NotThere.dll' does not exist");
	}

	[Test]
	public async Task Class_of_a_compiled_plugin_that_cannot_load_is_recorded_and_the_good_class_is_not()
	{
		using var scripts = new ScriptsFolder();
		string dll = scripts.Compile(@"Scripts\Mine", Source.Types("""
			[Plugin("NoHook")] public class NoHook { }
			[Plugin("Good")] public class Good : IStartup { public void Execute() { } }
			"""));

		PluginLoadFailure failure = scripts.Load().Engine.LoadFailures.Single();

		await Assert.That(failure.Kind).IsEqualTo(PluginKind.CompiledDotNet);
		await Assert.That(failure.File).IsEqualTo(dll);
		await Assert.That(failure.Name).IsEqualTo("NoHook");
		await Assert.That(failure.Reason).IsEqualTo("it implements no hook interface");
	}

	[Test]
	public async Task Plugin_blocked_by_windows_is_recorded_without_a_name()
	{
		using var scripts = new ScriptsFolder();
		string plugin = scripts.Compile(@"Scripts\Mine", Survivor("Blocked"));
		LoadingSafetyTests.MarkAsDownloaded(plugin);

		PluginLoadFailure failure = scripts.Load().Engine.LoadFailures.Single();

		await Assert.That(failure.File).IsEqualTo(plugin);
		await Assert.That(failure.Name).IsNull();
		await Assert.That(failure.Reason).Contains("Unblock");
	}

	[Test]
	public async Task Plugin_for_another_api_version_is_recorded()
	{
		using var scripts = new ScriptsFolder();
		scripts.Compile(@"Scripts\Mine", Source.Plugin("Newer", "IStartup", "public void Execute() { }", assemblyAttributes: $"[assembly: PluginApiVersion({PluginApi.Version + 1})]"));

		PluginLoadFailure failure = scripts.Load().Engine.LoadFailures.Single();

		await Assert.That(failure.Reason).Contains($"plugin API version {PluginApi.Version + 1}");
	}

	[Test]
	public async Task Plugin_that_uses_a_taken_key_is_recorded_once()
	{
		using var scripts = new ScriptsFolder();
		scripts.Compile(@"Scripts\A", Source.Plugin("Taken", "IStartup, IShutdown", "public void Execute() { } public bool Execute(bool userIsClosing) { return true; }"));
		scripts.Compile(@"Scripts\B", Source.Plugin("Taken", "IStartup, IShutdown", "public void Execute() { } public bool Execute(bool userIsClosing) { return true; }"));

		var loaded = scripts.Load();

		PluginLoadFailure failure = loaded.Engine.LoadFailures.Single();
		await Assert.That(failure.Name).IsEqualTo("Taken");
		await Assert.That(failure.Reason).IsEqualTo("the Key is already used by another plugin");
		await Assert.That(Path.GetDirectoryName(failure.File)).IsNotEqualTo(loaded.Engine.GetAllCommands().First().Environment.CommandPath);
	}

	[Test]
	public async Task Plugins_that_load_are_not_recorded()
	{
		using var scripts = new ScriptsFolder();
		scripts.Compile(@"Scripts\Mine", Survivor("Compiled"));
		scripts.Write(@"Scripts\Src\" + UniqueName() + ".csplugin", "");
		scripts.Write(@"Scripts\Src\Plugin.cs", Survivor("FromSource"));

		await Assert.That(scripts.Load().Engine.LoadFailures).IsEmpty();
	}

	[Test]
	public async Task Kind_tells_python_source_and_compiled_plugins_apart()
	{
		using var scripts = new ScriptsFolder();
		scripts.Compile(@"Scripts\Compiled", Survivor("Compiled"));
		scripts.Write(@"Scripts\Src\" + UniqueName() + ".csplugin", "");
		scripts.Write(@"Scripts\Src\Plugin.cs", Survivor("FromSource"));
		scripts.Write(@"Scripts\Py\Script.py", "#@Key FromPython\n#@Name Python\n#@Hook Books\ndef Run(books):\n    pass\n");
		scripts.Write(@"Scripts\Xml\Defined.xml", "<ArrayOfCommand xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><Command xsi:type=\"PythonCommand\" Hook=\"Books\" Key=\"FromXml\"><Name>Xml</Name><Method>Run</Method><ScriptFile>Script.py</ScriptFile></Command></ArrayOfCommand>");
		scripts.Write(@"Scripts\Xml\Script.py", "def Run(books):\n    pass\n");

		var loaded = scripts.Load();

		await Assert.That(loaded.Command("Compiled", PluginEngine.ScriptTypeStartup).Kind).IsEqualTo(PluginKind.CompiledDotNet);
		await Assert.That(loaded.Command("FromSource", PluginEngine.ScriptTypeStartup).Kind).IsEqualTo(PluginKind.CSharpSource);
		await Assert.That(loaded.Command("FromPython", PluginEngine.ScriptTypeBooks).Kind).IsEqualTo(PluginKind.Python);
		await Assert.That(loaded.Command("FromXml", PluginEngine.ScriptTypeBooks).Kind).IsEqualTo(PluginKind.Python);
	}
}
