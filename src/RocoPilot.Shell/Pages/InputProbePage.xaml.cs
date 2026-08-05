using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RocoPilot.Input;
using RocoPilot.Input.Interception;

namespace RocoPilot.Shell.Pages;

public partial class InputProbePage : Page
{
    private static readonly TimeSpan DiscoverTimeout = TimeSpan.FromSeconds(10);

    private readonly Brush _defaultStatusBrush;
    private IInputDriver? _driver;

    public InputProbePage()
    {
        InitializeComponent();
        _defaultStatusBrush = StatusText.Foreground;
        Unloaded += (_, _) => DisposeDriver();
    }

    private IInputDriver Driver => _driver ??= InputDriverFactory.Create();

    private async void OnArm(object sender, RoutedEventArgs e)
    {
        var driver = Driver;
        SetButtonsEnabled(false);
        SetStatus("设备发现中：10 秒内动一下鼠标……（收得到事件＝驱动真在设备栈）", isError: false);

        try
        {
            await Task.Run(() => driver.Arm(DiscoverTimeout));
            SetStatus($"✔ 设备发现成功（{driver.BackendName}）。可以「③ 发送探针」了。", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"✘ 设备发现失败：{ex.Message}", isError: true);
        }
        finally
        {
            SetButtonsEnabled(true);
        }
    }

    private async void OnProbe(object sender, RoutedEventArgs e)
    {
        if (!TryReadAmount(out var amount)) return;

        var driver = Driver;
        SetButtonsEnabled(false);
        try
        {
            await CountdownToFocus();
            await Task.Run(() => driver.MoveRelative(amount, 0));
            SetStatus($"✔ 已发送探针：相对移动 {amount} count（{driver.BackendName}）。" +
                      "游戏聚焦段看镜头转没转（光标与镜头同动＝成）；桌面段看光标动没动。", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"✘ 探针失败：{ex.Message}", isError: true);
        }
        finally
        {
            SetButtonsEnabled(true);
        }
    }

    private async void OnClick(object sender, RoutedEventArgs e)
    {
        var driver = Driver;
        SetButtonsEnabled(false);
        try
        {
            await CountdownToFocus();
            await Task.Run(() => driver.KeyPress(InputKey.LeftMouse, 120));
            SetStatus($"✔ 已发送左键点击（{driver.BackendName}）。", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"✘ 左键点击失败：{ex.Message}", isError: true);
        }
        finally
        {
            SetButtonsEnabled(true);
        }
    }

    private async void OnArmKey(object sender, RoutedEventArgs e)
    {
        var interception = (InterceptionDriver)Driver;

        SetButtonsEnabled(false);
        SetStatus("键盘设备发现中：10 秒内随便按一个键……", isError: false);
        try
        {
            await Task.Run(() => interception.DiscoverKey(DiscoverTimeout));
            SetStatus("✔ 键盘设备发现成功。可以「测试空格」了。", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"✘ 键盘设备发现失败：{ex.Message}", isError: true);
        }
        finally
        {
            SetButtonsEnabled(true);
        }
    }

    private async void OnKeyTest(object sender, RoutedEventArgs e)
    {
        var driver = Driver;
        SetButtonsEnabled(false);
        try
        {
            await CountdownToFocus();
            await Task.Run(() => driver.KeyPress(InputKey.Parse("space"), 120));
            SetStatus($"✔ 已发送空格按压（{driver.BackendName}）。", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"✘ 空格按压失败：{ex.Message}", isError: true);
        }
        finally
        {
            SetButtonsEnabled(true);
        }
    }

    private async Task CountdownToFocus()
    {
        for (var i = 3; i > 0; i--)
        {
            SetStatus($"{i}…… 现在去点目标窗口聚焦（游戏＝转镜头 / 桌面＝动光标），随后自动发射。", isError: false);
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    private bool TryReadAmount(out int amount)
    {
        if (int.TryParse(AmountBox.Text?.Trim(), out amount) && amount != 0)
        {
            return true;
        }

        SetStatus("✘ 探针移动量需为非零整数（如 400）。", isError: true);
        amount = 0;
        return false;
    }

    private void SetButtonsEnabled(bool enabled)
    {
        ArmButton.IsEnabled = enabled;
        ProbeButton.IsEnabled = enabled;
        ClickButton.IsEnabled = enabled;
        ArmKeyButton.IsEnabled = enabled;
        KeyTestButton.IsEnabled = enabled;
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError ? Brushes.OrangeRed : _defaultStatusBrush;
    }

    private void DisposeDriver()
    {
        _driver?.Dispose();
        _driver = null;
    }
}
