using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;

namespace ComicRack.Plugins.Tests;

public class HookTests
{
	private static readonly Type[] hookInterfaces = typeof(IDotNetCommand).Assembly.GetTypes()
		.Where(t => t.IsInterface && t != typeof(IDotNetCommand) && typeof(IDotNetCommand).IsAssignableFrom(t))
		.ToArray();

	[Test]
	public async Task Every_hook_interface_of_the_api_yields_a_command_for_its_own_hook()
	{
		using var scripts = new ScriptsFolder();
		scripts.Compile(@"Scripts\Mine", Source.Plugin("Everything", string.Join(", ", hookInterfaces.Select(t => t.Name)), string.Join("\n", hookInterfaces.Select(ExplicitImplementation))));

		var loaded = scripts.Load();
		// The engine keeps a ConfigScript command as the Configure action of the other commands, not in its command list.
		string[] hooks = loaded.Hooks("Everything").Append(loaded.Command("Everything", PluginEngine.ScriptTypeStartup).Configure?.Hook).ToArray();

		await Assert.That(hooks.Length).IsEqualTo(hookInterfaces.Length);
		await Assert.That(hooks).IsEquivalentTo(PluginEngine.ValidHooks.Keys);
	}

	[Test]
	public async Task Each_hook_accepts_the_arguments_the_host_passes()
	{
		using var scripts = new ScriptsFolder();
		scripts.Compile(@"Scripts\Mine", Source.Plugin("All", string.Join(", ", hookInterfaces.Select(t => t.Name)), """
			private readonly List<string> calls = new List<string>();
			IEnumerable<ComicBook> ICreateBookList.Execute(ComicBook[] books, string a, string b) { calls.Add("CreateBookList " + books.Length + " " + a + " " + b); return books; }
			void IParseComicPath.Execute(string path, ComicNameInfo proposed) { calls.Add("ParseComicPath " + path); proposed.Series = "Parsed"; }
			void ILibrary.Execute(ComicBook[] books) { calls.Add("Library " + books.Length); }
			void IEditor.Execute(ComicBook[] books) { calls.Add("Editor " + books.Length); }
			void IBooks.Execute(ComicBook[] books) { calls.Add("Books " + books.Length); }
			void INewBooks.Execute(ComicBook[] books) { calls.Add("NewBooks " + books.Length); }
			void IBookOpened.Execute(ComicBook book) { calls.Add("BookOpened " + book.Series); }
			void IReaderResized.Execute(int width, int height) { calls.Add("ReaderResized " + width + "x" + height); }
			Dictionary<string, string> ISearch.Execute(string hint, string text, int limit) { return new Dictionary<string, string> { { hint + " " + text, limit.ToString() } }; }
			void IConfig.Execute() { calls.Add("Config"); }
			void IStartup.Execute() { calls.Add("Startup"); }
			bool IShutdown.Execute(bool userIsClosing) { return !userIsClosing; }
			string IComicInfoHtml.Execute(ComicBook[] books) { return string.Join("|", calls); }
			Control IComicInfoUI.Execute() { return new Label { Text = "info" }; }
			string IQuickOpenHtml.Execute(ComicBook[] books) { return "quick " + books.Length; }
			Control IQuickOpenUI.Execute() { return new Label { Text = "quick" }; }
			void IDrawThumbnailOverlay.Execute(ComicBook book, Graphics graphics, Rectangle bounds, int flags) { calls.Add("Overlay " + bounds.Width + " " + flags + " " + (graphics != null)); }
			"""));
		var loaded = scripts.Load();
		Func<string, Command> command = hook => loaded.Command("All", hook);
		Command config = command(PluginEngine.ScriptTypeLibrary).Configure;
		ComicBook book = new ComicBook { Series = "Saga" };
		ComicBook[] books = { book, new ComicBook() };
		ComicNameInfo proposed = new ComicNameInfo();

		// The argument arrays below are the ones built in ScriptUtility, MainForm, Program, ComicBookDialog, PreferencesDialog and ComicBookPluginMatcher.
		object list = command(PluginEngine.ScriptTypeCreateBookList).Invoke(new object[] { books, "x", "y" });
		command(PluginEngine.ScriptTypeParseComicPath).Invoke(new object[] { @"C:\Comics\a.cbz", proposed }, catchErrors: true);
		command(PluginEngine.ScriptTypeLibrary).Invoke(new object[] { books });
		command(PluginEngine.ScriptTypeEditor).Invoke(new object[] { new[] { book } });
		command(PluginEngine.ScriptTypeBooks).Invoke(new object[] { books });
		command(PluginEngine.ScriptTypeNewBooks).Invoke(new object[] { books });
		loaded.Engine.Invoke(PluginEngine.ScriptTypeBookOpened, new object[] { book });
		loaded.Engine.Invoke(PluginEngine.ScriptTypeReaderResized, new object[] { 800, 600 });
		object found = command(PluginEngine.ScriptTypeSearch).Invoke(new object[] { "series", "saga", -1 });
		config.Invoke(null, catchErrors: true);
		config.Invoke(new object[0], catchErrors: true);
		loaded.Engine.Invoke(PluginEngine.ScriptTypeStartup, new object[0]);
		object mayClose = command(PluginEngine.ScriptTypeShutdown).Invoke(new object[] { true });
		object infoControl = command(PluginEngine.ScriptTypeComicInfoUI).Invoke(null);
		object quickHtml = command(PluginEngine.ScriptTypeQuickOpenHtml).Invoke(new object[] { books });
		object quickControl = command(PluginEngine.ScriptTypeQuickOpenUI).Invoke(null);
		using (Bitmap bitmap = new Bitmap(4, 4))
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			command(PluginEngine.ScriptTypeDrawThumbnailOverlay).Invoke(new object[] { book, graphics, new Rectangle(0, 0, 4, 4), 6 }, catchErrors: true);
		}
		object calls = command(PluginEngine.ScriptTypeComicInfoHtml).Invoke(new object[] { books });

		await Assert.That(calls).IsEqualTo(@"CreateBookList 2 x y|ParseComicPath C:\Comics\a.cbz|Library 2|Editor 1|Books 2|NewBooks 2|BookOpened Saga|ReaderResized 800x600|Config|Config|Startup|Overlay 4 6 True");
		await Assert.That(((IEnumerable<ComicBook>)list).Count()).IsEqualTo(2);
		await Assert.That(proposed.Series).IsEqualTo("Parsed");
		await Assert.That(((Dictionary<string, string>)found)["series saga"]).IsEqualTo("-1");
		await Assert.That((bool)mayClose).IsFalse();
		await Assert.That(((Control)infoControl).Text).IsEqualTo("info");
		await Assert.That(quickHtml).IsEqualTo("quick 2");
		await Assert.That(((Control)quickControl).Text).IsEqualTo("quick");
	}

	[Test]
	public async Task A_hook_matches_whole_names_only()
	{
		using var scripts = new ScriptsFolder();
		scripts.Compile(@"Scripts\Mine", Source.Plugin("OnlyNew", "INewBooks", "void INewBooks.Execute(ComicBook[] books) { }"));

		var loaded = scripts.Load();

		await Assert.That(loaded.Engine.GetCommands(PluginEngine.ScriptTypeNewBooks).Select(c => c.Key)).Contains("OnlyNew");
		await Assert.That(loaded.Engine.GetCommands(PluginEngine.ScriptTypeBooks).Select(c => c.Key)).DoesNotContain("OnlyNew");
	}

	[Test]
	public async Task A_comma_separated_hook_list_matches_each_name_despite_spaces()
	{
		using var scripts = new ScriptsFolder();
		scripts.Write(@"Scripts\Mine\a.py", "#@Name run\n#@Key Multi\n#@Hook Books,  Editor\ndef run(books):\n\tpass\n");

		var loaded = scripts.Load();

		await Assert.That(loaded.Engine.GetCommands(PluginEngine.ScriptTypeBooks).Select(c => c.Key)).Contains("Multi");
		await Assert.That(loaded.Engine.GetCommands(PluginEngine.ScriptTypeEditor).Select(c => c.Key)).Contains("Multi");
		await Assert.That(loaded.Engine.GetCommands(PluginEngine.ScriptTypeLibrary).Select(c => c.Key)).DoesNotContain("Multi");
	}

	private static string ExplicitImplementation(Type hook)
	{
		return string.Join("\n", hook.GetMethods().Select(m =>
			$"{CSharpName(m.ReturnType)} {hook.Name}.{m.Name}({string.Join(", ", m.GetParameters().Select(p => CSharpName(p.ParameterType) + " " + p.Name))}) {{ {(m.ReturnType == typeof(void) ? "" : "return default(" + CSharpName(m.ReturnType) + ");")} }}"));
	}

	private static string CSharpName(Type type)
	{
		if (type == typeof(void))
		{
			return "void";
		}
		if (type.IsArray)
		{
			return CSharpName(type.GetElementType()) + "[]";
		}
		if (type.IsGenericType)
		{
			return type.FullName.Substring(0, type.FullName.IndexOf('`')) + "<" + string.Join(", ", type.GetGenericArguments().Select(CSharpName)) + ">";
		}
		return type.FullName;
	}
}
