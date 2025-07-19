using UnityEngine;

namespace YNode.Editor.Internal
{
    public struct ReroutePoint
    {
        public Port Port;
        public int PointIndex;

        public ReroutePoint(Port port, int pointIndex)
        {
            this.Port = port;
            this.PointIndex = pointIndex;
        }

        public void InsertPoint(Vector2 pos)
        {
            Port.GetReroutePoints().Insert(PointIndex, pos);
        }

        public void SetPoint(Vector2 pos)
        {
            Port.GetReroutePoints()[PointIndex] = pos;
        }

        public void RemovePoint()
        {
            Port.GetReroutePoints().RemoveAt(PointIndex);
        }

        public Vector2 GetPoint()
        {
            return Port.GetReroutePoints()[PointIndex];
        }

        public Rect GetRect()
        {
            return GetRect(Port.GetReroutePoints()[PointIndex]);
        }

        public static Rect GetRect(Vector3 point)
        {
            var rect = new Rect(point, new Vector2(12, 12));
            rect.position = new Vector2(rect.position.x - 6, rect.position.y - 6);
            return rect;
        }
    }
}
