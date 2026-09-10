using System.Text.RegularExpressions;

namespace RevitCodexBridge.Addin;

internal readonly record struct PlanPoint(double X, double Y)
{
    public static PlanPoint operator +(PlanPoint a, PlanPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static PlanPoint operator -(PlanPoint a, PlanPoint b) => new(a.X - b.X, a.Y - b.Y);
    public static PlanPoint operator *(PlanPoint a, double s) => new(a.X * s, a.Y * s);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public double Cross(PlanPoint b) => X * b.Y - Y * b.X;
    public double Dot(PlanPoint b) => X * b.X + Y * b.Y;
}
internal sealed record AxisLine(string Name, PlanPoint Start, PlanPoint End);
internal sealed record GridFrame(PlanPoint Origin, PlanPoint U, PlanPoint V, double WidthMm, double LengthMm)
{
    public PlanPoint At(double u, double v) => Origin + U * u + V * v;
    public GridFrame Swap() => new(Origin, V, U, LengthMm, WidthMm);
}
internal sealed record PlatformMember(string Kind, PlanPoint Start, PlanPoint End, double BottomMm, double TopMm);
internal sealed record PlatformDeck(PlanPoint[] Outline, double TopMm);
internal sealed record PlatformScheme(int Bays, int ColumnCount, double LongitudinalSpanMm, double MaxBeamSpanMm,
    double WidthMm, double LengthMm, List<PlatformMember> Members, List<PlatformDeck> Decks);

internal static class PlatformLayout
{
    public static string[]? ParseGridNames(string text)
    {
        var match = Regex.Match(text, @"(?<a>[A-Za-z\d]+)\s*[-－—~～]\s*(?<b>[A-Za-z\d]+)\s*轴\s*[/／、,，]\s*(?<c>[A-Za-z\d]+)\s*[-－—~～]\s*(?<d>[A-Za-z\d]+)\s*轴");
        return match.Success ? new[] { match.Groups["a"].Value, match.Groups["b"].Value, match.Groups["c"].Value, match.Groups["d"].Value } : null;
    }

    public static GridFrame Resolve(AxisLine a, AxisLine b, AxisLine c, AxisLine d)
    {
        var points = new[] { a.Start, a.End, b.Start, b.End, c.Start, c.End, d.Start, d.End };
        if (points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) throw new InvalidOperationException("轴线坐标无效。");
        PlanPoint Direction(AxisLine line)
        {
            var v = line.End - line.Start;
            if (v.Length < 1) throw new InvalidOperationException("轴线长度不足。");
            return v * (1 / v.Length);
        }
        var ad = Direction(a); var bd = Direction(b); var cd = Direction(c); var dd = Direction(d);
        if (Math.Abs(ad.Cross(bd)) > 1e-6 || Math.Abs(cd.Cross(dd)) > 1e-6 || Math.Abs(ad.Dot(cd)) > 1e-6)
            throw new InvalidOperationException("本版平台需要两组平行且相互正交的直轴网；斜交轴网不能按矩形替代。");
        PlanPoint Cross(AxisLine x, AxisLine y)
        {
            var vx = Direction(x); var vy = Direction(y);
            return x.Start + vx * ((y.Start - x.Start).Cross(vy) / vx.Cross(vy));
        }
        var o = Cross(a, c); var ux = Cross(b, c) - o; var vy2 = Cross(a, d) - o;
        if (ux.Length < 500 || vy2.Length < 500) throw new InvalidOperationException("轴网区域重合或小于500mm。");
        return new(o, ux * (1 / ux.Length), vy2 * (1 / vy2.Length), ux.Length, vy2.Length);
    }

    public static PlatformScheme Build(GridFrame frame, double equipmentWidthMm, double equipmentLengthMm,
        double sideWidthMm, double sideTopMm, double equipmentTopMm, double deckThicknessMm,
        double foundationOffsetMm, int bays, double maxSecondarySpacingMm)
    {
        var values = new[] { equipmentWidthMm, equipmentLengthMm, sideWidthMm, sideTopMm, equipmentTopMm, deckThicknessMm, foundationOffsetMm, maxSecondarySpacingMm };
        if (values.Any(v => !double.IsFinite(v))) throw new InvalidOperationException("平台尺寸必须为有限数值。");
        if (equipmentWidthMm < 500 || equipmentLengthMm < 500 || sideWidthMm < 500 || sideTopMm < 500 || equipmentTopMm <= sideTopMm
            || deckThicknessMm <= 0 || deckThicknessMm >= sideTopMm || foundationOffsetMm >= sideTopMm - deckThicknessMm
            || bays is < 1 or > 6 || maxSecondarySpacingMm < 300)
            throw new InvalidOperationException("设备/侧平台尺寸、顶高、板厚、基础偏移或分跨参数无效。");
        var width = equipmentWidthMm + 2 * sideWidthMm;
        if (width > frame.WidthMm + 0.1 || equipmentLengthMm > frame.LengthMm + 0.1)
            throw new InvalidOperationException("设备平台加两侧操作带超出轴网区域；请减小尺寸或调整操作带方向。");
        var ox = (frame.WidthMm - width) / 2; var oy = (frame.LengthMm - equipmentLengthMm) / 2;
        var xs = new[] { 0.0, sideWidthMm, sideWidthMm + equipmentWidthMm, width };
        var members = new List<PlatformMember>();
        PlanPoint P(double x, double y) => frame.At(ox + x, oy + y);
        for (var row = 0; row < 4; row++)
        {
            var high = (row is 0 or 3 ? sideTopMm : equipmentTopMm) - deckThicknessMm;
            for (var station = 0; station <= bays; station++)
            {
                var y = equipmentLengthMm * station / bays;
                members.Add(new("column", P(xs[row], y), P(xs[row], y), foundationOffsetMm, high));
            }
            // Inner columns are shared by the lower operating deck and upper equipment deck.
            var heights = row is 0 or 3 ? new[] { sideTopMm - deckThicknessMm } : new[] { sideTopMm - deckThicknessMm, equipmentTopMm - deckThicknessMm };
            foreach (var z in heights)
                for (var station = 0; station < bays; station++)
                    members.Add(new("beam", P(xs[row], equipmentLengthMm * station / bays), P(xs[row], equipmentLengthMm * (station + 1) / bays), z, z));
        }
        // Equal divisions within each column bay ensure both frame beams and secondary beams are present.
        var subdivisions = (int)Math.Ceiling(equipmentLengthMm / bays / maxSecondarySpacingMm);
        if (subdivisions * bays > 80) throw new InvalidOperationException("次梁超过80排，请调整间距或缩小范围。");
        for (var s = 0; s <= bays * subdivisions; s++)
        {
            var y = equipmentLengthMm * s / (bays * subdivisions);
            for (var strip = 0; strip < 3; strip++)
            {
                var z = (strip == 1 ? equipmentTopMm : sideTopMm) - deckThicknessMm;
                members.Add(new("beam", P(xs[strip], y), P(xs[strip + 1], y), z, z));
            }
        }
        var decks = Enumerable.Range(0, 3).Select(strip => new PlatformDeck(
            new[] { P(xs[strip], 0), P(xs[strip + 1], 0), P(xs[strip + 1], equipmentLengthMm), P(xs[strip], equipmentLengthMm) },
            strip == 1 ? equipmentTopMm : sideTopMm)).ToList();
        if (members.Count > 320) throw new InvalidOperationException("单个平台构件数量超过320上限。");
        return new(bays, 4 * (bays + 1), equipmentLengthMm / bays,
            Math.Max(equipmentLengthMm / bays, Math.Max(equipmentWidthMm, sideWidthMm)), width, equipmentLengthMm, members, decks);
    }
}
