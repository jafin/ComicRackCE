using System.IO;
using System.Linq;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Plugins;

namespace ComicRack.Plugins.Tests;

public class EngineTests
{
	private const string RecordCreation = "public Plugin(IPluginContext context) { System.IO.File.WriteAllText(System.IO.Path.Combine(context.ScriptPath, \"created.txt\"), \"x\"); }";

	private static string PythonScript(string key, string hook, string function)
	{
		return $"#@Name {function}\n#@Key {key}\n#@Hook {hook}\ndef {function}(books):\n\tpass\n";
	}

	[Test]
	public async Task Python_scripts_with_the_same_key_still_register_only_the_first_one()
	{
		using var scripts = new ScriptsFolder();
		scripts.Write(@"Scripts\Mine\a.py", PythonScript("Dup", "Books", "first"));
		scripts.Write(@"Scripts\Mine\b.py", PythonScript("Dup", "Library", "second"));

		var loaded = scripts.Load();

		await Assert.That(loaded.Hooks("Dup")).IsEquivalentTo(new[] { PluginEngine.ScriptTypeBooks });
	}

	[Test]
	public async Task Second_dotnet_plugin_with_a_key_that_is_taken_is_skipped_and_never_created()
	{
		using var scripts = new ScriptsFolder();
		string first = scripts.Compile(@"Scripts\A", Source.Plugin("Same", "IStartup, IShutdown", RecordCreation + " public void Execute() { } public bool Execute(bool userIsClosing) { return true; }"));
		string second = scripts.Compile(@"Scripts\B", Source.Plugin("Same", "IBookOpened, IConfig", RecordCreation + " public void Execute(ComicBook book) { } public void Execute() { }"));

		var loaded = scripts.Load();
		loaded.Engine.Invoke(PluginEngine.ScriptTypeStartup, new object[0]);
		loaded.Engine.Invoke(PluginEngine.ScriptTypeBookOpened, new object[] { new cYo.Projects.ComicRack.Engine.ComicBook() });

		await Assert.That(loaded.Hooks("Same")).IsEquivalentTo(new[] { PluginEngine.ScriptTypeStartup, PluginEngine.ScriptTypeShutdown });
		await Assert.That(loaded.Command("Same", PluginEngine.ScriptTypeStartup).Configure).IsNull();
		await Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(first), "created.txt"))).IsTrue();
		await Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(second), "created.txt"))).IsFalse();
		await Assert.That(PluginConsole.LinesMentioning("'Same.BookOpened'")).Contains("already used by another plugin");
	}

	[Test]
	public async Task Dotnet_plugin_with_the_key_of_a_python_script_is_skipped()
	{
		using var scripts = new ScriptsFolder();
		scripts.Write(@"Scripts\A\script.py", PythonScript("Taken", "Books", "run"));
		scripts.Compile(@"Scripts\B", Source.Plugin("Taken", "IStartup, IShutdown", "public void Execute() { } public bool Execute(bool userIsClosing) { return true; }"));

		var loaded = scripts.Load();

		await Assert.That(loaded.Hooks("Taken")).IsEquivalentTo(new[] { PluginEngine.ScriptTypeBooks });
	}

	[Test]
	public async Task Python_script_with_the_key_of_a_dotnet_plugin_is_skipped()
	{
		using var scripts = new ScriptsFolder();
		scripts.Compile(@"Scripts\A", Source.Plugin("Taken", "IStartup, IShutdown", "public void Execute() { } public bool Execute(bool userIsClosing) { return true; }"));
		scripts.Write(@"Scripts\B\script.py", PythonScript("Taken", "Books", "run"));

		var loaded = scripts.Load();

		await Assert.That(loaded.Hooks("Taken")).IsEquivalentTo(new[] { PluginEngine.ScriptTypeStartup, PluginEngine.ScriptTypeShutdown });
	}

	[Test]
	public async Task Config_hook_becomes_the_configure_action_of_every_command_of_its_own_plugin_only()
	{
		using var scripts = new ScriptsFolder();
		scripts.Compile(@"Scripts\Mine", Source.Types("""
			[Plugin("WithConfig")]
			public class WithConfig : IStartup, IShutdown, IConfig
			{
				void IStartup.Execute() { }
				bool IShutdown.Execute(bool userIsClosing) { return true; }
				void IConfig.Execute() { }
			}
			[Plugin("Plain")]
			public class Plain : IStartup { public void Execute() { } }
			"""));
		scripts.Write(@"Scripts\Mine\z.py", PythonScript("Plain", PluginEngine.ScriptTypeConfig, "configure"));

		var loaded = scripts.Load();

		Command configure = loaded.Command("WithConfig", PluginEngine.ScriptTypeStartup).Configure;
		await Assert.That(configure?.Hook).IsEqualTo(PluginEngine.ScriptTypeConfig);
		await Assert.That(loaded.Command("WithConfig", PluginEngine.ScriptTypeShutdown).Configure).IsSameReferenceAs(configure);
		await Assert.That(loaded.Command("Plain", PluginEngine.ScriptTypeStartup).Configure).IsNull();
	}

	[Test]
	public async Task Python_config_script_still_attaches_to_the_python_command_with_its_key()
	{
		using var scripts = new ScriptsFolder();
		scripts.Write(@"Scripts\Mine\a.py", PythonScript("Py", "Books", "run") + PythonScript("Py", PluginEngine.ScriptTypeConfig, "configure"));

		var loaded = scripts.Load();

		await Assert.That(loaded.Command("Py", PluginEngine.ScriptTypeBooks).Configure?.Hook).IsEqualTo(PluginEngine.ScriptTypeConfig);
	}

	[Test]
	public async Task Command_states_hold_one_entry_per_key_and_apply_to_every_hook_of_the_plugin()
	{
		using var scripts = new ScriptsFolder();
		scripts.Compile(@"Scripts\Mine", Source.Types("""
			[Plugin("Multi")]
			public class Multi : IStartup, IShutdown
			{
				public void Execute() { }
				public bool Execute(bool userIsClosing) { return true; }
			}
			[Plugin("Solo")]
			public class Solo : IStartup { public void Execute() { } }
			"""));
		var loaded = scripts.Load();

		await Assert.That(loaded.Engine.CommandStates.Split(',')).IsEquivalentTo(new[] { "+Multi", "+Solo" });

		loaded.Engine.CommandStates = "-Multi,+Solo,-NotInstalled";

		await Assert.That(loaded.Engine.GetAllCommands().Where(c => c.Key == "Multi").Select(c => c.Enabled)).IsEquivalentTo(new[] { false, false });
		await Assert.That(loaded.Engine.GetCommands(PluginEngine.ScriptTypeStartup).Select(c => c.Key)).IsEquivalentTo(new[] { "Solo" });
		await Assert.That(loaded.Engine.GetCommands(PluginEngine.ScriptTypeShutdown)).IsEmpty();
		await Assert.That(loaded.Engine.CommandStates.Split(',')).IsEquivalentTo(new[] { "-Multi", "+Solo" });
	}

	[Test]
	public async Task SetEnabled_switches_every_hook_of_the_key()
	{
		using var scripts = new ScriptsFolder();
		scripts.Compile(@"Scripts\Mine", Source.Plugin("Multi", "IStartup, IShutdown", "public void Execute() { } public bool Execute(bool userIsClosing) { return true; }"));
		var loaded = scripts.Load();

		loaded.Engine.SetEnabled("Multi", false);
		bool[] off = loaded.Engine.GetAllCommands().Select(c => c.Enabled).ToArray();
		loaded.Engine.SetEnabled("Multi", true);
		bool[] on = loaded.Engine.GetAllCommands().Select(c => c.Enabled).ToArray();

		await Assert.That(off).IsEquivalentTo(new[] { false, false });
		await Assert.That(on).IsEquivalentTo(new[] { true, true });
	}

	[Test]
	public async Task Dispose_of_the_engine_disposes_each_created_disposable_plugin_once_even_when_another_one_throws()
	{
		using var scripts = new ScriptsFolder();
		scripts.Compile(@"Scripts\A", Source.Plugin("Thrower", "IStartup, IDisposable", "public void Execute() { } public void Dispose() { throw new InvalidOperationException(\"cannot close zq-dispose\"); }"));
		string good = scripts.Compile(@"Scripts\B", Source.Plugin("Good", "IStartup, IShutdown, IConfig, IDisposable", Source.Context + """
			void IStartup.Execute() { }
			bool IShutdown.Execute(bool userIsClosing) { return true; }
			void IConfig.Execute() { }
			public void Dispose() { System.IO.File.AppendAllText(System.IO.Path.Combine(context.ScriptPath, "disposed.txt"), "x"); }
			"""));
		scripts.Compile(@"Scripts\C", Source.Plugin("NotDisposable", "IStartup", "public void Execute() { }"));
		var loaded = scripts.Load();
		loaded.Engine.Invoke(PluginEngine.ScriptTypeStartup, new object[0]);

		loaded.Engine.Dispose();
		loaded.Engine.Dispose();

		await Assert.That(File.ReadAllText(Path.Combine(Path.GetDirectoryName(good), "disposed.txt"))).IsEqualTo("x");
		await Assert.That(PluginConsole.LinesMentioning("zq-dispose")).IsEqualTo("Failed to dispose .NET plugin 'Thrower': cannot close zq-dispose");
	}

	[Test]
	public async Task Dispose_of_the_engine_neither_creates_nor_disposes_a_plugin_that_no_hook_has_used()
	{
		using var scripts = new ScriptsFolder();
		string unused = scripts.Compile(@"Scripts\Mine", Source.Plugin("Unused", "IStartup, IDisposable", Source.Context + """
			public void Execute() { }
			public void Dispose() { System.IO.File.WriteAllText(System.IO.Path.Combine(context.ScriptPath, "disposed.txt"), "x"); }
			"""));
		var loaded = scripts.Load();

		loaded.Engine.Dispose();

		await Assert.That(Directory.GetFiles(Path.GetDirectoryName(unused)).Select(Path.GetFileName)).IsEquivalentTo(new[] { Path.GetFileName(unused) });
	}
}
