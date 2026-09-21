using RocoPilot.Settings;

namespace RocoPilot.Shell.Hotkeys;

public interface IHotkeyRegistry
{
    bool Register(string owner, string hotkey, HotkeyScope scope, Action callback, bool swallow);

    void Unregister(string owner);
}
