using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;

namespace ManifoldEditor.App;

/// <summary>
/// The application, without App.xaml: it loads WinUI's control styles itself, and answers
/// WinUI's type lookups with the controls' own metadata provider (the XAML compiler would
/// otherwise generate one).
/// </summary>
public sealed class App : Application, IXamlMetadataProvider
{
    readonly XamlControlsXamlMetaDataProvider controls = new();
    MainWindow? window;

    public App()
    {
        UnhandledException += (_, e) =>
        {
            System.Diagnostics.Debug.WriteLine(e.Exception);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        window = new MainWindow();
        window.Activate();
    }

    public IXamlType GetXamlType(Type type) => controls.GetXamlType(type);
    public IXamlType GetXamlType(string fullName) => controls.GetXamlType(fullName);
    public XmlnsDefinition[] GetXmlnsDefinitions() => controls.GetXmlnsDefinitions();
}
