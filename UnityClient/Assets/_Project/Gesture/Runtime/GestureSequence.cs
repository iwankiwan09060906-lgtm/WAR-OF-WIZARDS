using System;
using System.Collections.Generic;
using UnityEngine;

namespace WarOfWizards.Gesture
{
    [Serializable]
    public struct GesturePoint
    {
        public Vector3 position;
        public float timestamp;

        public GesturePoint(Vector3 pos, float time)
        {
            position = pos;
            timestamp = time;
        }
    }

    [Serializable]
    public class GestureSequence
    {
        public List<GesturePoint> points = new List<GesturePoint>();
        public float startTime;
        public float endTime;

        public int Count => points.Count;
        public float Duration => endTime - startTime;

        public void AddPoint(Vector3 pos, float time)
        {
            if (points.Count == 0) startTime = time;
            endTime = time;
            points.Add(new GesturePoint(pos, time));
        }

        public void Clear()
        {
            points.Clear();
            startTime = 0f;
            endTime = 0f;
        }

        public Vector3[] GetPositions()
        {
            Vector3[] positions = new Vector3[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                positions[i] = points[i].position;
            }
            return positions;
        }

        public float CalculateTotalArcLength()
        {
            if (points.Count < 2) return 0f;
            float total = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                total += Vector3.Distance(points[i - 1].position, points[i].position);
            }
            return total;
        }
    }
}
