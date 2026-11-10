using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using cYo.Common.IO;
using cYo.Common.Windows.Forms.Theme;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ComicRack.Plugins.Tests;

internal sealed class ScriptsFolder : IDisposable
{
	private static readonly MetadataReference[] defaultReferences = new[]
	{
		typeof(object).Assembly,
		typeof(Uri).Assembly,
		typeof(Enumerable).Assembly,
		typeof(System.Drawing.Image).Assembly,
		typeof(System.Windows.Forms.Control).Assembly,
		typeof(FileUtility).Assembly,
		typeof(Themes).Assembly,
		typeof(ComicBook).Assembly,
		typeof(Command).Assembly
	}.Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location)).ToArray();

	public ScriptsFolder()
	{
		Root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ComicRack.Plugins.Tests", Guid.NewGuid().ToString("N"))).FullName;
		Scripts = Directory.CreateDirectory(Path.Combine(Root, "Scripts")).FullName;
		Cache = Path.Combine(Root, "Cache");
	}

	public string Cache { get; }

	public string Root { get; }

	public string Scripts { get; }

	public string PathTo(string pathUnderRoot)
	{
		string path = Path.GetFullPath(Path.Combine(Root, pathUnderRoot));
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		return path;
	}

	public string Write(string pathUnderRoot, string content)
	{
		string path = PathTo(pathUnderRoot);
		File.WriteAllText(path, content);
		return path;
	}

	// Assembly loads are permanent for the process, so every fixture assembly gets its own identity.
	public string Compile(string folderUnderRoot, string source, params string[] references)
	{
		string name = "Fixture" + Guid.NewGuid().ToString("N");
		return CompileTo(Path.Combine(folderUnderRoot, name + ".dll"), name, source, references: references);
	}

	// Only assemblies the source really uses end up as references of the DLL.
	public string CompileTo(string dllUnderRoot, string assemblyName, string source, string embeddedResourceFile = null, string version = null, string strongNameKeyFile = null, params string[] references)
	{
		string dll = PathTo(dllUnderRoot);
		if (version != null)
		{
			source = "[assembly: System.Reflection.AssemblyVersion(\"" + version + "\")]\n" + source;
		}
		var compilation = CSharpCompilation.Create(
			assemblyName,
			new[] { CSharpSyntaxTree.ParseText(source) },
			defaultReferences.Concat(references.Select(r => (MetadataReference)MetadataReference.CreateFromFile(r))),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, cryptoKeyFile: strongNameKeyFile, strongNameProvider: new DesktopStrongNameProvider()));
		var resources = embeddedResourceFile == null
			? Enumerable.Empty<ResourceDescription>()
			: new[] { new ResourceDescription(assemblyName + "." + Path.GetFileName(embeddedResourceFile), () => File.OpenRead(embeddedResourceFile), isPublic: true) };
		using (FileStream stream = File.Create(dll))
		{
			var result = compilation.Emit(stream, manifestResources: resources);
			if (!result.Success)
			{
				throw new InvalidOperationException("Fixture source did not compile:" + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics) + Environment.NewLine + source);
			}
		}
		return dll;
	}

	// Like ComicRack, scans every folder with the same engine and then completes the initialization once.
	public LoadedPlugins Load(params string[] moreFoldersUnderRoot)
	{
		PluginConsole.Install();
		PluginEngine engine = new PluginEngine(Cache);
		engine.Initialize(new StubEnvironment(), Scripts);
		foreach (string folder in moreFoldersUnderRoot)
		{
			engine.Initialize(new StubEnvironment(), PathTo(folder));
		}
		engine.CompleteInitialization();
		return new LoadedPlugins(engine, Root);
	}

	public string CreateStrongNameKey()
	{
		string file = PathTo("key.snk");
		using (var rsa = new System.Security.Cryptography.RSACryptoServiceProvider(1024))
		{
			File.WriteAllBytes(file, rsa.ExportCspBlob(includePrivateParameters: true));
		}
		return file;
	}

	public static bool IsExecutionLoaded(string dll)
	{
		return AppDomain.CurrentDomain.GetAssemblies().Any(a => !a.IsDynamic && string.Equals(a.Location, dll, StringComparison.OrdinalIgnoreCase));
	}

	public void Dispose()
	{
		try
		{
			Directory.Delete(Root, recursive: true);
		}
		catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
		{
			// Loaded plugin DLLs stay locked until the process exits.
		}
	}
}

internal sealed class LoadedPlugins
{
	private readonly string root;

	public LoadedPlugins(PluginEngine engine, string root)
	{
		Engine = engine;
		this.root = root;
	}

	public PluginEngine Engine { get; }

	public string ConsoleLinesAboutThisFixture => PluginConsole.LinesMentioning(root);

	public IEnumerable<string> Hooks(string key) => Engine.GetAllCommands().Where(c => c.Key == key).Select(c => c.Hook);

	public Command Command(string key, string hook) => Engine.GetAllCommands().SingleOrDefault(c => c.Key == key && c.Hook == hook);
}

// The script console is one static stream for the process, so tests find their own lines by a text only they write.
internal static class PluginConsole
{
	private static readonly CaptureStream capture = new CaptureStream();

	public static void Install()
	{
		Command.Output = capture;
		Command.EnableLog = true;
	}

	public static string LinesMentioning(string text)
	{
		return string.Join("\n", capture.Text.Split('\n').Where(line => line.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).Select(line => line.TrimEnd('\r')));
	}

	private sealed class CaptureStream : Stream
	{
		private readonly StringBuilder text = new StringBuilder();

		public string Text
		{
			get
			{
				lock (text)
				{
					return text.ToString();
				}
			}
		}

		public override bool CanRead => false;

		public override bool CanSeek => false;

		public override bool CanWrite => true;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			lock (text)
			{
				text.Append(Encoding.UTF8.GetString(buffer, offset, count));
			}
		}

		public override void Flush()
		{
		}

		// Command.LogDefault closes its writer after every line; the console has to outlive that.
		protected override void Dispose(bool disposing)
		{
		}

		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();
	}
}

internal static class Source
{
	private const string Usings = "using System; using System.Collections.Generic; using System.Drawing; using System.Windows.Forms; using cYo.Projects.ComicRack.Engine; using cYo.Projects.ComicRack.Plugins;\n";

	public static string Types(string types) => Usings + "namespace Fixture {\n" + types + "\n}";

	// Members of a fixture class named Plugin that keep the context of the constructor.
	public const string Context = "private readonly IPluginContext context; public Plugin(IPluginContext context) { this.context = context; }\n";

	// attribute holds more arguments of the [Plugin] attribute, such as "Enabled = false".
	public static string Plugin(string key, string hooks, string members, string className = "Plugin", string attribute = null)
	{
		return Types($"[Plugin(\"{key}\"{(attribute == null ? "" : ", " + attribute)})]\npublic class {className} : {hooks} {{\n{members}\n}}");
	}
}
