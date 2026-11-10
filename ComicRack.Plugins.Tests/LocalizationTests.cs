using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;
using cYo.Projects.ComicRack.Plugins.Theme;

namespace ComicRack.Plugins.Tests;

public class LocalizationTests
{
	private const string Members = "private readonly IPluginContext context; public Plugin(IPluginContext context) { this.context = context; }\n"
		+ "public string Execute(ComicBook[] books) { return context.Localize(\"Greeting\", \"Hello\") + \"|\" + context.Localize(\"Untranslated\", \"Same\"); }";

	// The host reads the language folder named like the top level of the UI culture, which is empty for the invariant culture.
	private static string LanguageFile(ScriptsFolder scripts, string pluginFolder, string key, string entries)
	{
		string culture = CultureInfo.CurrentUICulture.Name.Split('-')[0];
		string xml = $"<?xml version=\"1.0\"?>\n<TR Name=\"{key}\" CultureName=\"{culture}\">\n<Texts>\n{entries}\n</Texts>\n</TR>";
		return scripts.Write(Path.Combine("Scripts", pluginFolder, culture, key + ".xml"), xml);
	}

	// A real PluginEnvironment, because the stub returns every text unchanged.
	private static LoadedPlugins LoadWithHost(ScriptsFolder scripts)
	{
		PluginConsole.Install();
		var engine = new PluginEngine(scripts.Cache);
		engine.Initialize(new PluginEnvironment(null, null, null, null, null, null, ThemePlugin.Default), scripts.Scripts);
		return new LoadedPlugins(engine, scripts.Root);
	}

	private const string Entries = "<Text Key=\"Name\" Text=\"Gezaehlt\" Comment=\"Counted\" />\n<Text Key=\"Description\" Text=\"Beschreibung\" Comment=\"Description\" />\n<Text Key=\"Greeting\" Text=\"Hallo\" Comment=\"Hello\" />";

	[Test]
	public async Task Compiled_plugin_is_translated_from_the_language_files_in_its_folder()
	{
		using var scripts = new ScriptsFolder();
		scripts.Compile(@"Scripts\Mine", Source.Plugin("LocalizedCompiled", "IComicInfoHtml", Members, attribute: "Name = \"Counted\", Description = \"Description\""));
		LanguageFile(scripts, "Mine", "LocalizedCompiled", Entries);

		Command command = LoadWithHost(scripts).Command("LocalizedCompiled", PluginEngine.ScriptTypeComicInfoHtml);

		await Assert.That(command.GetLocalizedName()).IsEqualTo("Gezaehlt");
		await Assert.That(command.GetLocalizedDescription()).IsEqualTo("Beschreibung");
		await Assert.That(command.Invoke(new object[] { new ComicBook[0] })).IsEqualTo("Hallo|Same");
	}

	[Test]
	public async Task Source_plugin_is_translated_from_the_language_files_in_its_folder()
	{
		using var scripts = new ScriptsFolder();
		scripts.Write(@"Scripts\Src\Mine.csplugin", "");
		scripts.Write(@"Scripts\Src\Plugin.cs", Source.Plugin("LocalizedSource", "IComicInfoHtml", Members, attribute: "Name = \"Counted\""));
		LanguageFile(scripts, "Src", "LocalizedSource", Entries);

		Command command = LoadWithHost(scripts).Command("LocalizedSource", PluginEngine.ScriptTypeComicInfoHtml);

		await Assert.That(command.GetLocalizedName()).IsEqualTo("Gezaehlt");
		await Assert.That(command.Invoke(new object[] { new ComicBook[0] })).IsEqualTo("Hallo|Same");
	}

	[Test]
	public async Task Plugin_without_language_files_shows_the_texts_of_the_code()
	{
		using var scripts = new ScriptsFolder();
		scripts.Compile(@"Scripts\Mine", Source.Plugin("NotLocalized", "IComicInfoHtml", Members, attribute: "Name = \"Counted\""));

		Command command = LoadWithHost(scripts).Command("NotLocalized", PluginEngine.ScriptTypeComicInfoHtml);

		await Assert.That(command.GetLocalizedName()).IsEqualTo("Counted");
		await Assert.That(command.Invoke(new object[] { new ComicBook[0] })).IsEqualTo("Hello|Same");
	}
}
