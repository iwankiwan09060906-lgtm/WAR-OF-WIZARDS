using UnityEngine;

namespace WarOfWizards.Gesture
{
    public static class GestureValidator
    {
        public const float MIN_STROKE_LENGTH = 0.25f; // at least 25cm in VR space
        public const float MIN_STROKE_DURATION = 0.2f; // at least 200ms
        public const float MAX_STROKE_DURATION = 4.0f; // maximum 4 seconds
        public const int MIN_RAW_POINTS = 8;

        public static bool IsValidGesture(GestureSequence sequence, out string reason)
        {
            if (sequence == null || sequence.Count < MIN_RAW_POINTS)
            {
                reason = "Not enough recorded points";
                return false;
            }

            if (sequence.Duration < MIN_STROKE_DURATION)
            {
                reason = "Stroke too fast or too short";
                return false;
            }

            if (sequence.Duration > MAX_STROKE_DURATION)
            {
                reason = "Stroke exceeded time limit";
                return false;
            }

            float arcLength = sequence.CalculateTotalArcLength();
            if (arcLength < MIN_STROKE_LENGTH)
            {
                reason = $"Stroke length ({arcLength:F2}m) below minimum ({MIN_STROKE_LENGTH:F2}m)";
                return false;
            }

            reason = "Valid";
            return true;
        }
    }
}
