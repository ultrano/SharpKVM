using Avalonia;
using System;

namespace SharpKVM
{
    public static class LayoutGeometry
    {
        public const double PERPENDICULAR_OVERLAP_MIN = 8;
        public const double EDGE_TOLERANCE = 1.0;
        public const int EDGE_BUFFER_MIN = 5;
        public const int EDGE_BUFFER_MAX = 30;
        public const double EDGE_BUFFER_SCREEN_RATIO = 0.01;

        public static EdgeDirection OppositeEdge(EdgeDirection edge)
        {
            if (edge == EdgeDirection.Left) return EdgeDirection.Right;
            if (edge == EdgeDirection.Right) return EdgeDirection.Left;
            if (edge == EdgeDirection.Top) return EdgeDirection.Bottom;
            if (edge == EdgeDirection.Bottom) return EdgeDirection.Top;
            return EdgeDirection.None;
        }

        public static double Overlap1D(double a1, double a2, double b1, double b2)
        {
            double lo = Math.Max(Math.Min(a1, a2), Math.Min(b1, b2));
            double hi = Math.Min(Math.Max(a1, a2), Math.Max(b1, b2));
            return Math.Max(0, hi - lo);
        }

        public static bool IsHorizontalEdge(EdgeDirection edge) => edge == EdgeDirection.Left || edge == EdgeDirection.Right;

        public static bool HasPerpendicularOverlapForEntry(Rect sourceRect, Rect targetRect, EdgeDirection entryEdge)
        {
            if (entryEdge == EdgeDirection.Left || entryEdge == EdgeDirection.Right)
            {
                return Overlap1D(sourceRect.Top, sourceRect.Bottom, targetRect.Top, targetRect.Bottom) > PERPENDICULAR_OVERLAP_MIN;
            }
            if (entryEdge == EdgeDirection.Top || entryEdge == EdgeDirection.Bottom)
            {
                return Overlap1D(sourceRect.Left, sourceRect.Right, targetRect.Left, targetRect.Right) > PERPENDICULAR_OVERLAP_MIN;
            }
            return false;
        }

        public static bool AreEdgesAdjacent(Rect sourceRect, EdgeDirection exitEdge, Rect targetRect, double tolerance)
        {
            if (exitEdge == EdgeDirection.Left)
            {
                return Math.Abs(sourceRect.Left - targetRect.Right) <= tolerance &&
                       Overlap1D(sourceRect.Top, sourceRect.Bottom, targetRect.Top, targetRect.Bottom) > PERPENDICULAR_OVERLAP_MIN;
            }
            if (exitEdge == EdgeDirection.Right)
            {
                return Math.Abs(sourceRect.Right - targetRect.Left) <= tolerance &&
                       Overlap1D(sourceRect.Top, sourceRect.Bottom, targetRect.Top, targetRect.Bottom) > PERPENDICULAR_OVERLAP_MIN;
            }
            if (exitEdge == EdgeDirection.Top)
            {
                return Math.Abs(sourceRect.Top - targetRect.Bottom) <= tolerance &&
                       Overlap1D(sourceRect.Left, sourceRect.Right, targetRect.Left, targetRect.Right) > PERPENDICULAR_OVERLAP_MIN;
            }
            if (exitEdge == EdgeDirection.Bottom)
            {
                return Math.Abs(sourceRect.Bottom - targetRect.Top) <= tolerance &&
                       Overlap1D(sourceRect.Left, sourceRect.Right, targetRect.Left, targetRect.Right) > PERPENDICULAR_OVERLAP_MIN;
            }
            return false;
        }

        public static Point MapPointAcrossEdge(Point sourcePoint, Rect sourceRect, EdgeDirection exitEdge, Rect targetRect)
        {
            if (exitEdge == EdgeDirection.Left || exitEdge == EdgeDirection.Right)
            {
                double overlapTop = Math.Max(sourceRect.Top, targetRect.Top);
                double overlapBottom = Math.Min(sourceRect.Bottom, targetRect.Bottom);
                double mappedY;
                if (overlapBottom > overlapTop)
                {
                    mappedY = Math.Clamp(sourcePoint.Y, overlapTop, overlapBottom);
                }
                else
                {
                    double ratio = sourceRect.Height > 0 ? Math.Clamp((sourcePoint.Y - sourceRect.Top) / sourceRect.Height, 0.0, 1.0) : 0.5;
                    mappedY = targetRect.Top + ratio * targetRect.Height;
                }
                double mappedX = exitEdge == EdgeDirection.Left ? targetRect.Right : targetRect.Left;
                return new Point(mappedX, mappedY);
            }

            double overlapLeft = Math.Max(sourceRect.Left, targetRect.Left);
            double overlapRight = Math.Min(sourceRect.Right, targetRect.Right);
            double mappedX2;
            if (overlapRight > overlapLeft)
            {
                mappedX2 = Math.Clamp(sourcePoint.X, overlapLeft, overlapRight);
            }
            else
            {
                double ratio = sourceRect.Width > 0 ? Math.Clamp((sourcePoint.X - sourceRect.Left) / sourceRect.Width, 0.0, 1.0) : 0.5;
                mappedX2 = targetRect.Left + ratio * targetRect.Width;
            }
            double mappedY2 = exitEdge == EdgeDirection.Top ? targetRect.Bottom : targetRect.Top;
            return new Point(mappedX2, mappedY2);
        }

        public static Rect AttachToScreenEdge(Rect rect, Rect screenBounds, EdgeDirection edge)
        {
            if (edge == EdgeDirection.Left)
            {
                double y = Math.Clamp(rect.Y, screenBounds.Top - rect.Height + 8, screenBounds.Bottom - 8);
                return new Rect(screenBounds.Left - rect.Width, y, rect.Width, rect.Height);
            }
            if (edge == EdgeDirection.Right)
            {
                double y = Math.Clamp(rect.Y, screenBounds.Top - rect.Height + 8, screenBounds.Bottom - 8);
                return new Rect(screenBounds.Right, y, rect.Width, rect.Height);
            }
            if (edge == EdgeDirection.Top)
            {
                double x = Math.Clamp(rect.X, screenBounds.Left - rect.Width + 8, screenBounds.Right - 8);
                return new Rect(x, screenBounds.Top - rect.Height, rect.Width, rect.Height);
            }
            if (edge == EdgeDirection.Bottom)
            {
                double x = Math.Clamp(rect.X, screenBounds.Left - rect.Width + 8, screenBounds.Right - 8);
                return new Rect(x, screenBounds.Bottom, rect.Width, rect.Height);
            }
            return rect;
        }

        public static double GetDistanceToRect(Point pt, Rect rect)
        {
            double dx = Math.Max(rect.Left - pt.X, Math.Max(0, pt.X - rect.Right));
            double dy = Math.Max(rect.Top - pt.Y, Math.Max(0, pt.Y - rect.Bottom));
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static Point GetNearestPointOnEdge(Point source, Rect rect, EdgeDirection edge)
        {
            if (edge == EdgeDirection.Left) return new Point(rect.Left, Math.Clamp(source.Y, rect.Top, rect.Bottom));
            if (edge == EdgeDirection.Right) return new Point(rect.Right, Math.Clamp(source.Y, rect.Top, rect.Bottom));
            if (edge == EdgeDirection.Top) return new Point(Math.Clamp(source.X, rect.Left, rect.Right), rect.Top);
            if (edge == EdgeDirection.Bottom) return new Point(Math.Clamp(source.X, rect.Left, rect.Right), rect.Bottom);
            return source;
        }

        public static bool IsCandidateOnSide(Rect rootBounds, Rect candidate, EdgeDirection edge)
        {
            if (edge == EdgeDirection.Left) return candidate.Center.X <= rootBounds.Center.X;
            if (edge == EdgeDirection.Right) return candidate.Center.X >= rootBounds.Center.X;
            if (edge == EdgeDirection.Top) return candidate.Center.Y <= rootBounds.Center.Y;
            if (edge == EdgeDirection.Bottom) return candidate.Center.Y >= rootBounds.Center.Y;
            return false;
        }

        public static bool IsGlobalOuterEdge(Rect screenBounds, EdgeDirection edge, Rect globalDesktopBounds)
        {
            if (edge == EdgeDirection.Left) return Math.Abs(screenBounds.Left - globalDesktopBounds.Left) <= EDGE_TOLERANCE;
            if (edge == EdgeDirection.Right) return Math.Abs(screenBounds.Right - globalDesktopBounds.Right) <= EDGE_TOLERANCE;
            if (edge == EdgeDirection.Top) return Math.Abs(screenBounds.Top - globalDesktopBounds.Top) <= EDGE_TOLERANCE;
            if (edge == EdgeDirection.Bottom) return Math.Abs(screenBounds.Bottom - globalDesktopBounds.Bottom) <= EDGE_TOLERANCE;
            return false;
        }

        public static Rect ClampRectByAnchor(Rect rect, EdgeDirection anchorEdge, Rect globalDesktopBounds)
        {
            if (anchorEdge == EdgeDirection.Left || anchorEdge == EdgeDirection.Right)
            {
                double y = Math.Clamp(rect.Y, globalDesktopBounds.Top, Math.Max(globalDesktopBounds.Top, globalDesktopBounds.Bottom - rect.Height));
                return new Rect(rect.X, y, rect.Width, rect.Height);
            }

            if (anchorEdge == EdgeDirection.Top || anchorEdge == EdgeDirection.Bottom)
            {
                double x = Math.Clamp(rect.X, globalDesktopBounds.Left, Math.Max(globalDesktopBounds.Left, globalDesktopBounds.Right - rect.Width));
                return new Rect(x, rect.Y, rect.Width, rect.Height);
            }

            return rect;
        }

        public static bool TryGetEdgeAtLocalScreen(Rect screenBounds, int x, int y, out EdgeDirection edge)
        {
            edge = EdgeDirection.None;
            int buffer = (int)(Math.Min(screenBounds.Width, screenBounds.Height) * EDGE_BUFFER_SCREEN_RATIO);
            buffer = Math.Clamp(buffer, EDGE_BUFFER_MIN, EDGE_BUFFER_MAX);

            bool inY = y >= screenBounds.Top - buffer && y <= screenBounds.Bottom + buffer;
            bool inX = x >= screenBounds.Left - buffer && x <= screenBounds.Right + buffer;

            if (inY && x <= screenBounds.Left + buffer) edge = EdgeDirection.Left;
            else if (inY && x >= screenBounds.Right - 1 - buffer) edge = EdgeDirection.Right;
            else if (inX && y <= screenBounds.Top + buffer) edge = EdgeDirection.Top;
            else if (inX && y >= screenBounds.Bottom - 1 - buffer) edge = EdgeDirection.Bottom;

            return edge != EdgeDirection.None;
        }
    }
}
