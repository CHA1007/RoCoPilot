namespace RocoPilot.Detection;

public interface IUiStateMatcher
{
    UiStateMatch? Match(ReadOnlySpan<byte> bgraPixels, int width, int height, string stateName);
}

public sealed record UiStateMatch(string StateName, float Confidence);
