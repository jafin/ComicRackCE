using System;
using System.Linq;
using System.Net;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;
using SampleLibrary;

[assembly: PluginApiVersion(1)]

namespace SampleSourcePlugin
{
	// A source plugin. Copy this folder into the Scripts folder of ComicRack; there is nothing to build.
	// It uses SampleLibrary.dll from the lib folder, which SampleSourcePlugin.csplugin lists as a reference.
	[Plugin("SampleSourcePlugin", Name = "[Code Sample] Slugs (C# source)", Description = "Shows a URL-friendly name for the selected books")]
	public sealed class SampleSourcePlugin : IComicInfoHtml, IStartup
	{
		private readonly IPluginContext context;

		public SampleSourcePlugin(IPluginContext context)
		{
			this.context = context;
		}

		void IStartup.Execute()
		{
			Console.WriteLine("SampleSourcePlugin runs from " + context.ScriptPath);
		}

		string IComicInfoHtml.Execute(ComicBook[] books)
		{
			string items = string.Concat(books.Select(book => "<li>" + WebUtility.HtmlEncode(Slug.From(book.Caption)) + "</li>"));
			return "<html><body><ul>" + items + "</ul></body></html>";
		}
	}
}
