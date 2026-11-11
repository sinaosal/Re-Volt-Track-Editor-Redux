// IconRasterizer.cs - procedural, asset-free icon set for the Studio UI.
//
// DESIGN GRID
//   Every icon is authored on a 24 x 24 unit grid with the origin at the top-left and y growing
//   DOWN (like SVG); content stays inside roughly 2..22. Strokes are 1.75 units wide with round
//   caps and round joins. Angles passed to arc helpers use the maths convention (0 deg = right,
//   90 deg = up, positive = counter-clockwise on screen). At render time the grid is scaled
//   uniformly so 24 units span the requested pixel size.
//
// COVERAGE
//   Every primitive is a signed distance function (SDF) evaluated at pixel centres, in output
//   pixel units: negative inside, positive outside. Primitives are combined in draw order:
//     add  -> union        d = min(d, d_i)
//     cut  -> subtraction  d = max(d, -d_i)   (cut shapes may be inflated to leave a gap)
//   The final alpha is the box-filter approximation coverage = clamp(0.5 - d, 0, 1), which gives
//   one-pixel-wide anti-aliased edges at any scale without supersampling. Because min/max commute
//   with that clamp, any primitive farther than half a pixel away can be skipped, which lets the
//   rasteriser cull by bounding box.
//
// HINTING (grid fitting)
//   To keep 16-24 px renders crisp, key coordinates are snapped to the pixel grid: stroke centre
//   lines land on pixel centres when the stroke rounds to an odd number of pixels and on pixel
//   edges when it rounds to an even number, so horizontal/vertical strokes cover whole pixels.
//   Circle and arc radii are rounded to whole pixels for the same reason, and filled rectangles
//   snap to pixel edges. Snapping is symmetric about the icon centre so symmetric icons stay
//   symmetric. When the canvas centre is not on that grid (e.g. 1 px strokes on a 16 px canvas)
//   the whole icon is shifted half a pixel up/left. Diagonal or curvy artwork (star, bolt,
//   eraser...) turns snapping off and is rendered at exact positions.
//
// The output is RGBA32, rows bottom-to-top (Unity Texture2D convention), pure white RGB with the
// coverage in straight alpha. No UnityEngine dependency, so it can be previewed or unit tested in
// plain .NET.

using System;
using System.Collections.Generic;

namespace Studio.Icons
{
    /// <summary>Identifiers of all procedurally drawn Studio icons.</summary>
    public enum IconId
    {
        New,
        Open,
        Save,
        SaveAs,
        Export,
        Undo,
        Redo,
        ToolPlace,
        ToolPickup,
        ToolErase,
        ToolEyedropper,
        RotateCW,
        RotateCCW,
        ElevationUp,
        ElevationDown,
        VariantPrev,
        VariantNext,
        Surface,
        OrbitLeft,
        OrbitRight,
        ViewTop,
        ViewPerspective,
        Frame,
        Grid,
        Settings,
        Help,
        Info,
        Warning,
        Error,
        Success,
        Search,
        Close,
        Check,
        ChevronDown,
        ChevronRight,
        ChevronUp,
        ChevronLeft,
        Plus,
        Minus,
        Trash,
        Keyboard,
        History,
        Track,
        Module,
        Resize,
        Move,
        Recent,
        Folder,
        File,
        SoundOn,
        SoundOff,
        Eye,
        Layout,
        Recover,
        Gamepad,
        Mouse,
        Pin,
        Dot,
        Lightning,
        Ruler,
        Copy,
        Paste,
        Star,
        StarFilled,
        Lock,
        LockOpen,
        EyeOff,
        List,
        Link,
        Cursor,
        Command,
        Map,
    }

    /// <summary>Rasterises <see cref="IconId"/> icons into white-on-transparent RGBA32 buffers.</summary>
    public static class IconRasterizer
    {
        const float GridSize = 24f;
        const float GridCenter = 12f;
        const float StrokeWidth = 1.75f;
        const float Deg2Rad = (float)(Math.PI / 180.0);

        /// RGBA32 buffer, length size*size*4, rows ordered BOTTOM-TO-TOP (Unity Texture2D convention:
        /// row 0 is the bottom of the image). Color is pure white (255,255,255) everywhere;
        /// coverage goes in alpha. Straight (non-premultiplied) alpha.
        public static byte[] Render(IconId id, int sizePx)
        {
            if (sizePx < 1)
                throw new ArgumentOutOfRangeException(nameof(sizePx), sizePx, "Icon size must be at least 1 pixel.");

            var c = new Canvas(sizePx);
            Draw(id, c);
            return c.Rasterize();
        }

        // ------------------------------------------------------------------------------------
        // Icon artwork (24-unit design grid, y down)
        // ------------------------------------------------------------------------------------

        static void Draw(IconId id, Canvas c)
        {
            switch (id)
            {
                // ---- File ----------------------------------------------------------------
                case IconId.New:
                    Document(c);
                    c.Cross(12, 14.5f, 3);
                    break;

                case IconId.File:
                    Document(c);
                    c.Line(8.5f, 12, 15.5f, 12);
                    c.Line(8.5f, 16, 15.5f, 16);
                    break;

                case IconId.Folder:
                    c.Closed(1.5f, 3, 5, 9, 5, 11, 7.5f, 21, 7.5f, 21, 19, 3, 19);
                    break;

                case IconId.Open:
                    // back panel with tab, then the tilted front flap
                    c.Poly(1.5f, 3, 19, 3, 5, 8.5f, 5, 10.5f, 7, 17.5f, 7, 17.5f, 10.5f);
                    c.Closed(1.25f, 3, 19, 6.5f, 10.5f, 21.5f, 10.5f, 18, 19);
                    break;

                case IconId.Save:
                    Tray(c);
                    c.Arrow(12, 3.5f, 12, 14.5f, 4.5f);
                    break;

                case IconId.SaveAs:
                    Tray(c);
                    c.Arrow(10, 3.5f, 10, 14.5f, 4.5f);
                    c.Cross(18.25f, 6.5f, 2.75f);
                    break;

                case IconId.Export:
                    c.Poly(2, 10.5f, 4, 4, 4, 4, 20, 20, 20, 20, 13.5f);
                    c.Arrow(11.5f, 12.5f, 20, 4, 5.5f);
                    break;

                case IconId.Recover:
                    // life ring: outer and inner rim joined by four diagonal bands. (A page with a
                    // circular arrow was tried too but turns to mush at 16 px.)
                    c.Ring(12, 12, 9);
                    c.Ring(12, 12, 4);
                    c.Snap = false; // diagonal spokes: keep exact so they stay symmetric
                    for (int i = 0; i < 4; i++)
                    {
                        V2 d = V2.FromAngle(45 + 90 * i);
                        c.Line(12 + d.X * 4.5f, 12 + d.Y * 4.5f, 12 + d.X * 8.5f, 12 + d.Y * 8.5f);
                    }
                    c.Snap = true;
                    break;

                // ---- Edit ----------------------------------------------------------------
                case IconId.Undo:
                    c.Poly(5, 4.5f, 9, 20, 9, 20, 19, 9, 19);
                    c.Chevron(4.5f, 9, -1, 0, 5);
                    break;

                case IconId.Redo:
                    c.Mirror = true;
                    goto case IconId.Undo;

                // ---- Tools ---------------------------------------------------------------
                case IconId.ToolPlace:
                    c.Closed(0.75f, 12, 13, 20.5f, 17, 12, 21, 3.5f, 17);
                    c.Arrow(12, 2.5f, 12, 9, 4);
                    break;

                case IconId.ToolPickup:
                    Star(c, 12, 12.9f, 9.6f, 4.3f);
                    break;

                case IconId.ToolErase:
                    {
                        c.Snap = false;
                        // eraser block tilted 45 degrees, with the rubber tip separated by a band
                        const float ang = 45f;
                        c.OrientedRect(13, 10.5f, ang, 8, 4, 1.5f);
                        V2 u = V2.FromAngle(ang), v = new V2(-u.Y, u.X);
                        V2 band = new V2(13, 10.5f) - u * 2.5f;
                        V2 a = band + v * 4, b = band - v * 4;
                        c.Line(a.X, a.Y, b.X, b.Y);
                        c.Snap = true;
                        c.Line(11, 21, 20, 21);
                        break;
                    }

                case IconId.ToolEyedropper:
                    Eyedropper(c);
                    break;

                case IconId.RotateCW:
                    c.ArcArrow(12, 12, 8, 30, -280, 4);
                    break;

                case IconId.RotateCCW:
                    c.Mirror = true;
                    goto case IconId.RotateCW;

                case IconId.ElevationUp:
                    c.Line(5, 20.5f, 19, 20.5f);
                    c.Arrow(12, 16.5f, 12, 3.5f, 5.5f);
                    break;

                case IconId.ElevationDown:
                    c.Line(5, 20.5f, 19, 20.5f);
                    c.Arrow(12, 3.5f, 12, 16, 5.5f);
                    break;

                case IconId.VariantPrev:
                    c.Line(6.5f, 6.5f, 6.5f, 17.5f);
                    c.Poly(0, 16.5f, 6, 10.5f, 12, 16.5f, 18);
                    break;

                case IconId.VariantNext:
                    c.Mirror = true;
                    goto case IconId.VariantPrev;

                case IconId.Surface:
                    c.Closed(0.75f, 12, 4.5f, 21, 9, 12, 13.5f, 3, 9);
                    c.Poly(0.75f, 3, 14, 12, 18.5f, 21, 14);
                    break;

                // ---- View ----------------------------------------------------------------
                case IconId.OrbitLeft:
                    // horizontal orbit seen slightly from above; the head sits at the front so it
                    // points clearly sideways
                    c.Dot(12, 11.5f, 2);
                    c.EllipseArrow(12, 12, 9.5f, 6, 175, -295, 4);
                    break;

                case IconId.OrbitRight:
                    c.Mirror = true;
                    goto case IconId.OrbitLeft;

                case IconId.ViewTop:
                    c.Rect(4, 4, 20, 20, 2.5f);
                    c.Dot(12, 12, 2.25f);
                    break;

                case IconId.ViewPerspective:
                    {
                        // isometric cube: hexagon silhouette plus the three inner edges
                        const float w = 8.5f, top = 2.5f, h = 9.5f;
                        float dy = w * 0.57735f; // tan(30 deg)
                        c.Closed(0.75f, 12, top, 12 + w, top + dy, 12 + w, top + dy + h, 12, top + 2 * dy + h, 12 - w, top + dy + h, 12 - w, top + dy);
                        c.Poly(0, 12 - w, top + dy, 12, top + 2 * dy, 12 + w, top + dy);
                        c.Line(12, top + 2 * dy, 12, top + 2 * dy + h);
                        break;
                    }

                case IconId.Frame:
                    c.Poly(1.5f, 3.5f, 9, 3.5f, 3.5f, 9, 3.5f);
                    c.Poly(1.5f, 15, 3.5f, 20.5f, 3.5f, 20.5f, 9);
                    c.Poly(1.5f, 20.5f, 15, 20.5f, 20.5f, 15, 20.5f);
                    c.Poly(1.5f, 9, 20.5f, 3.5f, 20.5f, 3.5f, 15);
                    break;

                case IconId.Grid:
                    c.Rect(3.5f, 3.5f, 20.5f, 20.5f, 2);
                    c.Line(9.17f, 3.5f, 9.17f, 20.5f);
                    c.Line(14.83f, 3.5f, 14.83f, 20.5f);
                    c.Line(3.5f, 9.17f, 20.5f, 9.17f);
                    c.Line(3.5f, 14.83f, 20.5f, 14.83f);
                    break;

                case IconId.Layout:
                    c.Rect(3, 4, 21, 20, 2);
                    c.Line(9.5f, 4, 9.5f, 20);
                    break;

                case IconId.Eye:
                    c.ArcThrough(2.5f, 12, 12, 5, 21.5f, 12);
                    c.ArcThrough(2.5f, 12, 12, 19, 21.5f, 12);
                    c.Ring(12, 12, 3);
                    break;

                // ---- Status --------------------------------------------------------------
                case IconId.Settings:
                    Gear(c);
                    break;

                case IconId.Help:
                    {
                        c.Ring(12, 12, 9);
                        V2 end = c.ArcPx(12, 9.75f, 2.75f, 165, -225);
                        V2 stem = c.P(12, 13.75f);
                        c.SegmentPx(end, stem);
                        c.Line(12, 13.75f, 12, 14.25f);
                        c.Dot(12, 17.25f, 1.15f);
                        break;
                    }

                case IconId.Info:
                    c.Ring(12, 12, 9);
                    c.Dot(12, 7.75f, 1.15f);
                    c.Line(12, 11, 12, 16.5f);
                    break;

                case IconId.Warning:
                    c.Closed(2, 12, 3.5f, 21.5f, 20, 2.5f, 20);
                    c.Line(12, 9.5f, 12, 13.5f);
                    c.Dot(12, 16.75f, 1.15f);
                    break;

                case IconId.Error:
                    c.Ring(12, 12, 9);
                    c.Line(9, 9, 15, 15);
                    c.Line(15, 9, 9, 15);
                    break;

                case IconId.Success:
                    c.Ring(12, 12, 9);
                    c.Poly(0, 8, 12, 11, 15, 16.5f, 9.5f);
                    break;

                case IconId.Dot:
                    c.Dot(12, 12, 4);
                    break;

                // ---- Generic UI ----------------------------------------------------------
                case IconId.Search:
                    c.Ring(10.5f, 10.5f, 6.5f);
                    c.Line(15.5f, 15.5f, 20.5f, 20.5f);
                    break;

                case IconId.Close:
                    c.Line(6, 6, 18, 18);
                    c.Line(18, 6, 6, 18);
                    break;

                case IconId.Check:
                    c.Poly(0, 4.5f, 12, 9.5f, 17, 19.5f, 7);
                    break;

                case IconId.ChevronDown:
                    c.Poly(0, 6, 9, 12, 15, 18, 9);
                    break;

                case IconId.ChevronUp:
                    c.Poly(0, 6, 15, 12, 9, 18, 15);
                    break;

                case IconId.ChevronRight:
                    c.Poly(0, 9, 6, 15, 12, 9, 18);
                    break;

                case IconId.ChevronLeft:
                    c.Poly(0, 15, 6, 9, 12, 15, 18);
                    break;

                case IconId.Plus:
                    c.Line(5, 12, 19, 12);
                    c.Line(12, 5, 12, 19);
                    break;

                case IconId.Minus:
                    c.Line(5, 12, 19, 12);
                    break;

                case IconId.Trash:
                    c.Line(3.5f, 6.5f, 20.5f, 6.5f);
                    c.Poly(1, 9, 6.5f, 9, 3.5f, 15, 3.5f, 15, 6.5f);
                    c.Poly(1.75f, 5.5f, 6.5f, 6.5f, 20.5f, 17.5f, 20.5f, 18.5f, 6.5f);
                    if (!c.Small)
                    {
                        c.Line(10, 10.5f, 10, 16.5f);
                        c.Line(14, 10.5f, 14, 16.5f);
                    }
                    break;

                case IconId.Keyboard:
                    c.Rect(2.5f, 6, 21.5f, 18, 2);
                    for (int i = -2; i <= 2; i++)
                    {
                        c.Dot(12 + 3 * i, 9.5f, 1);
                        c.Dot(12 + 3 * i, 12.5f, 1);
                    }
                    c.Line(8, 15.25f, 16, 15.25f);
                    break;

                case IconId.History:
                    c.ArcArrow(12, 12, 8.5f, 210, 300, 4);
                    c.Poly(0, 12, 7.5f, 12, 12, 15, 14);
                    break;

                case IconId.Recent:
                    c.Ring(12, 12, 9);
                    c.Poly(0, 12, 7, 12, 12, 15.5f, 14);
                    break;

                case IconId.Track:
                    {
                        // checkered flag on a pole
                        c.Line(5, 3.5f, 5, 21);
                        c.Poly(0.75f, 5, 4, 19, 4, 19, 13, 5, 13);
                        float x0 = 5, x1 = 9.67f, x2 = 14.33f, x3 = 19, y0 = 4, y1 = 8.5f, y2 = 13;
                        c.FillRect(x0, y0, x1, y1);
                        c.FillRect(x2, y0, x3, y1);
                        c.FillRect(x1, y1, x2, y2);
                        break;
                    }

                case IconId.Module:
                    c.Rect(3.5f, 3.5f, 10.5f, 10.5f, 1.5f);
                    c.Rect(13.5f, 3.5f, 20.5f, 10.5f, 1.5f);
                    c.Rect(3.5f, 13.5f, 10.5f, 20.5f, 1.5f);
                    c.Rect(13.5f, 13.5f, 20.5f, 20.5f, 1.5f);
                    break;

                case IconId.Resize:
                    c.Line(5, 19, 19, 5);
                    c.Chevron(19, 5, 1, -1, 6);
                    c.Chevron(5, 19, -1, 1, 6);
                    break;

                case IconId.Move:
                    c.Line(12, 2.5f, 12, 21.5f);
                    c.Line(2.5f, 12, 21.5f, 12);
                    c.Chevron(12, 2.5f, 0, -1, 4);
                    c.Chevron(12, 21.5f, 0, 1, 4);
                    c.Chevron(2.5f, 12, -1, 0, 4);
                    c.Chevron(21.5f, 12, 1, 0, 4);
                    break;

                // ---- Audio / devices -----------------------------------------------------
                case IconId.SoundOn:
                    Speaker(c);
                    c.Arc(12.5f, 12, 4, -45, 90);
                    c.Arc(12.5f, 12, 7.5f, -50, 100);
                    break;

                case IconId.SoundOff:
                    Speaker(c);
                    c.Line(15.25f, 9, 21.25f, 15);
                    c.Line(21.25f, 9, 15.25f, 15);
                    break;

                case IconId.Gamepad:
                    c.Closed(2.5f, 7, 6.5f, 17, 6.5f, 21.5f, 16, 18.5f, 18.5f, 15, 15, 9, 15, 5.5f, 18.5f, 2.5f, 16);
                    c.Cross(7.5f, 10.5f, 1.75f);
                    c.Dot(15.5f, 11.75f, 1);
                    c.Dot(17.75f, 9.5f, 1);
                    break;

                case IconId.Mouse:
                    c.Rect(6, 3, 18, 21, 6);
                    c.Line(12, 7, 12, 10.5f);
                    break;

                case IconId.Pin:
                    c.Line(8, 3.5f, 16, 3.5f);
                    c.Poly(0.5f, 10, 3.5f, 10, 9.5f, 6, 14.5f, 18, 14.5f, 14, 9.5f, 14, 3.5f);
                    c.Line(12, 14.5f, 12, 21);
                    break;

                case IconId.Lightning:
                    c.Snap = false;
                    c.Closed(0.75f, 13.5f, 2.5f, 4.5f, 13.5f, 11.5f, 13.5f, 10.5f, 21.5f, 19.5f, 10.5f, 12.5f, 10.5f);
                    break;

                case IconId.Ruler:
                    c.Rect(2.5f, 7.5f, 21.5f, 16.5f, 1.5f);
                    for (int i = -2; i <= 2; i++)
                        c.Line(12 + 3 * i, 7.5f, 12 + 3 * i, (i & 1) == 0 ? 11.5f : 10);
                    break;

                case IconId.Copy:
                    c.Rect(8.5f, 8.5f, 20.5f, 20.5f, 2);
                    c.Poly(2, 4, 15.5f, 4, 4, 15.5f, 4);
                    break;

                case IconId.Paste:
                    c.Rect(4.5f, 5, 19.5f, 21, 2);
                    c.Rect(8.5f, 2.5f, 15.5f, 7, 1);
                    c.Line(8.5f, 12, 15.5f, 12);
                    c.Line(8.5f, 16, 13.5f, 16);
                    break;

                case IconId.Star:
                    c.Closed(0.75f, 12.00f, 3.80f, 14.29f, 9.64f, 20.56f, 10.02f, 15.71f, 14.01f, 17.29f, 20.08f, 12.00f, 16.70f, 6.71f, 20.08f, 8.29f, 14.01f, 3.44f, 10.02f, 9.71f, 9.64f);
                    break;

                case IconId.StarFilled:
                    c.FillPoly(12.00f, 3.80f, 14.29f, 9.64f, 20.56f, 10.02f, 15.71f, 14.01f, 17.29f, 20.08f, 12.00f, 16.70f, 6.71f, 20.08f, 8.29f, 14.01f, 3.44f, 10.02f, 9.71f, 9.64f);
                    c.Closed(0.75f, 12.00f, 3.80f, 14.29f, 9.64f, 20.56f, 10.02f, 15.71f, 14.01f, 17.29f, 20.08f, 12.00f, 16.70f, 6.71f, 20.08f, 8.29f, 14.01f, 3.44f, 10.02f, 9.71f, 9.64f);
                    break;

                case IconId.Lock:
                    c.Rect(5, 11, 19, 21, 2);
                    c.Line(8, 11, 8, 7.5f);
                    c.Line(16, 11, 16, 7.5f);
                    c.ArcThrough(8, 7.5f, 12, 3.5f, 16, 7.5f);
                    c.Dot(12, 16, 1.25f);
                    break;

                case IconId.LockOpen:
                    c.Rect(5, 11, 19, 21, 2);
                    c.Line(8, 11, 8, 7.5f);
                    c.ArcThrough(8, 7.5f, 12, 3.5f, 16, 7.5f);
                    c.Dot(12, 16, 1.25f);
                    break;

                case IconId.EyeOff:
                    c.ArcThrough(2.5f, 12, 12, 5, 21.5f, 12);
                    c.ArcThrough(2.5f, 12, 12, 19, 21.5f, 12);
                    c.Ring(12, 12, 3);
                    c.Line(4, 20, 20, 4);
                    break;

                case IconId.List:
                    c.Dot(5, 7, 1.25f);
                    c.Line(9, 7, 20, 7);
                    c.Dot(5, 12, 1.25f);
                    c.Line(9, 12, 20, 12);
                    c.Dot(5, 17, 1.25f);
                    c.Line(9, 17, 20, 17);
                    break;

                case IconId.Link:
                    c.Ring(8.5f, 12, 5);
                    c.Ring(15.5f, 12, 5);
                    break;

                case IconId.Cursor:
                    c.Closed(1, 6, 3, 6, 18.5f, 10, 14.5f, 13, 21, 15.5f, 20, 12.5f, 13.5f, 18, 13.5f);
                    break;

                case IconId.Command:
                    c.Poly(1, 5, 7, 10, 12, 5, 17);
                    c.Line(12.5f, 17, 19, 17);
                    break;

                case IconId.Map:
                    c.Closed(1, 3, 6, 9, 4, 15, 6, 21, 4, 21, 18, 15, 20, 9, 18, 3, 20);
                    c.Line(9, 4, 9, 18);
                    c.Line(15, 6, 15, 20);
                    break;

                default:
                    // Unknown id: draw a plain placeholder square so the gap is visible.
                    c.Rect(5, 5, 19, 19, 1);
                    break;
            }
        }

        static void Document(Canvas c)
        {
            // page with a folded top-right corner
            c.PolyR(true, new[] { 1.5f, 0f, 0f, 1.5f, 1.5f }, 5, 3, 14.5f, 3, 19, 7.5f, 19, 21, 5, 21);
            c.Poly(0, 14.5f, 3, 14.5f, 7.5f, 19, 7.5f);
        }

        static void Tray(Canvas c)
        {
            c.Poly(2, 4, 14, 4, 20, 20, 20, 20, 14);
        }

        static void Speaker(Canvas c)
        {
            c.Closed(1, 3.5f, 9, 7.5f, 9, 12.5f, 4.5f, 12.5f, 19.5f, 7.5f, 15, 3.5f, 15);
        }

        static void Star(Canvas c, float cx, float cy, float rOuter, float rInner)
        {
            bool snap = c.Snap;
            c.Snap = false;
            var pts = new float[20];
            for (int i = 0; i < 10; i++)
            {
                float r = (i & 1) == 0 ? rOuter : rInner;
                float a = (90 + 36 * i) * Deg2Rad;
                pts[2 * i] = cx + r * (float)Math.Cos(a);
                pts[2 * i + 1] = cy - r * (float)Math.Sin(a);
            }
            c.Closed(0.75f, pts);
            c.Snap = snap;
        }

        static void Gear(Canvas c)
        {
            // 8 teeth; polygon outline through root and tip points, plus a hub ring.
            const int teeth = 8;
            const float rTip = 9.75f, rRoot = 7.25f;
            const float tipHalf = 9f, rootHalf = 15f; // half-widths of a tooth in degrees
            bool snap = c.Snap;
            c.Snap = false;
            var pts = new float[teeth * 4 * 2];
            int k = 0;
            for (int i = 0; i < teeth; i++)
            {
                float a = 90 + i * 360f / teeth;
                AddPolar(pts, ref k, 12, 12, rRoot, a - rootHalf);
                AddPolar(pts, ref k, 12, 12, rTip, a - tipHalf);
                AddPolar(pts, ref k, 12, 12, rTip, a + tipHalf);
                AddPolar(pts, ref k, 12, 12, rRoot, a + rootHalf);
            }
            c.Closed(0.6f, pts);
            c.Snap = snap;
            c.Ring(12, 12, 3);
        }

        static void AddPolar(float[] pts, ref int k, float cx, float cy, float r, float deg)
        {
            float a = deg * Deg2Rad;
            pts[k++] = cx + r * (float)Math.Cos(a);
            pts[k++] = cy - r * (float)Math.Sin(a);
        }

        static void Eyedropper(Canvas c)
        {
            // Built along a 45 degree axis running from the tip (bottom-left) to the bulb (top-right).
            // Local frame: s runs along the axis from the tip, t across it.
            c.Snap = false;
            const float ang = 45f;
            V2 u = V2.FromAngle(ang), v = new V2(-u.Y, u.X);
            V2 tip = new V2(4.25f, 19.75f);

            // nib + tube
            V2 n1 = tip + u * 3f;
            c.Line(tip.X, tip.Y, n1.X, n1.Y);
            V2 a = tip + u * 3f + v * 1.9f, b = tip + u * 12f + v * 1.9f;
            V2 d = tip + u * 3f - v * 1.9f, e = tip + u * 12f - v * 1.9f;
            c.Closed(0.5f, d.X, d.Y, a.X, a.Y, b.X, b.Y, e.X, e.Y);

            // collar
            V2 ca = tip + u * 12f + v * 3.75f, cb = tip + u * 12f - v * 3.75f;
            c.Line(ca.X, ca.Y, cb.X, cb.Y);

            // squeeze bulb
            V2 bc = tip + u * 15.5f;
            c.OrientedRect(bc.X, bc.Y, ang, 3.5f, 2.75f, 2.75f);
            c.Snap = true;
        }

        // ------------------------------------------------------------------------------------
        // Canvas: converts design-unit primitives into pixel-space SDF shapes and rasterises.
        // ------------------------------------------------------------------------------------

        sealed class Canvas
        {
            public readonly int Size;
            public readonly bool Small;        // below 20 px: icons may drop fine interior detail

            /// Grid-fit subsequent primitives (see HINTING in the header).
            public bool Snap = true;

            /// Mirror subsequent primitives horizontally around the icon centre (x -> 24 - x).
            public bool Mirror;

            readonly float scale;              // pixels per design unit
            readonly float halfStroke;         // stroke half-width in pixels
            readonly float origin;             // pixel position of the design centre (12,12); on the stroke grid
            readonly bool halfGrid;            // stroke grid = pixel centres (true) or pixel edges (false)
            readonly List<Shape> shapes = new List<Shape>();

            bool cutting;
            float cutGrow;

            public Canvas(int size)
            {
                Size = size;
                Small = size < 20;
                scale = size / GridSize;
                halfStroke = StrokeWidth * scale * 0.5f;

                int strokePx = Math.Max(1, (int)Math.Round(StrokeWidth * scale));
                halfGrid = (strokePx & 1) == 1;
                float centre = size * 0.5f;
                bool centreOnHalf = (size & 1) == 1;
                origin = centreOnHalf == halfGrid ? centre : centre - 0.5f;
            }

            // ---- coordinate mapping ---------------------------------------------------------

            /// Round half away from zero (symmetric about 0, so mirrored features round alike).
            static float RoundSym(float v)
            {
                return v >= 0 ? (float)Math.Floor(v + 0.5f) : -(float)Math.Floor(-v + 0.5f);
            }

            /// Nearest value of the form n + 0.5, symmetric about 0.
            static float RoundHalfSym(float v)
            {
                return v >= 0 ? (float)Math.Floor(v) + 0.5f : -((float)Math.Floor(-v) + 0.5f);
            }

            float MirrorX(float x) { return Mirror ? GridSize - x : x; }

            float MapX(float x)
            {
                float rel = (MirrorX(x) - GridCenter) * scale;
                return origin + (Snap ? RoundSym(rel) : rel);
            }

            float MapY(float y)
            {
                float rel = (y - GridCenter) * scale;
                return origin + (Snap ? RoundSym(rel) : rel);
            }

            /// Design point -> pixel point (stroke grid when snapping).
            public V2 P(float x, float y) { return new V2(MapX(x), MapY(y)); }

            /// Design coordinate -> pixel edge (used by filled rectangles).
            float EdgeX(float x) { return EdgeRel((MirrorX(x) - GridCenter) * scale); }
            float EdgeY(float y) { return EdgeRel((y - GridCenter) * scale); }

            float EdgeRel(float rel)
            {
                if (!Snap) return origin + rel;
                // origin sits on a pixel centre when halfGrid, so edges are at origin + n + 0.5
                return origin + (halfGrid ? RoundHalfSym(rel) : RoundSym(rel));
            }

            /// Design radius -> pixel radius (whole pixels when snapping so circle extremes hit the grid).
            float Radius(float r)
            {
                float px = r * scale;
                return Snap ? Math.Max(1f, (float)Math.Floor(px + 0.5f)) : px;
            }

            float MirrorAngle(float deg) { return Mirror ? 180f - deg : deg; }
            float MirrorSweep(float sweep) { return Mirror ? -sweep : sweep; }

            // ---- draw modes ------------------------------------------------------------------

            /// Subsequent primitives subtract from what is already drawn, inflated by gapUnits.
            public void BeginCut(float gapUnits) { cutting = true; cutGrow = gapUnits * scale; }
            public void EndCut() { cutting = false; cutGrow = 0; }

            void Add(Shape s)
            {
                s.Cut = cutting;
                s.Grow = cutting ? cutGrow : 0f;
                shapes.Add(s);
            }

            // ---- primitives (design units) ------------------------------------------------------

            public void Line(float x0, float y0, float x1, float y1)
            {
                SegmentPx(P(x0, y0), P(x1, y1));
            }

            /// Open polyline; interior corners are filleted with the given radius (design units).
            public void Poly(float radius, params float[] xy) { Path(false, null, radius, xy); }

            /// Closed polygon outline with filleted corners.
            public void Closed(float radius, params float[] xy) { Path(true, null, radius, xy); }

            /// Polyline/polygon with a per-vertex fillet radius.
            public void PolyR(bool closed, float[] radii, params float[] xy) { Path(closed, radii, 0, xy); }

            public void Ring(float cx, float cy, float r)
            {
                Add(new RingShape(P(cx, cy), Radius(r), halfStroke));
            }

            /// Filled disc (centre snapped, radius exact).
            public void Dot(float cx, float cy, float r)
            {
                Add(new DiscShape(P(cx, cy), r * scale));
            }

            /// Circular arc from startDeg sweeping sweepDeg (positive = counter-clockwise).
            public void Arc(float cx, float cy, float r, float startDeg, float sweepDeg)
            {
                ArcPx(cx, cy, r, startDeg, sweepDeg);
            }

            /// Same as Arc but returns the pixel-space end point (for attaching further strokes).
            public V2 ArcPx(float cx, float cy, float r, float startDeg, float sweepDeg)
            {
                V2 c = P(cx, cy);
                float rp = Radius(r);
                startDeg = MirrorAngle(startDeg);
                sweepDeg = MirrorSweep(sweepDeg);
                AddArc(c, rp, startDeg, sweepDeg);
                return c + V2.FromAngle(startDeg + sweepDeg) * rp;
            }

            /// Arc with an open arrowhead at its end, pointing along the direction of travel.
            public void ArcArrow(float cx, float cy, float r, float startDeg, float sweepDeg, float wing)
            {
                V2 c = P(cx, cy);
                float rp = Radius(r);
                startDeg = MirrorAngle(startDeg);
                sweepDeg = MirrorSweep(sweepDeg);
                AddArc(c, rp, startDeg, sweepDeg);

                float end = startDeg + sweepDeg;
                V2 radial = V2.FromAngle(end);
                V2 tip = c + radial * rp;
                // Counter-clockwise tangent of FromAngle(a) is FromAngle(a + 90).
                V2 travel = V2.FromAngle(end + (sweepDeg >= 0 ? 90f : -90f));
                // Tilt the head inwards by half the angle its wings subtend, so it sits on the
                // curve instead of on the tangent line.
                float wingPx = wing * scale;
                float tilt = (float)Math.Atan2(wingPx * 0.7071f * 0.5f, rp);
                travel = (travel * (float)Math.Cos(tilt) - radial * (float)Math.Sin(tilt)).Normalized;
                ChevronPx(tip, travel, wingPx, false);
            }

            /// Elliptical arc (approximated by a fine polyline) with an arrowhead at its end.
            public void EllipseArrow(float cx, float cy, float rx, float ry, float startDeg, float sweepDeg, float wing)
            {
                V2 c = P(cx, cy);
                float rxp = Radius(rx), ryp = Radius(ry);
                startDeg = MirrorAngle(startDeg);
                sweepDeg = MirrorSweep(sweepDeg);
                int n = Math.Max(8, (int)Math.Ceiling(Math.Abs(sweepDeg) / 5f));
                var pts = new V2[n + 1];
                for (int i = 0; i <= n; i++)
                {
                    float a = (startDeg + sweepDeg * i / n) * Deg2Rad;
                    pts[i] = c + new V2(rxp * (float)Math.Cos(a), -ryp * (float)Math.Sin(a));
                }
                PathPx(pts, false, null, 0);

                float e = (startDeg + sweepDeg) * Deg2Rad;
                float sign = sweepDeg >= 0 ? 1f : -1f;
                // d/da (rx cos a, -ry sin a) = (-rx sin a, -ry cos a)
                V2 travel = new V2(-rxp * (float)Math.Sin(e), -ryp * (float)Math.Cos(e)) * sign;
                ChevronPx(pts[n], travel.Normalized, wing * scale, false);
            }

            /// Arc through three design points (start, a point on the arc, end).
            public void ArcThrough(float x0, float y0, float xm, float ym, float x1, float y1)
            {
                V2 a = P(x0, y0), m = P(xm, ym), b = P(x1, y1);
                // circumcentre
                float d = 2 * (a.X * (m.Y - b.Y) + m.X * (b.Y - a.Y) + b.X * (a.Y - m.Y));
                if (Math.Abs(d) < 1e-6f) { SegmentPx(a, b); return; }
                float a2 = a.Dot(a), m2 = m.Dot(m), b2 = b.Dot(b);
                var cc = new V2(
                    (a2 * (m.Y - b.Y) + m2 * (b.Y - a.Y) + b2 * (a.Y - m.Y)) / d,
                    (a2 * (b.X - m.X) + m2 * (a.X - b.X) + b2 * (m.X - a.X)) / d);
                float r = (a - cc).Length;
                float aa = (a - cc).AngleDeg(), ab = (b - cc).AngleDeg(), am = (m - cc).AngleDeg();
                float sweep = Wrap360(ab - aa);       // counter-clockwise a -> b
                if (Wrap360(am - aa) > sweep) sweep -= 360f; // mid point not on that side: go clockwise
                AddArc(cc, r, aa, sweep);
            }

            static float Wrap360(float deg)
            {
                deg %= 360f;
                return deg < 0 ? deg + 360f : deg;
            }

            /// Open arrowhead whose tip is at (tipX,tipY) pointing along (dirX,dirY).
            public void Chevron(float tipX, float tipY, float dirX, float dirY, float wing)
            {
                if (Mirror) dirX = -dirX;
                ChevronPx(P(tipX, tipY), new V2(dirX, dirY).Normalized, wing * scale, Snap);
            }

            /// Straight arrow from (x0,y0) to the tip at (x1,y1).
            public void Arrow(float x0, float y0, float x1, float y1, float wing)
            {
                Line(x0, y0, x1, y1);
                Chevron(x1, y1, x1 - x0, y1 - y0, wing);
            }

            /// Plus sign centred on (cx,cy). The arm length is rounded once and applied to both
            /// sides, so the sign stays symmetric after snapping.
            public void Cross(float cx, float cy, float arm)
            {
                V2 o = P(cx, cy);
                float a = arm * scale;
                if (Snap) a = Math.Max(1f, RoundSym(a));
                SegmentPx(new V2(o.X - a, o.Y), new V2(o.X + a, o.Y));
                SegmentPx(new V2(o.X, o.Y - a), new V2(o.X, o.Y + a));
            }

            /// Axis-aligned rounded rectangle outline (edges on the stroke grid).
            public void Rect(float x0, float y0, float x1, float y1, float radius)
            {
                float ax = MapX(x0), bx = MapX(x1), ay = MapY(y0), by = MapY(y1);
                AddBox(ax, ay, bx, by, radius * scale, false);
            }

            /// Axis-aligned filled rectangle (edges snapped to pixel edges).
            public void FillRect(float x0, float y0, float x1, float y1)
            {
                AddBox(EdgeX(x0), EdgeY(y0), EdgeX(x1), EdgeY(y1), 0f, true);
            }

            /// Rotated rounded rectangle outline; angle in degrees (maths convention), half extents
            /// along and across the rotated axis.
            public void OrientedRect(float cx, float cy, float angleDeg, float halfLen, float halfWid, float radius)
            {
                V2 c = P(cx, cy);
                V2 axis = V2.FromAngle(MirrorAngle(angleDeg));
                Add(new BoxShape(c, axis, halfLen * scale, halfWid * scale, radius * scale, halfStroke, false));
            }

            /// Filled polygon (any simple polygon).
            public void FillPoly(params float[] xy)
            {
                int n = xy.Length / 2;
                var pts = new V2[n];
                for (int i = 0; i < n; i++) pts[i] = P(xy[2 * i], xy[2 * i + 1]);
                Add(new PolygonShape(pts));
            }

            // ---- primitives (pixel space) ----------------------------------------------------

            public void SegmentPx(V2 a, V2 b)
            {
                Add(new SegmentShape(a, b, halfStroke));
            }

            void AddArc(V2 c, float r, float startDeg, float sweepDeg)
            {
                float half = Math.Min(Math.Abs(sweepDeg), 360f) * 0.5f;
                V2 mid = V2.FromAngle(startDeg + sweepDeg * 0.5f);
                Add(new ArcShape(c, r, mid, half * Deg2Rad, halfStroke));
            }

            void AddBox(float ax, float ay, float bx, float by, float radiusPx, bool fill)
            {
                float minX = Math.Min(ax, bx), maxX = Math.Max(ax, bx);
                float minY = Math.Min(ay, by), maxY = Math.Max(ay, by);
                var c = new V2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
                Add(new BoxShape(c, new V2(1, 0), (maxX - minX) * 0.5f, (maxY - minY) * 0.5f, radiusPx,
                    fill ? 0f : halfStroke, fill));
            }

            void ChevronPx(V2 tip, V2 dir, float wingPx, bool snap)
            {
                V2 back = -dir;
                V2 w1 = back.Rotated(45f) * wingPx;
                V2 w2 = back.Rotated(-45f) * wingPx;
                if (snap)
                {
                    // whole-pixel offsets keep wing ends on the same grid as the (snapped) tip
                    w1 = new V2(RoundSym(w1.X), RoundSym(w1.Y));
                    w2 = new V2(RoundSym(w2.X), RoundSym(w2.Y));
                }
                PathPx(new[] { tip + w1, tip, tip + w2 }, false, null, 0);
            }

            void Path(bool closed, float[] radii, float radius, float[] xy)
            {
                int n = xy.Length / 2;
                var pts = new V2[n];
                for (int i = 0; i < n; i++) pts[i] = P(xy[2 * i], xy[2 * i + 1]);
                float[] rpx = null;
                if (radii != null)
                {
                    rpx = new float[n];
                    for (int i = 0; i < n && i < radii.Length; i++) rpx[i] = radii[i] * scale;
                }
                PathPx(pts, closed, rpx, radius * scale);
            }

            /// Polyline through pixel-space points. Interior corners get a circular fillet of the
            /// given radius: the two legs are trimmed by t = r / tan(theta/2) (theta = angle between
            /// the legs) and joined by an arc whose centre lies on the corner bisector at distance
            /// r / sin(theta/2). The fillet shrinks if a leg is too short. Round caps on every segment
            /// give round joins where no fillet is requested.
            void PathPx(V2[] pts, bool closed, float[] radiiPx, float radiusPx)
            {
                int n = pts.Length;
                if (n == 0) return;
                if (n == 1) { SegmentPx(pts[0], pts[0]); return; }

                var segStart = new V2[n]; // where the segment leaving vertex i starts
                var segEnd = new V2[n];   // where the segment arriving at vertex i ends
                for (int i = 0; i < n; i++) segStart[i] = segEnd[i] = pts[i];

                for (int i = 0; i < n; i++)
                {
                    if (!closed && (i == 0 || i == n - 1)) continue;
                    float rad = radiiPx != null ? radiiPx[i] : radiusPx;
                    if (rad <= 0) continue;

                    V2 cur = pts[i];
                    V2 u = pts[(i + n - 1) % n] - cur, v = pts[(i + 1) % n] - cur;
                    float lu = u.Length, lv = v.Length;
                    if (lu < 1e-4f || lv < 1e-4f) continue;
                    u = u * (1f / lu);
                    v = v * (1f / lv);

                    float theta = (float)Math.Acos(Clamp(u.Dot(v), -1f, 1f));
                    if (theta < 1e-3f || theta > Math.PI - 1e-3f) continue; // spike or straight

                    float tanHalf = (float)Math.Tan(theta * 0.5f);
                    float t = rad / tanHalf;
                    float maxT = Math.Min(lu, lv) * 0.5f;
                    if (t > maxT) { t = maxT; rad = t * tanHalf; }

                    segEnd[i] = cur + u * t;
                    segStart[i] = cur + v * t;
                    V2 bis = (u + v).Normalized;
                    V2 centre = cur + bis * (rad / (float)Math.Sin(theta * 0.5f));
                    // the arc bulges towards the corner, i.e. away from its centre along -bis
                    Add(new ArcShape(centre, rad, -bis, (float)(Math.PI - theta) * 0.5f, halfStroke));
                }

                int count = closed ? n : n - 1;
                for (int i = 0; i < count; i++)
                    SegmentPx(segStart[i], segEnd[(i + 1) % n]);
            }

            // ---- rasterisation ------------------------------------------------------------------

            public byte[] Rasterize()
            {
                int n = Size;
                var buffer = new byte[n * n * 4];
                var rowShapes = new List<Shape>(shapes.Count);

                for (int iy = 0; iy < n; iy++)
                {
                    float y = iy + 0.5f;
                    rowShapes.Clear();
                    for (int k = 0; k < shapes.Count; k++)
                    {
                        Shape s = shapes[k];
                        float m = s.Grow + 0.5f;
                        if (y >= s.MinY - m && y <= s.MaxY + m) rowShapes.Add(s);
                    }

                    // design space is y-down; Texture2D rows are bottom-to-top
                    int rowBase = (n - 1 - iy) * n * 4;
                    for (int ix = 0; ix < n; ix++)
                    {
                        float x = ix + 0.5f;
                        // Running distance, conceptually clamped to [-0.5, 0.5]: 0.5 = empty pixel.
                        float d = 0.5f;
                        for (int k = 0; k < rowShapes.Count; k++)
                        {
                            Shape s = rowShapes[k];
                            float m = s.Grow + 0.5f;
                            if (x < s.MinX - m || x > s.MaxX + m) continue; // >= half a pixel away
                            float di = s.Distance(x, y) - s.Grow;
                            if (s.Cut) { if (-di > d) d = -di; }
                            else if (di < d) d = di;
                        }

                        float coverage = Clamp(0.5f - d, 0f, 1f);
                        int o = rowBase + ix * 4;
                        buffer[o] = 255;
                        buffer[o + 1] = 255;
                        buffer[o + 2] = 255;
                        buffer[o + 3] = (byte)(coverage * 255f + 0.5f);
                    }
                }
                return buffer;
            }
        }

        static float Clamp(float v, float lo, float hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        // ------------------------------------------------------------------------------------
        // Geometry
        // ------------------------------------------------------------------------------------

        struct V2
        {
            public readonly float X, Y;

            public V2(float x, float y) { X = x; Y = y; }

            /// Unit vector for a maths-convention angle, expressed in y-down space.
            public static V2 FromAngle(float deg)
            {
                float a = deg * Deg2Rad;
                return new V2((float)Math.Cos(a), -(float)Math.Sin(a));
            }

            public static V2 operator +(V2 a, V2 b) { return new V2(a.X + b.X, a.Y + b.Y); }
            public static V2 operator -(V2 a, V2 b) { return new V2(a.X - b.X, a.Y - b.Y); }
            public static V2 operator -(V2 a) { return new V2(-a.X, -a.Y); }
            public static V2 operator *(V2 a, float s) { return new V2(a.X * s, a.Y * s); }

            public float Dot(V2 b) { return X * b.X + Y * b.Y; }
            public float Length { get { return (float)Math.Sqrt(X * X + Y * Y); } }

            public V2 Normalized
            {
                get
                {
                    float l = Length;
                    return l > 1e-6f ? new V2(X / l, Y / l) : new V2(0, 0);
                }
            }

            /// Maths-convention angle (degrees) of this y-down vector.
            public float AngleDeg() { return (float)(Math.Atan2(-Y, X) / Deg2Rad); }

            /// Rotate counter-clockwise on screen by deg degrees.
            public V2 Rotated(float deg)
            {
                float a = deg * Deg2Rad;
                float cs = (float)Math.Cos(a), sn = (float)Math.Sin(a);
                // y-down: counter-clockwise on screen is clockwise in the raw coordinates
                return new V2(X * cs + Y * sn, -X * sn + Y * cs);
            }
        }

        /// Pixel-space signed distance primitive. Bounds enclose the region where Distance < 0.
        abstract class Shape
        {
            public float MinX, MinY, MaxX, MaxY;
            public bool Cut;
            public float Grow;
            public abstract float Distance(float x, float y);

            protected void SetBounds(float minX, float minY, float maxX, float maxY)
            {
                MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
            }
        }

        /// Capsule: segment a-b with half-width hw (round caps).
        sealed class SegmentShape : Shape
        {
            readonly float ax, ay, dx, dy, invLen2, hw;

            public SegmentShape(V2 a, V2 b, float hw)
            {
                ax = a.X; ay = a.Y; dx = b.X - a.X; dy = b.Y - a.Y; this.hw = hw;
                float l2 = dx * dx + dy * dy;
                invLen2 = l2 > 1e-12f ? 1f / l2 : 0f;
                SetBounds(Math.Min(a.X, b.X) - hw, Math.Min(a.Y, b.Y) - hw, Math.Max(a.X, b.X) + hw, Math.Max(a.Y, b.Y) + hw);
            }

            public override float Distance(float x, float y)
            {
                float px = x - ax, py = y - ay;
                float h = Clamp((px * dx + py * dy) * invLen2, 0f, 1f);
                float ex = px - dx * h, ey = py - dy * h;
                return (float)Math.Sqrt(ex * ex + ey * ey) - hw;
            }
        }

        sealed class RingShape : Shape
        {
            readonly float cx, cy, r, hw;

            public RingShape(V2 c, float r, float hw)
            {
                cx = c.X; cy = c.Y; this.r = r; this.hw = hw;
                float e = r + hw;
                SetBounds(cx - e, cy - e, cx + e, cy + e);
            }

            public override float Distance(float x, float y)
            {
                float px = x - cx, py = y - cy;
                return Math.Abs((float)Math.Sqrt(px * px + py * py) - r) - hw;
            }
        }

        sealed class DiscShape : Shape
        {
            readonly float cx, cy, r;

            public DiscShape(V2 c, float r)
            {
                cx = c.X; cy = c.Y; this.r = r;
                SetBounds(cx - r, cy - r, cx + r, cy + r);
            }

            public override float Distance(float x, float y)
            {
                float px = x - cx, py = y - cy;
                return (float)Math.Sqrt(px * px + py * py) - r;
            }
        }

        /// Stroked circular arc with round caps, symmetric about the unit direction (mx,my) with
        /// half-aperture h. The point is expressed in the arc's frame (ly along the mid direction,
        /// lx folded across it); outside the aperture the nearest point is the arc end point.
        sealed class ArcShape : Shape
        {
            readonly float cx, cy, r, hw, mx, my, sinH, cosH;

            public ArcShape(V2 c, float r, V2 mid, float halfAngle, float hw)
            {
                cx = c.X; cy = c.Y; this.r = r; this.hw = hw; mx = mid.X; my = mid.Y;
                sinH = (float)Math.Sin(halfAngle);
                cosH = (float)Math.Cos(halfAngle);
                float e = r + hw;
                SetBounds(cx - e, cy - e, cx + e, cy + e);
            }

            public override float Distance(float x, float y)
            {
                float px = x - cx, py = y - cy;
                float ly = px * mx + py * my;
                float lx = Math.Abs(py * mx - px * my);
                float d;
                if (cosH * lx > sinH * ly)
                {
                    float ex = lx - sinH * r, ey = ly - cosH * r;
                    d = (float)Math.Sqrt(ex * ex + ey * ey);
                }
                else
                {
                    d = Math.Abs((float)Math.Sqrt(px * px + py * py) - r);
                }
                return d - hw;
            }
        }

        /// Rounded box (optionally rotated), filled or stroked.
        sealed class BoxShape : Shape
        {
            readonly float cx, cy, ux, uy, hx, hy, r, hw;
            readonly bool fill;

            public BoxShape(V2 c, V2 axis, float hx, float hy, float r, float hw, bool fill)
            {
                cx = c.X; cy = c.Y; ux = axis.X; uy = axis.Y; this.hx = hx; this.hy = hy;
                this.r = Math.Max(0f, Math.Min(r, Math.Min(hx, hy)));
                this.hw = hw; this.fill = fill;
                float ex = Math.Abs(ux) * hx + Math.Abs(uy) * hy + hw;
                float ey = Math.Abs(uy) * hx + Math.Abs(ux) * hy + hw;
                SetBounds(cx - ex, cy - ey, cx + ex, cy + ey);
            }

            public override float Distance(float x, float y)
            {
                float px = x - cx, py = y - cy;
                float lx = Math.Abs(px * ux + py * uy);
                float ly = Math.Abs(py * ux - px * uy);
                float qx = lx - hx + r, qy = ly - hy + r;
                float ox = Math.Max(qx, 0f), oy = Math.Max(qy, 0f);
                float d = (float)Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0f) - r;
                return fill ? d : Math.Abs(d) - hw;
            }
        }

        /// Filled simple polygon: distance to the nearest edge, sign from a crossing-number test.
        sealed class PolygonShape : Shape
        {
            readonly float[] vx, vy;

            public PolygonShape(V2[] pts)
            {
                int n = pts.Length;
                vx = new float[n];
                vy = new float[n];
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (int i = 0; i < n; i++)
                {
                    vx[i] = pts[i].X; vy[i] = pts[i].Y;
                    minX = Math.Min(minX, vx[i]); maxX = Math.Max(maxX, vx[i]);
                    minY = Math.Min(minY, vy[i]); maxY = Math.Max(maxY, vy[i]);
                }
                SetBounds(minX, minY, maxX, maxY);
            }

            public override float Distance(float x, float y)
            {
                int n = vx.Length;
                float d = float.MaxValue, s = 1f;
                for (int i = 0, j = n - 1; i < n; j = i, i++)
                {
                    float ex = vx[j] - vx[i], ey = vy[j] - vy[i];
                    float wx = x - vx[i], wy = y - vy[i];
                    float l2 = ex * ex + ey * ey;
                    float t = l2 > 1e-12f ? Clamp((wx * ex + wy * ey) / l2, 0f, 1f) : 0f;
                    float bx = wx - ex * t, by = wy - ey * t;
                    d = Math.Min(d, bx * bx + by * by);
                    bool c1 = y >= vy[i], c2 = y < vy[j], c3 = ex * wy > ey * wx;
                    if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
                }
                return s * (float)Math.Sqrt(d);
            }
        }
    }
}
