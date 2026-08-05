using System.Windows;
using RocoPilot.Core;
using Wpf.Ui.Controls;

namespace RocoPilot.ToolUi;

public interface IToolUi : ITool
{
    SymbolRegular Icon { get; }

    FrameworkElement CreateConfigPanel(object settings, Action persist);
}
