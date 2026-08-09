using System.Windows.Input;
using RocoPilot.Shell.Hotkeys;

namespace RocoPilot.Shell.Tests;

public class HotkeyBindingTests
{
    [Fact]
    public void ParsesPlainFunctionKey()
    {
        Assert.True(HotkeyBinding.TryParse("F12", out var binding));
        Assert.Equal(Key.F12, binding.Key);
        Assert.Equal(ModifierKeys.None, binding.Modifiers);
    }

    [Fact]
    public void ParsesChordWithCaseInsensitiveParts()
    {
        Assert.True(HotkeyBinding.TryParse("ctrl+alt+f9", out var binding));
        Assert.Equal(Key.F9, binding.Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, binding.Modifiers);
    }

    [Fact]
    public void ParsesAllModifiers()
    {
        Assert.True(HotkeyBinding.TryParse("Ctrl+Alt+Shift+Win+K", out var binding));
        Assert.Equal(Key.K, binding.Key);
        Assert.Equal(
            ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows,
            binding.Modifiers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Alt")]
    [InlineData("NotAKey")]
    [InlineData("Ctrl+NotAKey")]
    public void RejectsInvalidText(string text) =>
        Assert.False(HotkeyBinding.TryParse(text, out _));

    [Fact]
    public void FormatRoundTripsThroughParse()
    {
        var text = HotkeyBinding.Format(Key.F12, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.Equal("Ctrl+Shift+F12", text);
        Assert.True(HotkeyBinding.TryParse(text, out var binding));
        Assert.Equal(Key.F12, binding.Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, binding.Modifiers);
    }

    [Fact]
    public void Win32ModifiersIncludeNoRepeatAndMappedFlags()
    {
        var binding = new HotkeyBinding(Key.F12, ModifierKeys.Control);
        var mods = binding.Win32Modifiers;
        Assert.True((mods & HotkeyBinding.ModNoRepeat) != 0);
        Assert.True((mods & HotkeyBinding.ModControl) != 0);
        Assert.Equal(0u, mods & HotkeyBinding.ModAlt);
        Assert.Equal(0x7B, binding.VirtualKey);
    }
}