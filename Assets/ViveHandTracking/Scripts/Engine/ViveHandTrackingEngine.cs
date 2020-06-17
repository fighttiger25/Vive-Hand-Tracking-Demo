using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
#if UNITY_2018_3_OR_NEWER
using UnityEngine.Android;
#endif

namespace ViveHandTracking {

public class ViveHandTrackingEngine: HandTrackingEngine {
#if (VIVEHANDTRACKING_WITH_WAVEVR || VIVEHANDTRACKING_WITH_GOOGLEVR) && !UNITY_EDITOR
  private static string[] permissionNames = { "android.permission.CAMERA" };
#endif
  internal int lastIndex = -1;

  public override bool IsSupported() {
    return true;
  }

  public override IEnumerator Setup() {
    lastIndex = -1;
    var transform = GestureProvider.Current.transform;
    var gameObject = GestureProvider.Current.gameObject;

#if UNITY_ANDROID && !UNITY_EDITOR
#if VIVEHANDTRACKING_WITH_WAVEVR
    // get camera permission
    var pmInstance = WaveVR_PermissionManager.instance;
    while (!pmInstance.isInitialized())
      yield return null;

    bool granting = true;
    bool hasPermission = pmInstance.isPermissionGranted(permissionNames[0]);
    WaveVR_PermissionManager.requestCompleteCallback callback = (results) => {
      granting = false;
      if (results.Count > 0 && results[0].Granted)
        hasPermission = true;
    };
    while (!hasPermission) {
      granting = true;
      pmInstance.requestPermissions(permissionNames, callback);
      while (granting)
        yield return null;
    }
#elif VIVEHANDTRACKING_WITH_GOOGLEVR
    // get camera permission using daydream API
    if (UnityEngine.VR.VRSettings.loadedDeviceName == "daydream") {
      var permissionRequester = GvrPermissionsRequester.Instance;
      bool granting = true;
      Action<GvrPermissionsRequester.PermissionStatus[]> callback = (results) => granting = false;
      while (!permissionRequester.IsPermissionGranted(permissionNames[0])) {
        granting = true;
        permissionRequester.RequestPermissions(permissionNames, callback);
        while (granting)
          yield return null;
      }
    }
#elif UNITY_2018_3_OR_NEWER
    // Unity 2018.3 or newer adds support for android runtime permission
    while (!Permission.HasUserAuthorizedPermission(Permission.Camera)) {
      Permission.RequestUserPermission(Permission.Camera);
      yield return null;
    }
#else
    while (!Application.HasUserAuthorization(UserAuthorization.WebCam))
      yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
#endif
#endif

    GestureInterface.UseExternalTransform(true);
    yield break;
  }

  public override IEnumerator StartDetection(GestureOption option) {
#if VIVEHANDTRACKING_WITH_WAVEVR && !UNITY_EDITOR
    // retry at most 5 times if camera is not ready
    int count = 5;
#else
    int count = 1;
#endif
    while (count > 0) {
      State.Error = GestureInterface.StartGestureDetection(option);
      if (State.Error == GestureFailure.Camera && count > 1) {
        Debug.LogError("Start camera failed, retrying...");
        yield return new WaitForSeconds(0.5f);
        count--;
        continue;
      } else if (State.Error != GestureFailure.None) {
        Debug.LogError("Start gesture detection failed: " + State.Error);
        State.Status = GestureStatus.Error;
      } else {
        State.Mode = option.mode;
        State.Status = GestureStatus.Starting;
      }
      break;
    }
  }

  public override void UpdateResult() {
    var transform = GestureProvider.Current.transform;
    GestureInterface.SetCameraTransform(transform.position, transform.rotation);

    IntPtr ptr;
    int index;
    var size = GestureInterface.GetGestureResult(out ptr, out index);

    if (index < 0) {
      Debug.LogError("Gesture detection stopped");
      State.Status = GestureStatus.Error;
      State.Error = GestureFailure.Internal;
      return;
    }
    if (index <= lastIndex)
      return;
    lastIndex = index;
    State.UpdatedInThisFrame = true;

    State.LeftHand = State.RightHand = null;
    if (size <= 0)
      return;

    var structSize = Marshal.SizeOf(typeof(GestureResultRaw));
    for (var i = 0; i < size; i++) {
      var gesture = (GestureResultRaw)Marshal.PtrToStructure(ptr, typeof(GestureResultRaw));
      ptr = new IntPtr(ptr.ToInt64() + structSize);
      if (gesture.isLeft)
        State.LeftHand = new GestureResult(gesture);
      else
        State.RightHand = new GestureResult(gesture);
    }
  }

  public override void StopDetection() {
    GestureInterface.StopGestureDetection();
    lastIndex = - 1;
  }

  public override string Description() {
    return "Default detection engine";
  }
}

}
