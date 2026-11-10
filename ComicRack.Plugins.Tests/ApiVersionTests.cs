using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Plugins;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ComicRack.Plugins.Tests;

public class ApiVersionTests
{
	private static string Versioned(string key, int? version)
	{
		return Source.Plugin(key, "IStartup", "public void Execute() { }", assemblyAttributes: version == null ? "" : $"[assembly: PluginApiVersion({version})]");
	}

	[Test]
	[Arguments(PluginApi.Version)]
	[Arguments(PluginApi.MinimumVersion)]
	[Arguments(null)]
	public async Task Compiled_plugin_that_fits_or_declares_nothing_is_loaded(int? version)
	{
		using var scripts = new ScriptsFolder();
		string dll = scripts.Compile(@"Scripts\Mine", Versioned("Fits", version));

		var loaded = scripts.Load();

		await Assert.That(loaded.Engine.GetAllCommands().Select(c => c.Key)).IsEquivalentTo(new[] { "Fits" });
		await Assert.That(ScriptsFolder.IsExecutionLoaded(dll)).IsTrue();
	}

	[Test]
	public async Task Compiled_plugin_for_a_newer_api_is_skipped_and_never_loaded()
	{
		using var scripts = new ScriptsFolder();
		string dll = scripts.Compile(@"Scripts\Mine", Versioned("Newer", PluginApi.Version + 1));
		scripts.Compile(@"Scripts\Mine", Versioned("Fits", PluginApi.Version));

		var loaded = scripts.Load();

		await Assert.That(loaded.Engine.GetAllCommands().Select(c => c.Key)).IsEquivalentTo(new[] { "Fits" });
		await Assert.That(ScriptsFolder.IsExecutionLoaded(dll)).IsFalse();
		await Assert.That(loaded.ConsoleLinesAboutThisFixture).Contains($"Skipped .NET plugin '{dll}': it was written for plugin API version {PluginApi.Version + 1}, but this ComicRack supports versions {PluginApi.MinimumVersion} to {PluginApi.Version}");
	}

	[Test]
	public async Task Compiled_plugin_for_an_api_below_the_minimum_is_skipped_and_never_loaded()
	{
		using var scripts = new ScriptsFolder();
		string dll = scripts.Compile(@"Scripts\Mine", Versioned("Older", PluginApi.MinimumVersion - 1));

		var loaded = scripts.Load();

		await Assert.That(loaded.Engine.GetAllCommands()).IsEmpty();
		await Assert.That(ScriptsFolder.IsExecutionLoaded(dll)).IsFalse();
		await Assert.That(loaded.ConsoleLinesAboutThisFixture).Contains($"it was written for plugin API version {PluginApi.MinimumVersion - 1}, but");
	}

	[Test]
	public async Task Source_plugin_for_a_newer_api_is_compiled_but_skipped_and_never_loaded()
	{
		using var scripts = new ScriptsFolder();
		scripts.Write(@"Scripts\Src\Mine.csplugin", "");
		scripts.Write(@"Scripts\Src\Plugin.cs", Versioned("NewerSource", PluginApi.Version + 1));

		var loaded = scripts.Load();

		await Assert.That(loaded.Engine.GetAllCommands()).IsEmpty();
		await Assert.That(loaded.ConsoleLinesAboutThisFixture).Contains("Skipped source plugin").And.Contains($"plugin API version {PluginApi.Version + 1}");
		await Assert.That(System.IO.Directory.GetFiles(scripts.Cache, "*.dll").Any(ScriptsFolder.IsExecutionLoaded)).IsFalse();
	}

	// Plugin authors build against the ComicRack.Plugins.dll of the ComicRack they have, which is another build than the one that loads the plugin.
	[Test]
	public async Task Plugin_built_against_another_build_of_the_api_assembly_is_still_checked()
	{
		using var scripts = new ScriptsFolder();
		string oldApi = Path.Combine(scripts.Root, "OldApi", "ComicRack.Plugins.dll");
		Directory.CreateDirectory(Path.GetDirectoryName(oldApi));
		Emit(oldApi, "ComicRack.Plugins", "[assembly: System.Reflection.AssemblyVersion(\"0.5.0.0\")] namespace cYo.Projects.ComicRack.Plugins { public sealed class PluginApiVersionAttribute : System.Attribute { public PluginApiVersionAttribute(int version) { } } }");
		string plugin = Path.Combine(scripts.Scripts, "Mine", "OldBuild" + Guid.NewGuid().ToString("N") + ".dll");
		Directory.CreateDirectory(Path.GetDirectoryName(plugin));
		Emit(plugin, Path.GetFileNameWithoutExtension(plugin), $"[assembly: cYo.Projects.ComicRack.Plugins.PluginApiVersion({PluginApi.Version + 1})]", oldApi);

		var loaded = scripts.Load();

		await Assert.That(ScriptsFolder.IsExecutionLoaded(plugin)).IsFalse();
		await Assert.That(loaded.ConsoleLinesAboutThisFixture).Contains($"plugin API version {PluginApi.Version + 1}");
	}

	private static void Emit(string file, string name, string source, params string[] references)
	{
		var compilation = CSharpCompilation.Create(
			name,
			new[] { CSharpSyntaxTree.ParseText(source) },
			references.Concat(new[] { typeof(object).Assembly.Location }).Select(r => MetadataReference.CreateFromFile(r)),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		using (FileStream stream = File.Create(file))
		{
			var result = compilation.Emit(stream);
			if (!result.Success)
			{
				throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
			}
		}
	}

	[Test]
	public async Task Source_plugin_that_fits_is_loaded()
	{
		using var scripts = new ScriptsFolder();
		scripts.Write(@"Scripts\Src\Mine.csplugin", "");
		scripts.Write(@"Scripts\Src\Plugin.cs", Versioned("FitsSource", PluginApi.Version));

		await Assert.That(scripts.Load().Engine.GetAllCommands().Select(c => c.Key)).IsEquivalentTo(new[] { "FitsSource" });
	}
}
