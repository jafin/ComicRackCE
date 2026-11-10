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

		private readonly PluginLoadFailures failures;

		public DotNetPluginInitializer(PluginLoadFailures failures)
		{
			this.failures = failures;
		}

		public override IEnumerable<Command> GetCommands(string file)
		{
			if (!".dll".Equals(Path.GetExtension(file), StringComparison.OrdinalIgnoreCase) || !ReferencesPluginApi(file, out string apiProblem))
			{
				return Enumerable.Empty<Command>();
			}
			if (apiProblem != null)
			{
				return Skip(file, apiProblem);
			}
			try
			{
				PluginAssemblyResolver.Register(Path.GetDirectoryName(file));
				Assembly assembly = Assembly.LoadFrom(file);
				if (!string.Equals(Path.GetFullPath(assembly.Location), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase))
				{
					return Skip(file, $"an assembly with the same identity is already loaded from '{assembly.Location}'");
				}
				return DotNetPluginLoader.CreateCommands(assembly, file, failures);
			}
			catch (FileLoadException e) when (e.HResult == BlockedByZoneHResult)
			{
				return Skip(file, "Windows blocked it because it came from the internet. Open the Properties of the file and select Unblock, then restart ComicRack.");
			}
			catch (Exception e)
			{
				return Skip(file, e.Message);
			}
		}

		private IEnumerable<Command> Skip(string file, string reason)
		{
			Command.LogDefault("Skipped .NET plugin '{0}': {1}", file, reason);
			failures.Add(PluginKind.CompiledDotNet, file, null, reason);
			return Enumerable.Empty<Command>();
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
