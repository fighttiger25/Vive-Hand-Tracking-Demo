using System.Collections;
using UnityEngine;

namespace ViveHandTracking {

#if VIVEHANDTRACKING_WAVEVR_HAND && !UNITY_EDITOR

public class WaveVRHandEngine: HandTrackingEngine {
  private wvr.WVR_HandTrackingData_t trackingData = new wvr.WVR_HandTrackingData_t();
  private WaveVR_Utils.RigidTransform rigidTransform = WaveVR_Utils.RigidTransform.identity;
  private GestureResultRaw leftHand, rightHand;

  public override bool IsSupported() {
    return true;
  }

  public override IEnumerator Setup() {
    var transform = GestureProvider.Current.transform;
    var gameObject = GestureProvider.Current.gameObject;

    if (WaveVR_GestureManager.Instance == null) {
      gameObject.AddComponent<WaveVR_GestureManager>();
      WaveVR_GestureManager.Instance.EnableHandGesture = false;
      WaveVR_GestureManager.Instance.EnableHandTracking = false;
    }

    leftHand = CreateHand(true);
    rightHand = CreateHand(false);
    yield break;
  }

  public override IEnumerator StartDetection(GestureOption option) {
    if (State.Status == GestureStatus.Starting || State.Status == GestureStatus.Running)
      yield break;

    var gestureStatus = WaveVR_GestureManager.Instance.GetHandGestureStatus();
    if (gestureStatus == WaveVR_Utils.HandGestureStatus.UNSUPPORT) {
      Debug.LogError("WaveVR gesture not supported");
      State.Status = GestureStatus.Error;
      yield break;
    }
    var trackingStatus = WaveVR_GestureManager.Instance.GetHandTrackingStatus();
    if (trackingStatus == WaveVR_Utils.HandTrackingStatus.UNSUPPORT) {
      Debug.LogError("WaveVR tracking not supported");
      State.Status = GestureStatus.Error;
      yield break;
    }

    WaveVR_GestureManager.Instance.EnableHandGesture = true;
    WaveVR_GestureManager.Instance.EnableHandTracking = true;
    State.Mode = GestureMode.Skeleton;

    while (true) {
      yield return null;
      gestureStatus = WaveVR_GestureManager.Instance.GetHandGestureStatus();
      if (gestureStatus == WaveVR_Utils.HandGestureStatus.NOT_START ||
          gestureStatus == WaveVR_Utils.HandGestureStatus.STARTING)
        continue;
      trackingStatus = WaveVR_GestureManager.Instance.GetHandTrackingStatus();
      if (trackingStatus == WaveVR_Utils.HandTrackingStatus.NOT_START ||
          trackingStatus == WaveVR_Utils.HandTrackingStatus.STARTING)
        continue;
      break;
    }

    if (gestureStatus != WaveVR_Utils.HandGestureStatus.AVAILABLE) {
      Debug.LogError("WaveVR gesture start failed: " + gestureStatus);
      State.Status = GestureStatus.Error;
      WaveVR_GestureManager.Instance.EnableHandGesture = false;
      WaveVR_GestureManager.Instance.EnableHandTracking = false;
      yield break;
    }
    if (trackingStatus != WaveVR_Utils.HandTrackingStatus.AVAILABLE) {
      Debug.LogError("WaveVR tracking start failed: " + trackingStatus);
      State.Status = GestureStatus.Error;
      WaveVR_GestureManager.Instance.EnableHandGesture = false;
      WaveVR_GestureManager.Instance.EnableHandTracking = false;
      yield break;
    }
    State.Status = GestureStatus.Running;
  }

  public override void UpdateResult() {
    if (State.Status != GestureStatus.Running)
      return;
    if (!WaveVR_GestureManager.Instance.GetHandTrackingData(ref trackingData, WaveVR_Render.Instance.origin, 0)) {
      Debug.LogError("Tracking stopped");
      State.Status = GestureStatus.Error;
      State.Error = GestureFailure.Internal;
      return;
    }
    State.UpdatedInThisFrame = true;
    State.LeftHand = State.RightHand = null;

    if (trackingData.left.IsValidPose) {
      leftHand.gesture = MapGesture(WaveVR_GestureManager.Instance.GetCurrentLeftHandStaticGesture());
      SetHandPoints(leftHand, trackingData.left, trackingData.leftFinger);
      State.LeftHand = new GestureResult(leftHand);
    }
    if (trackingData.right.IsValidPose) {
      rightHand.gesture = MapGesture(WaveVR_GestureManager.Instance.GetCurrentRightHandStaticGesture());
      SetHandPoints(rightHand, trackingData.right, trackingData.rightFinger);
      State.RightHand = new GestureResult(rightHand);
    }
  }

  public override void StopDetection() {
    WaveVR_GestureManager.Instance.EnableHandGesture = false;
    WaveVR_GestureManager.Instance.EnableHandTracking = false;
  }

  GestureResultRaw CreateHand(bool left) {
    var hand = new GestureResultRaw();
    hand.isLeft = left;
    hand.points = new Vector3[21];
    hand.confidence = 1;
    return hand;
  }

  GestureType MapGesture(wvr.WVR_HandGestureType gesture) {
    switch (gesture) {
    case wvr.WVR_HandGestureType.WVR_HandGestureType_Fist:
      return GestureType.Fist;
    case wvr.WVR_HandGestureType.WVR_HandGestureType_Five:
      return GestureType.Five;
    case wvr.WVR_HandGestureType.WVR_HandGestureType_OK:
      return GestureType.OK;
    case wvr.WVR_HandGestureType.WVR_HandGestureType_ThumbUp:
      return GestureType.Like;
    case wvr.WVR_HandGestureType.WVR_HandGestureType_IndexUp:
      return GestureType.Point;
    default:
      return GestureType.Unknown;
    }
  }

  void SetHandPoints(GestureResultRaw hand, wvr.WVR_PoseState_t pose, wvr.WVR_Fingers_t fingers) {
    if (hand == null || !pose.IsValidPose)
      return;
    rigidTransform.update(pose.PoseMatrix);
    hand.points[0] = rigidTransform.pos;
    SetFingerPoints(hand, fingers.thumb, 1);
    SetFingerPoints(hand, fingers.index, 5);
    SetFingerPoints(hand, fingers.middle, 9);
    SetFingerPoints(hand, fingers.ring, 13);
    SetFingerPoints(hand, fingers.pinky, 17);

    // calculate pinch level
    hand.pinchLevel = Mathf.Clamp01(0.0425f - Vector3.Distance(hand.points[4], hand.points[8])) / 0.025f;

    // apply camera offset to hand points
    var transform = GestureProvider.Current.transform;
    if (transform.parent != null) {
      for (int i = 0; i < 21; i++)
        hand.points[i] = transform.parent.rotation * hand.points[i] + transform.parent.position;
    }
  }

  void SetFingerPoints(GestureResultRaw hand, wvr.WVR_SingleFinger_t finger, int startIndex) {
    hand.points[startIndex] = WaveVR_Utils.GetPosition(finger.joint1);
    hand.points[startIndex + 1] = WaveVR_Utils.GetPosition(finger.joint2);
    hand.points[startIndex + 2] = WaveVR_Utils.GetPosition(finger.joint3);
    hand.points[startIndex + 3] = WaveVR_Utils.GetPosition(finger.tip);
  }
}

#else

public class WaveVRHandEngine: HandTrackingEngine {
  public override bool IsSupported() {
    return false;
  }

  public override IEnumerator Setup() {
    yield break;
  }

  public override IEnumerator StartDetection(GestureOption option) {
    yield break;
  }

  public override void UpdateResult() {}

  public override void StopDetection() {}

  public override string Description() {
#if VIVEHANDTRACKING_WAVEVR_HAND
    return "[Experimental] Requires real WaveVR device";
#else
    return "[Experimental] Requires WaveVR 3.1.94+";
#endif
  }
}

#endif

}
