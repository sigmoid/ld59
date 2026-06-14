using Microsoft.Xna.Framework;

namespace ld59.UI.Powergrid;

public static class Geometry
{
    /// <summary>
    /// Returns true if segment p1->p2 intersects segment p3->p4.
    /// Ported from the original jam game (LevelManager.DoLinesIntersect),
    /// which adapted https://forum.unity.com/threads/line-intersection.17384/
    /// </summary>
    public static bool DoLinesIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        Vector2 a = p2 - p1;
        Vector2 b = p3 - p4;
        Vector2 c = p1 - p3;

        float alphaNumerator   = b.Y * c.X - b.X * c.Y;
        float alphaDenominator = a.Y * b.X - a.X * b.Y;
        float betaNumerator    = a.X * c.Y - a.Y * c.X;
        float betaDenominator  = a.Y * b.X - a.X * b.Y;

        bool doIntersect = true;

        if (alphaDenominator == 0 || betaDenominator == 0)
        {
            doIntersect = false;
        }
        else
        {
            if (alphaDenominator > 0)
            {
                if (alphaNumerator < 0 || alphaNumerator > alphaDenominator)
                    doIntersect = false;
            }
            else if (alphaNumerator > 0 || alphaNumerator < alphaDenominator)
            {
                doIntersect = false;
            }

            if (doIntersect && betaDenominator > 0)
            {
                if (betaNumerator < 0 || betaNumerator > betaDenominator)
                    doIntersect = false;
            }
            else if (betaNumerator > 0 || betaNumerator < betaDenominator)
            {
                doIntersect = false;
            }
        }

        return doIntersect;
    }
}
