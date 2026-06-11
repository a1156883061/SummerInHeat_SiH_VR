using UnityVRMod.Core;

namespace UnityVRMod.Features.VrVisualization
{
    internal sealed class GameCameraRigFollowState
    {
        private const float PositionEpsilonSqr = 0.000001f;
        private const float YawEpsilonDegrees = 0.001f;
        private const float MaxPositionDeltaMetersPerFrame = 2.0f;
        private const float MaxYawDeltaDegreesPerFrame = 90.0f;

        private bool _hasLastPose;
        private Vector3 _lastPosition;
        private float _lastYawDegrees;

        public void Reset(Camera sourceCamera)
        {
            if (sourceCamera == null)
            {
                Clear();
                return;
            }

            _lastPosition = sourceCamera.transform.position;
            _lastYawDegrees = sourceCamera.transform.eulerAngles.y;
            _hasLastPose = true;
        }

        public void ResetRigToSourceEyePosition(
            Camera sourceCamera,
            GameObject vrRig,
            Vector3 hmdLocalPosition,
            float verticalOffset,
            bool includeHmdHorizontalPosition)
        {
            if (sourceCamera == null || vrRig == null)
            {
                Clear();
                return;
            }

            Vector3 sourcePosition = sourceCamera.transform.position;
            float sourceYawDegrees = sourceCamera.transform.eulerAngles.y;
            Vector3 hmdAlignmentLocalPosition = includeHmdHorizontalPosition
                ? hmdLocalPosition
                : new Vector3(0f, hmdLocalPosition.y, 0f);
            vrRig.transform.position = sourcePosition - vrRig.transform.TransformVector(hmdAlignmentLocalPosition) + (Vector3.up * verticalOffset);

            _lastPosition = sourcePosition;
            _lastYawDegrees = sourceYawDegrees;
            _hasLastPose = true;
        }

        public void Clear()
        {
            _hasLastPose = false;
            _lastPosition = Vector3.zero;
            _lastYawDegrees = 0f;
        }

        public void ApplyDelta(Camera sourceCamera, GameObject vrRig)
        {
            if (sourceCamera == null || vrRig == null)
            {
                Clear();
                return;
            }

            Vector3 currentPosition = sourceCamera.transform.position;
            float currentYawDegrees = sourceCamera.transform.eulerAngles.y;
            if (!IsFinite(currentPosition) || !IsFinite(currentYawDegrees))
            {
                Clear();
                return;
            }

            if (!_hasLastPose)
            {
                _lastPosition = currentPosition;
                _lastYawDegrees = currentYawDegrees;
                _hasLastPose = true;
                return;
            }

            Vector3 positionDelta = currentPosition - _lastPosition;
            float yawDeltaDegrees = Mathf.DeltaAngle(_lastYawDegrees, currentYawDegrees);
            float positionDeltaSqr = positionDelta.sqrMagnitude;
            if (!IsFinite(positionDelta) || !IsFinite(positionDeltaSqr) || !IsFinite(yawDeltaDegrees))
            {
                Clear();
                return;
            }

            if (positionDeltaSqr > MaxPositionDeltaMetersPerFrame * MaxPositionDeltaMetersPerFrame)
            {
                positionDelta = positionDelta.normalized * MaxPositionDeltaMetersPerFrame;
                positionDeltaSqr = positionDelta.sqrMagnitude;
            }

            yawDeltaDegrees = Mathf.Clamp(yawDeltaDegrees, -MaxYawDeltaDegreesPerFrame, MaxYawDeltaDegreesPerFrame);

            if (positionDeltaSqr > PositionEpsilonSqr)
            {
                vrRig.transform.position += positionDelta;
            }

            if (Mathf.Abs(yawDeltaDegrees) > YawEpsilonDegrees)
            {
                vrRig.transform.rotation = Quaternion.AngleAxis(yawDeltaDegrees, Vector3.up) * vrRig.transform.rotation;
            }

            _lastPosition = currentPosition;
            _lastYawDegrees = currentYawDegrees;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
