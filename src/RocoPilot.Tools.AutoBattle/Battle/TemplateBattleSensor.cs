using System.IO;
using RocoPilot.Dispatch;

namespace RocoPilot.Tools.AutoBattle.Battle;

public sealed class TemplateBattleSensor : IBattleSensor, IDisposable
{
    private static readonly (BattlePanel Panel, double CX, double CY)[] s_buttons =
    [
        (BattlePanel.Flee,    0.743, 0.911),
        (BattlePanel.Bag,     0.799, 0.913),
        (BattlePanel.Capture, 0.857, 0.911),
        (BattlePanel.Switch,  0.896, 0.910),
        (BattlePanel.Skill,   0.953, 0.916),
    ];

    private static readonly (double CX, double CY)[] s_skillSlots =
    [
        (0.188, 0.375),
        (0.169, 0.506),
        (0.178, 0.637),
        (0.215, 0.750),
    ];

    private const double PanelThreshold = 0.70;
    private const double SkillThreshold = 0.65;

    private const double BtnSearchR = 0.04;

    private const double SkillSearchR = 0.06;

    private readonly Dictionary<BattlePanel, (TemplateMatcher On, TemplateMatcher Off)> _panelMatchers = new();
    private readonly Dictionary<string, TemplateMatcher[]> _skillMatchers = new();

    public TemplateBattleSensor(string panelTemplateDir, string skillTemplateDir)
    {
        string[] names = ["flee", "bag", "capture", "switch", "skill"];
        BattlePanel[] panels = [BattlePanel.Flee, BattlePanel.Bag, BattlePanel.Capture, BattlePanel.Switch, BattlePanel.Skill];

        for (var i = 0; i < names.Length; i++)
        {
            var (cx, cy) = (s_buttons[i].CX, s_buttons[i].CY);
            var region = AnchorRegion(cx, cy, BtnSearchR);
            var onPath = Path.Combine(panelTemplateDir, $"{names[i]}-on.png");
            var offPath = Path.Combine(panelTemplateDir, $"{names[i]}-off.png");
            _panelMatchers[panels[i]] = (
                TemplateMatcher.Load(onPath, region, PanelThreshold),
                TemplateMatcher.Load(offPath, region, PanelThreshold));
        }

        if (Directory.Exists(skillTemplateDir))
        {
            foreach (var file in Directory.GetFiles(skillTemplateDir, "*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                _skillMatchers[name] = [.. s_skillSlots.Select(s => TemplateMatcher.Load(file, AnchorRegion(s.CX, s.CY, SkillSearchR), SkillThreshold))];
            }
        }
    }

    public BattlePanel? DetectSelectedPanel(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        BattlePanel? selected = null;
        var bestMargin = 0.0;

        foreach (var (panel, _, _) in s_buttons)
        {
            var (on, off) = _panelMatchers[panel];
            var onScore = on.BestScore(bgraPixels, width, height);
            var offScore = off.BestScore(bgraPixels, width, height);

            var margin = onScore - offScore;
            if (onScore >= PanelThreshold && margin > bestMargin)
            {
                bestMargin = margin;
                selected = panel;
            }
        }

        return selected;
    }

    public (int Slot, int ScreenX, int ScreenY)? MatchSkill(
        ReadOnlySpan<byte> bgraPixels, int width, int height, string skillName)
    {
        if (!_skillMatchers.TryGetValue(skillName, out var slotMatchers))
            return null;

        for (var slot = 0; slot < slotMatchers.Length; slot++)
        {
            if (slotMatchers[slot].Find(bgraPixels, width, height) is null)
                continue;

            var (cx, cy) = s_skillSlots[slot];
            return (slot, (int)(cx * width), (int)(cy * height));
        }

        return null;
    }

    private static (double X, double Y, double W, double H) AnchorRegion(double cx, double cy, double radius)
        => (cx - radius, cy - radius, radius * 2, radius * 2);

    public void Dispose()
    {
        foreach (var (on, off) in _panelMatchers.Values)
        {
            on.Dispose();
            off.Dispose();
        }

        foreach (var slotMatchers in _skillMatchers.Values)
            foreach (var matcher in slotMatchers)
                matcher.Dispose();
    }
}
