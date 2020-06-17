﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace ViveHandTracking {

[Flags]
enum HandFlag {
  NoHand = 1,
  Unknown = 2, Point = 4, Fist = 8, OK = 16, Like = 32, Five = 64, Victory = 128,
  Pinch = 8192
}

[Serializable]
class HandResetCondition {
  [Tooltip("Reset immediately if left hand is missing")]
  public bool LeftHandMissing = true;
  [Tooltip("Reset immediately if right hand is missing")]
  public bool RightHandMissing = true;
}

[Serializable]
class HandStateCondition {
  [Tooltip("Left hand gesture to match. Select Nothing to never enter this condition.")]
  [EnumFlagsAttribute]
  public HandFlag Left = (HandFlag)(-1);
  [Tooltip("Right hand gesture to match. Select Nothing to never enter this condition.")]
  [EnumFlagsAttribute]
  public HandFlag Right = (HandFlag)(-1);
  [Tooltip("How many continous frames of matched hand state before entering this state.")]
  [Range(0, 20)]
  public int MinMatchFrames = 4;
  [Tooltip("How many continous frames of non-matched hand state before leaving this state.")]
  [Range(0, 30)]
  public int MaxMissingFrames = 20;
}

[Serializable]
class HandStateChangeEvent : UnityEvent<int> {}

class HandStateChecker : MonoBehaviour {
  [Tooltip("Conditions for reseting to state 0")]
  public HandResetCondition ResetCondition = null;
  [Tooltip("Conditions for state 1")]
  public HandStateCondition PrepareCondition = null;
  [Tooltip("Conditions for state 2")]
  public HandStateCondition TriggerCondition = null;
  [Tooltip("Allow enter state 2 from state 0")]
  public bool CanSkipPrepare = false;

  public HandStateChangeEvent OnStateChanged = null;

  // 0 - None, 1 - Prepare, 2 - Trigger
  public int state {
    get;
    private set;
  }
  private int PrepareMatchCounter = 0;
  private int TriggerMatchCounter = 0;
  private int MissingCounter = 0;

  void Update () {
    HandFlag LeftFlag = GetFlag(GestureProvider.LeftHand);
    HandFlag RightFlag = GetFlag(GestureProvider.RightHand);

    if ((ResetCondition.LeftHandMissing && LeftFlag == HandFlag.NoHand) ||
        (ResetCondition.RightHandMissing && RightFlag == HandFlag.NoHand)) {
      SetState(0);
      MissingCounter = 0;
      PrepareMatchCounter = PrepareCondition.MinMatchFrames;
    } else if (IsFlagMatch(LeftFlag, RightFlag, PrepareCondition)) {
      if (PrepareMatchCounter > 0)
        PrepareMatchCounter--;
      else {
        SetState(1);
        MissingCounter = PrepareCondition.MaxMissingFrames;
        PrepareMatchCounter = 0;
        TriggerMatchCounter = TriggerCondition.MinMatchFrames;
      }
    } else if ((state != 0 || CanSkipPrepare) && IsFlagMatch(LeftFlag, RightFlag, TriggerCondition)) {
      if (TriggerMatchCounter > 0)
        TriggerMatchCounter--;
      else {
        SetState(2);
        MissingCounter = TriggerCondition.MaxMissingFrames;
        TriggerMatchCounter = 0;
        PrepareMatchCounter = PrepareCondition.MinMatchFrames;
      }
    } else if (MissingCounter > 0)
      MissingCounter--;
    else {
      SetState(0);
      MissingCounter = 0;
      PrepareMatchCounter = PrepareCondition.MinMatchFrames;
    }
  }

  HandFlag GetFlag(GestureResult hand) {
    var flag = HandFlag.NoHand;
    if (hand != null) {
      flag = (HandFlag)(2 << (int)hand.gesture);
      if (hand.pinch.isPinching)
        flag |= HandFlag.Pinch;
    }
    return flag;
  }

  bool IsFlagMatch(HandFlag left, HandFlag right, HandStateCondition condition) {
    return ((left & condition.Left) != (HandFlag)(0)) && ((right & condition.Right) != (HandFlag)(0));
  }

  void SetState(int newState) {
    if (state != newState && OnStateChanged != null)
      OnStateChanged.Invoke((int)newState);
    state = newState;
  }
}

}
