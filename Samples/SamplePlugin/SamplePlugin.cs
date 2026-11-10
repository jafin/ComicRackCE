using System;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;

[assembly: PluginApiVersion(1)]

namespace SamplePlugin
{
	// A compiled plugin. Build the project and copy SamplePlugin.dll into a folder below the Scripts folder of ComicRack.
	//
	// The attribute describes the plugin. ComicRack reads it when it scans the script folders and does not create the class then.
	// The class gets one command for each hook interface it implements. All commands share one instance.
	[Plugin("SamplePlugin", Name = "[Code Sample] Count Books (C#)", Description = "Shows how many books a command gets", Image = "SamplePlugin.png")]
	public sealed class SamplePlugin : IBooks, ILibrary, IComicInfoHtml, IConfig, IStartup, IDisposable
	{
		private readonly IPluginContext context;

		private readonly string createdAt;

		// Runs once, when the first hook of the plugin runs. It does not run when the plugin is disabled.
		public SamplePlugin(IPluginContext context)
		{
			this.context = context;
			createdAt = DateTime.Now.ToShortTimeString();
			Console.WriteLine("SamplePlugin loaded from " + context.ScriptPath);
		}

		void IStartup.Execute()
		{
			Console.WriteLine("SamplePlugin started");
		}

		// IBooks and ILibrary have the same Execute signature. A public Execute(ComicBook[]) method would serve both hooks.
		// Explicit implementations give each hook its own code.
		void IBooks.Execute(ComicBook[] books)
		{
			Show(books.Length + " selected books, " + books.Sum(book => book.PageCount) + " pages");
		}

		void ILibrary.Execute(ComicBook[] books)
		{
			Show(books.Length + " books in the library");
		}

		string IComicInfoHtml.Execute(ComicBook[] books)
		{
			string items = string.Concat(books.Select(book => "<li>" + WebUtility.HtmlEncode(book.Caption) + "</li>"));
			return "<html><body><p>Sample plugin, created at " + createdAt + "</p><ul>" + items + "</ul></body></html>";
		}

		// The Configure button of the plugin in the Preferences, and the arrow of its toolbar button.
		void IConfig.Execute()
		{
			Show("The plugin is in " + context.ScriptPath);
		}

		// Runs when ComicRack closes, if a hook of the plugin has run.
		public void Dispose()
		{
			Console.WriteLine("SamplePlugin disposed");
		}

		private void Show(string text)
		{
			MessageBox.Show(context.ComicRack.MainWindow, text, "Sample plugin");
		}
	}
}
