namespace RocoPilot.Dispatch;

/// <summary>游戏场景枚举。</summary>
public enum GameScene
{
    /// <summary>无法判定（过渡动画 / 加载画面）。</summary>
    Unknown,

    /// <summary>开放世界（可丢球）。</summary>
    OpenWorld,

    /// <summary>战斗界面。</summary>
    Battle,
}
