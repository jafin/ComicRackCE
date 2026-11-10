using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace cYo.Projects.ComicRack.Plugins
{
	internal sealed class DotNetPluginInitializer : PluginInitializer
	{
		// COR_E_NOTSUPPORTED: .NET Framework refuses LoadFrom of a file that carries the internet zone mark.
		private const int BlockedByZoneHResult = unchecked((int)0x80131515);

		private static readonly string pluginApiName = typeof(PluginAttribute).Assembly.GetName().Name;

		public override IEnumerable<Command> GetCommands(string file)
		{
			if (!".dll".Equals(Path.GetExtension(file), StringComparison.OrdinalIgnoreCase) || !ReferencesPluginApi(file, out string apiProblem))
			{
				return Enumerable.Empty<Command>();
			}
			if (apiProblem != null)
			{
				Command.LogDefault("Skipped .NET plugin '{0}': {1}", file, apiProblem);
				return Enumerable.Empty<Command>();
			}
			try
			{
				PluginAssemblyResolver.Register(Path.GetDirectoryName(file));
				Assembly assembly = Assembly.LoadFrom(file);
				if (!string.Equals(Path.GetFullPath(assembly.Location), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase))
				{
					Command.LogDefault("Skipped .NET plugin '{0}': an assembly with the same identity is already loaded from '{1}'", file, assembly.Location);
					return Enumerable.Empty<Command>();
				}
				return DotNetPluginLoader.CreateCommands(assembly, file);
			}
			catch (FileLoadException e) when (e.HResult == BlockedByZoneHResult)
			{
				Command.LogDefault("Windows blocked the .NET plugin '{0}' because it came from the internet. Open the Properties of the file and select Unblock, then restart ComicRack.", file);
				return Enumerable.Empty<Command>();
			}
			catch (Exception e)
			{
				Command.LogDefault("Failed to load .NET plugin '{0}': {1}", file, e.Message);
				return Enumerable.Empty<Command>();
			}
		}

		// A script folder also holds the libraries of plugins. Only a DLL that references the plugin API can contain a plugin, and only such a DLL gets loaded for execution.
		private static bool ReferencesPluginApi(string file, out string apiProblem)
		{
			apiProblem = null;
			try
			{
				AssemblyName name = AssemblyName.GetAssemblyName(file);
				Assembly assembly = PluginApiCheck.LoadReflectionOnly(file);
				if (assembly.GetReferencedAssemblies().Any(a => a.Name == pluginApiName))
				{
					apiProblem = PluginApiCheck.FindProblem(assembly);
					return true;
				}
				PluginAssemblyResolver.NoteLibrary(file, name);
				return false;
			}
			catch (Exception)
			{
				return false;
			}
		}
	}
}
