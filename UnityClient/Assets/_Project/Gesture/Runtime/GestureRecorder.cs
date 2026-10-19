using System;
using UnityEngine;

namespace WarOfWizards.Gesture
{
    public class GestureRecorder : MonoBehaviour
    {
        [Header("Tracking Source")]
        [SerializeField] private Transform castingPoint; // Tip of wand or index finger
        [SerializeField] private float minPointDistance = 0.015f; // 1.5 cm sample threshold

        public bool IsRecording { get; private set; }
        public GestureSequence CurrentSequence { get; private set; } = new GestureSequence();

        public event Action OnRecordingStarted;
        public event Action<GestureSequence> OnRecordingCompleted;
        public event Action OnRecordingCancelled;

        private Vector3 lastRecordedPos;

        public void SetCastingPoint(Transform point)
        {
            castingPoint = point;
        }

        public void StartRecording()
        {
            if (castingPoint == null)
            {
                Debug.LogWarning("[GestureRecorder] Casting point is null!");
                return;
            }

            IsRecording = true;
            CurrentSequence.Clear();
            lastRecordedPos = castingPoint.position;
            CurrentSequence.AddPoint(lastRecordedPos, Time.time);

            OnRecordingStarted?.Invoke();
        }

        public void StopRecording()
        {
            if (!IsRecording) return;
            IsRecording = false;

            if (castingPoint != null)
            {
                CurrentSequence.AddPoint(castingPoint.position, Time.time);
            }

            if (GestureValidator.IsValidGesture(CurrentSequence, out string reason))
            {
                OnRecordingCompleted?.Invoke(CurrentSequence);
            }
            else
            {
                Debug.Log($"[GestureRecorder] Discarded invalid gesture: {reason}");
                OnRecordingCancelled?.Invoke();
            }
        }

        private void Update()
        {
            if (!IsRecording || castingPoint == null) return;

            Vector3 currentPos = castingPoint.position;
            if (Vector3.Distance(currentPos, lastRecordedPos) >= minPointDistance)
            {
                CurrentSequence.AddPoint(currentPos, Time.time);
                lastRecordedPos = currentPos;
            }
        }
    }
}
