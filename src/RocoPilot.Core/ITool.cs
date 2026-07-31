using System.Windows;
using Wpf.Ui.Controls;

namespace RocoPilot.Core;

public interface ITool
{
    string Id { get; }

    string DisplayName { get; }

    SymbolRegular Icon { get; }

    Type SettingsType { get; }

    object CreateDefaultSettings();

    IRunningTask Run(object settings);

    FrameworkElement CreateConfigPanel(object settings, Action persist);
}
