namespace cYo.Projects.ComicRack.Plugins
{
	/// <summary>
	/// What ComicRack gives to the constructor of a plugin class (<see cref="PluginAttribute"/>).
	/// </summary>
	public interface IPluginContext
	{
		/// <summary>The host.</summary>
		IPluginEnvironment ComicRack { get; } // Uses the same name than Python to keep the usage similar

		/// <summary>The folder of the plugin: the folder of the DLL, or of the .csplugin file for a source plugin.</summary>
		string ScriptPath { get; } // Uses the same name than Python to keep the usage similar

		/// <summary>
		/// Returns the translation of <paramref name="text"/> for the language of ComicRack, or <paramref name="text"/> when there is none.
		/// The translations are in the language files of the plugin, see the Samples README. <paramref name="elementKey"/> names the entry in the file.
		/// </summary>
		string Localize(string elementKey, string text);
	}
}
