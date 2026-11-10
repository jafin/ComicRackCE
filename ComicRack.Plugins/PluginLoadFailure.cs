using System.Collections.Generic;
using System.Linq;

namespace cYo.Projects.ComicRack.Plugins
{
	/// <summary>How a plugin is written and shipped.</summary>
	public enum PluginKind
	{
		/// <summary>A Python script, whether a .py file or a command defined in an XML file.</summary>
		Python,

		/// <summary>A folder with a .csplugin file that ComicRack compiles.</summary>
		CSharpSource,

		/// <summary>A .NET DLL that the author built.</summary>
		CompiledDotNet
	}

	/// <summary>A plugin that ComicRack found but could not load.</summary>
	public sealed class PluginLoadFailure
	{
		public PluginLoadFailure(PluginKind kind, string file, string name, string reason)
		{
			Kind = kind;
			File = file;
			Name = name;
			Reason = reason;
		}

		public PluginKind Kind { get; }

		/// <summary>The DLL of a compiled plugin, the .csplugin file of a source plugin.</summary>
		public string File { get; }

		/// <summary>The key or the class name of the plugin, or null when ComicRack could not get far enough to know it.</summary>
		public string Name { get; }

		/// <summary>A short explanation, such as the first compile error.</summary>
		public string Reason { get; }
	}

	// Collects the failures of one engine. The initializers add to it from wherever they log a failure.
	internal sealed class PluginLoadFailures
	{
		private readonly List<PluginLoadFailure> items = new List<PluginLoadFailure>();

		public IReadOnlyList<PluginLoadFailure> Snapshot()
		{
			lock (items)
			{
				return items.ToList();
			}
		}

		// One plugin class can fail once for each of its hooks, and the user needs to see it once.
		public void Add(PluginKind kind, string file, string name, string reason)
		{
			lock (items)
			{
				if (!items.Any(item => item.Kind == kind && item.File == file && item.Name == name && item.Reason == reason))
				{
					items.Add(new PluginLoadFailure(kind, file, name, reason));
				}
			}
		}
	}
}
