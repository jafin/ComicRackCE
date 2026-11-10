using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace cYo.Projects.ComicRack.Plugins
{
	// One plugin class of one scan. It is the identity of the plugin for the engine, and it owns the one instance that all hook commands of the class share.
	internal sealed class DotNetPluginClass
	{
		private sealed class Context : IPluginContext
		{
			public IPluginEnvironment ComicRack { get; set; }

			public string ScriptPath { get; set; }

			public string Key { get; set; }

			public string Localize(string elementKey, string text) => ComicRack.Localize(Key, elementKey, text);
		}

		private readonly ConstructorInfo constructor;

		private readonly Lazy<object> instance;

		private IPluginContext context;

		private DotNetPluginClass(Type type, PluginAttribute attribute, ConstructorInfo constructor, string file)
		{
			Type = type;
			Attribute = attribute;
			File = file;
			this.constructor = constructor;
			// The Lazy keeps a constructor exception and throws it again, so a failed constructor never runs twice.
			instance = new Lazy<object>(Construct, LazyThreadSafetyMode.ExecutionAndPublication);
		}

		public Type Type { get; }

		public PluginAttribute Attribute { get; }

		// The file that stands for the plugin on disk: the DLL of a compiled plugin, the .csplugin file of a source plugin.
		public string File { get; }

		// Returns null when the type has no constructor that ComicRack can call.
		public static DotNetPluginClass Create(Type type, PluginAttribute attribute, string file)
		{
			ConstructorInfo constructor = type.GetConstructor(new[] { typeof(IPluginContext) }) ?? type.GetConstructor(Type.EmptyTypes);
			return constructor == null ? null : new DotNetPluginClass(type, attribute, constructor, file);
		}

		// Every hook command of the class calls this when it is initialized; they all have the same folder.
		public void SetContext(IPluginEnvironment env, string folder)
		{
			Interlocked.CompareExchange(ref context, new Context { ComicRack = env, ScriptPath = folder, Key = Attribute.Key }, null);
		}

		public object GetInstance()
		{
			if (context == null)
			{
				throw new InvalidOperationException($"The .NET plugin '{Attribute.Key}' is not initialized");
			}
			try
			{
				return instance.Value;
			}
			catch (Exception e)
			{
				Command.LogDefault($"{Attribute.Key}: constructor failed: {e.Message}");
				throw;
			}
		}

		public bool TryGetCreatedInstance(out object created)
		{
			created = instance.IsValueCreated ? instance.Value : null;
			return created != null;
		}

		private object Construct()
		{
			try
			{
				return constructor.Invoke(constructor.GetParameters().Length == 0 ? null : new object[] { context });
			}
			catch (TargetInvocationException e) when (e.InnerException != null)
			{
				ExceptionDispatchInfo.Capture(e.InnerException).Throw();
				throw;
			}
		}
	}
}
