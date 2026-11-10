# .NET plugin samples

A .NET plugin is a class that has the `[Plugin]` attribute and implements one or more hook interfaces, for example `IBooks` or `IComicInfoHtml`. ComicRack gives the class one command for each hook interface.

```c#
[Plugin("MyPlugin", Name = "My Plugin", Description = "Does something", Image = "MyPlugin.png")]
public sealed class MyPlugin : IBooks
{
    private readonly IPluginContext context;

    public MyPlugin(IPluginContext context) { this.context = context; }

    void IBooks.Execute(ComicBook[] books) { ... }
}
```

You can ship a plugin in two forms. Both use the same classes and interfaces.

| Form | What you ship | Sample |
| --- | --- | --- |
| Source plugin | A folder with a `<Name>.csplugin` file and `.cs` files. ComicRack compiles it. | `SampleSourcePlugin` |
| Compiled plugin | A DLL that you build. | `SamplePlugin` |

Prefer a source plugin. Users can read what it does, and it needs no build.

## Install a sample

1. Build the solution. The build puts `SampleLibrary.dll` into `SampleSourcePlugin\lib`.
2. Open the script folder: `%AppData%\cYo\ComicRack Community Edition\Scripts`.
3. For the source sample, copy the folder `SampleSourcePlugin` into the script folder.
4. For the compiled sample, make a folder `SamplePlugin` in the script folder and copy `SamplePlugin\bin\Debug\net48\SamplePlugin.dll` into it.
5. Start ComicRack.

To see the messages of plugins and compile errors, start ComicRack with the script console: `ComicRack.exe -ssc`.

## Write a plugin class

- Describe the plugin in the `[Plugin]` attribute. The `Key` is required. `Name`, `Description`, `Image`, `Enabled` and `PCount` are optional, and `Name` defaults to the `Key`. `PCount` is the number of parameters of an `ICreateBookList` hook.
- The class must be public, not abstract and not generic. A derived class does not inherit the attribute.
- Give the class a public constructor that takes an `IPluginContext`, or a public constructor without parameters. The context has the properties `ComicRack` (the host) and `ScriptPath` (the plugin folder).
- ComicRack does not create the class when it scans the script folders. It creates one instance when a hook of the class runs for the first time, and that instance serves all hooks of the class. A plugin that is disabled is never created.
- Do the initialization in the constructor. If the constructor throws, the hooks of the class fail with that exception and ComicRack does not call the constructor again.
- If the class implements `IDisposable`, ComicRack calls `Dispose` when it closes, but only if it created the instance.
- ComicRack skips a class that does not follow these rules and writes the reason to the script console. This includes a class that implements a hook interface but has no `[Plugin]` attribute.
- Some hooks have the same `Execute` signature, for example `ILibrary`, `IEditor`, `IBooks` and `INewBooks`. To give each hook its own code, implement the interfaces explicitly: `void IBooks.Execute(ComicBook[] books) { ... }`.
- The `Key` must be unique. ComicRack skips a plugin whose `Key` another plugin or script already uses.

## Source plugins

The `.csplugin` file marks the folder as a source plugin. Its name is the name of the plugin. It is an INI file, and all entries are optional:

```ini
[Plugin]
References=lib/Some.dll;lib/Other.dll
FrameworkReferences=System.Web
LangVersion=latest
```

- ComicRack compiles all `.cs` files of the folder and its subfolders into one assembly. It ignores the subfolders `bin` and `obj`.
- `References` lists the DLLs that the plugin ships, relative to the plugin folder.
- `FrameworkReferences` lists more assemblies of the .NET Framework or of ComicRack by name. ComicRack always adds the common ones, such as `System`, `System.Core`, `System.Drawing` and `System.Windows.Forms`, and its own plugin API.
- ComicRack does not load a DLL in the folder of a source plugin as a plugin. The source is the plugin.
- ComicRack keeps the compiled assembly in its cache folder and compiles again when a source file, the `.csplugin` file or a reference changes.
- A compile error is written to the script console with file, line and column, and ComicRack skips the plugin.
- A compile warning is written to the script console after the line "Compiling source plugin", followed by the number of warnings. ComicRack compiles only when something changed, so a start from the cache writes no warnings.
- Plugins run on .NET Framework 4.8. Language features that need a newer runtime, such as `init` accessors and records, do not compile unless the plugin declares the missing types itself.

## Third-party DLLs

Put the DLLs that your plugin needs into the plugin folder or into a `lib` subfolder. For a source plugin, also list them in `References`.

- A package file (`.crplugin`) is installed into one folder without subfolders. A reference `lib/Some.dll` is then found as `Some.dll` in the plugin folder.
- ComicRack loads a DLL without a strong name once for each name. If two plugins ship different versions of such a DLL, both plugins use the version that is needed first. The script console shows a warning.
- Different versions of a strong-named DLL load side by side, so each plugin gets its own version.
