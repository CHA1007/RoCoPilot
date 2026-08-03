using System.IO;
using OpenCvSharp;

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

    private readonly Dictionary<BattlePanel, (Mat On, Mat Off)> _panelTemplates = new();
    private readonly Dictionary<string, Mat> _skillTemplates = new();

    public TemplateBattleSensor(string panelTemplateDir, string skillTemplateDir)
    {

        string[] names = ["flee", "bag", "capture", "switch", "skill"];
        BattlePanel[] panels = [BattlePanel.Flee, BattlePanel.Bag, BattlePanel.Capture, BattlePanel.Switch, BattlePanel.Skill];

        for (var i = 0; i < names.Length; i++)
        {
            var onPath = Path.Combine(panelTemplateDir, $"{names[i]}-on.png");
            var offPath = Path.Combine(panelTemplateDir, $"{names[i]}-off.png");
            var on = LoadTemplate(onPath);
            var off = LoadTemplate(offPath);
            _panelTemplates[panels[i]] = (on, off);
        }

        if (Directory.Exists(skillTemplateDir))
        {
            foreach (var file in Directory.GetFiles(skillTemplateDir, "*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                _skillTemplates[name] = LoadTemplate(file);
            }
        }
    }

    public BattlePanel? DetectSelectedPanel(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        using var bgr = ToBgrMat(bgraPixels, width, height);

        BattlePanel? selected = null;
        var bestMargin = 0.0;

        foreach (var (panel, cx, cy) in s_buttons)
        {
            var (onTpl, offTpl) = _panelTemplates[panel];
            var onScore = MatchAt(bgr, onTpl, cx, cy, BtnSearchR, width, height);
            var offScore = MatchAt(bgr, offTpl, cx, cy, BtnSearchR, width, height);

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
        if (!_skillTemplates.TryGetValue(skillName, out var template))
            return null;

        using var bgr = ToBgrMat(bgraPixels, width, height);

        for (var slot = 0; slot < s_skillSlots.Length; slot++)
        {
            var (cx, cy) = s_skillSlots[slot];
            var score = MatchAt(bgr, template, cx, cy, SkillSearchR, width, height);
            if (score >= SkillThreshold)
            {
                return (slot, (int)(cx * width), (int)(cy * height));
            }
        }

        return null;
    }

    private static double MatchAt(Mat bgr, Mat template, double cx, double cy, double radius, int width, int height)
    {
        var searchW = (int)(radius * 2 * width);
        var searchH = (int)(radius * 2 * height);
        var x0 = Math.Max(0, (int)(cx * width - radius * width));
        var y0 = Math.Max(0, (int)(cy * height - radius * height));
        searchW = Math.Min(searchW, width - x0);
        searchH = Math.Min(searchH, height - y0);

        if (searchW < template.Cols || searchH < template.Rows)
            return 0;

        using var roi = new Mat(bgr, new Rect(x0, y0, searchW, searchH));
        using var result = new Mat();
        Cv2.MatchTemplate(roi, template, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out _);
        return maxVal;
    }

    private static Mat ToBgrMat(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        var frame = Mat.FromPixelData(height, width, MatType.CV_8UC4, bgraPixels.ToArray());
        var bgr = new Mat();
        Cv2.CvtColor(frame, bgr, ColorConversionCodes.BGRA2BGR);
        frame.Dispose();
        return bgr;
    }

    private static Mat LoadTemplate(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"模板图不存在：{path}", path);
        var mat = Cv2.ImRead(path, ImreadModes.Color);
        if (mat.Empty())
            throw new InvalidOperationException($"模板图无法解码：{path}");
        return mat;
    }

    public void Dispose()
    {
        foreach (var (on, off) in _panelTemplates.Values)
        {
            on.Dispose();
            off.Dispose();
        }

        foreach (var tpl in _skillTemplates.Values)
            tpl.Dispose();
    }
}
