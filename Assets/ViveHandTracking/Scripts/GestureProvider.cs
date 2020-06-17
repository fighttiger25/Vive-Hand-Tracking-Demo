using System;
using System.Collections;
using UnityEngine;

namespace ViveHandTracking {

// This is the main class used for gesture detection. It handles start/stop of the detection
// automatically once the script is enabled. GestureProvider is a singleton class, make sure there
// is one and only one instance in the whole scene. It provides several static readonly properties
// about detection status and result.
public class GestureProvider : MonoBehaviour {
  // Get detection result of left hand. Returns null if left hand is not detected.
  public static GestureResult LeftHand {
    get {
      return State.LeftHand;
    }
  }
  // Get detection result of right hand. Returns null if right hand is not detected.
  public static GestureResult RightHand {
    get {
      return State.RightHand;
    }
  }
  // Returns running status of gesture detection.
  public static GestureStatus Status {
    get {
      return State.Status;
    }
  }
  // Returns detailed error if Status is Error
  public static GestureFailure Error {
    get {
      return State.Error;
    }
  }
  // Returns true if LeftHand and RightHand are updated in this frame. This can be useful for
  // calculating states that only depends on hand result. Since detection normally have slower FPS
  // than VR rendering, it saves computation power if these states are only updated when hand
  // results change. Always return false if detection is not running.
  public static bool UpdatedInThisFrame {
    get {
      return State.UpdatedInThisFrame;
    }
  }
  // Returns the current singleton (or null if no instance exists).
  public static GestureProvider Current {
    get;
    private set;
  }
  // Current running mode for detection, value is only valid if Status is Starting or Running.
  public static GestureMode Mode {
    get {
      return State.Mode;
    }
  }
  // A shortcut for checking skeleton mode. Equivalent to Status == Skeleton.
  public static bool HaveSkeleton {
    get {
      return Mode == GestureMode.Skeleton;
    }
  }
  public static string EngineName {
    get;
    private set;
  }
  private static EngineState State = new EngineState();

  [SerializeField]
  private GestureOption option;
  [Tooltip("Auto restart detection on error (exclude startup error)")]
  public bool autoRestart = true;

  private VHTSettings settings;
  private HandTrackingEngine engine = null;
  internal int frames {
    get;
    private set;
  }

  void Awake() {
    if (Current != null) {
      Debug.LogWarning("Only one GestureProvider is allowed in the scene.");
      GameObject.Destroy(this);
      return;
    }
    Current = this;
    EngineName = "";
    settings = VHTSettings.GetSettings(true);
    State.ClearState();
    State.Mode = option.mode;
  }

  void OnEnable() {
    if (engine != null)
      StartCoroutine(StartGestureDetection(engine));
  }

  IEnumerator Start () {
    Screen.sleepTimeout = SleepTimeout.NeverSleep;

    // setup wavevr rendering
#if VIVEHANDTRACKING_WITH_WAVEVR
#if UNITY_EDITOR
    // NOTE: use reflection in unity eidtor to avoid compile breakage if wavevr package is removed
    var type = Type.GetType("WaveVR_Render");
    if (type != null) {
      if (transform.GetComponent(type) == null) {
        Destroy(transform.GetComponent<AudioListener>());
        Destroy(transform.GetComponent<FlareLayer>());
        gameObject.AddComponent(type);
        var tracker = gameObject.AddComponent(Type.GetType("WaveVR_DevicePoseTracker"));
        type = tracker.GetType();
        var fieldInfo = type.GetField("type");
        fieldInfo.SetValue(tracker, Enum.ToObject(fieldInfo.FieldType, 1));
      }
    }
#else
    if (transform.GetComponent<WaveVR_Render>() == null) {
      Destroy(transform.GetComponent<AudioListener>());
      Destroy(transform.GetComponent<FlareLayer>());
      gameObject.AddComponent<WaveVR_Render>();
      var tracker = gameObject.AddComponent<WaveVR_DevicePoseTracker>();
#if VIVEHANDTRACKING_WITH_WAVEVR3
      tracker.type = WaveVR_Controller.EDeviceType.Head;
#else
      tracker.type = wvr.WVR_DeviceType.WVR_DeviceType_HMD;
#endif
    }
#endif
    yield return null;
#endif

    foreach (var engine in settings.Engines) {
      if (!engine.IsSupported())
        continue;
      State.ClearState();
      engine.State = State;
      yield return engine.Setup();
      if (State.Status == GestureStatus.Error) {
        Debug.LogError(engine.GetType().Name + " setup failed");
        continue;
      }
      // try start detection
      yield return StartGestureDetection(engine);
      if (State.Status == GestureStatus.Error) {
        engine.StopDetection();
        Debug.LogError(engine.GetType().Name + " start failed");
        continue;
      }
      this.engine = engine;
      EngineName = engine.GetType().Name;
      Debug.Log("Selected " + EngineName);
      break;
    }
    if (engine == null)
      Debug.LogError("No suitable engine found");
  }

  void Update () {
    if (engine == null || Status == GestureStatus.NotStarted || Status == GestureStatus.Error)
      return;
    State.UpdatedInThisFrame = false;
    engine.UpdateResult();
    if (Status == GestureStatus.Error) {
      State.LeftHand = State.RightHand = null;
      // only restart if detection has been running for some time
      if (autoRestart && frames > 100) {
        engine.StopDetection();
        StartCoroutine(StartGestureDetection(engine));
      }
      return;
    }
    if (UpdatedInThisFrame) {
      if (State.Status == GestureStatus.Starting)
        State.Status = GestureStatus.Running;
      if (engine is ViveHandTrackingEngine)
        frames = (engine as ViveHandTrackingEngine).lastIndex;
      else
        frames++;
    }
  }

  IEnumerator StartGestureDetection(HandTrackingEngine engine) {
    if (Status == GestureStatus.Starting || Status == GestureStatus.Running || engine == null)
      yield break;
    yield return engine.StartDetection(option);
    if (Status == GestureStatus.Starting || Status == GestureStatus.Running) {
      State.Error = GestureFailure.None;
      option.mode = State.Mode;
      frames = 0;
    }
    if (State.Error != GestureFailure.None)
      Debug.LogError(engine.GetType().Name + " start failed: " + State.Error);
  }

  void StopGestureDetection() {
    if (engine != null)
      engine.StopDetection();
    State.ClearState();
    frames = 0;
  }

  void OnDisable() {
    StopGestureDetection();
  }

  void OnDestroy() {
    StopGestureDetection();
    Current = null;
  }

  void OnApplicationPause(bool isPaused) {
    if (isPaused)
      StopGestureDetection();
    else if (engine != null)
      StartCoroutine(StartGestureDetection(engine));
  }

  void OnApplicationQuit() {
    StopGestureDetection();
  }
}

}
