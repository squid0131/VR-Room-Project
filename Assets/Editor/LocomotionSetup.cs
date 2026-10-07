using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

/// <summary>
/// 開いているシーンの移動・旋回設定を適用する
/// 左スティック: 移動 / 右スティック: なめらかな旋回（Snap Turn は無効）
/// </summary>
[InitializeOnLoad]
public static class LocomotionSetup
{
    private const string ActionsPath = "Assets/Samples/XR Interaction Toolkit/3.1.3/Starter Assets/XRI Default Input Actions.inputactions";
    private const string ScenePath = "Assets/Scenes/Macky Room.unity";
    private const string AppliedKey = "VRRoom.LocomotionSetup.Applied.v1";

    static LocomotionSetup()
    {
        // 初回のみ自動適用（Play 中なら Edit モードに戻ってから）
        if (EditorPrefs.GetBool(AppliedKey, false))
            return;

        EditorApplication.delayCall += TryAutoApply;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                TryAutoApply();
        };
    }

    private static void TryAutoApply()
    {
        if (EditorPrefs.GetBool(AppliedKey, false) || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (Apply())
            EditorPrefs.SetBool(AppliedKey, true);
    }

    [MenuItem("Tools/VR Room/移動・旋回の設定を適用")]
    private static void ApplyFromMenu()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[LocomotionSetup] Play モードを停止してから実行してください。");
            return;
        }

        Apply();
    }

    private static bool Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogWarning($"[LocomotionSetup] {ScenePath} を開いてから Tools > VR Room > 移動・旋回の設定を適用 を実行してください。");
            return false;
        }

        var references = AssetDatabase.LoadAllAssetsAtPath(ActionsPath).OfType<InputActionReference>().ToArray();
        var leftMove = FindReference(references, "XRI Left Locomotion", "Move");
        var rightTurn = FindReference(references, "XRI Right Locomotion", "Turn");
        if (leftMove == null || rightTurn == null)
        {
            Debug.LogError("[LocomotionSetup] XRI Default Input Actions から Move / Turn アクションが見つかりません。");
            return false;
        }

        var move = Object.FindAnyObjectByType<ContinuousMoveProvider>(FindObjectsInactive.Include);
        var turn = Object.FindAnyObjectByType<ContinuousTurnProvider>(FindObjectsInactive.Include);
        var snap = Object.FindAnyObjectByType<SnapTurnProvider>(FindObjectsInactive.Include);
        if (move == null || turn == null)
        {
            Debug.LogError("[LocomotionSetup] シーンに ContinuousMoveProvider または ContinuousTurnProvider がありません。");
            return false;
        }

        Undo.RecordObject(move, "Locomotion Setup");
        move.enabled = true;
        SetInput(move.leftHandMoveInput, leftMove);
        SetInput(move.rightHandMoveInput, null);
        EditorUtility.SetDirty(move);

        Undo.RecordObject(turn, "Locomotion Setup");
        turn.enabled = true;
        SetInput(turn.leftHandTurnInput, null);
        SetInput(turn.rightHandTurnInput, rightTurn);
        EditorUtility.SetDirty(turn);

        // Snap Turn は右スティックの旋回と競合するので無効にする
        if (snap != null)
        {
            Undo.RecordObject(snap, "Locomotion Setup");
            snap.enabled = false;
            EditorUtility.SetDirty(snap);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[LocomotionSetup] 適用しました: 左スティック=移動 / 右スティック=なめらかな旋回 / Snap Turn=無効");
        return true;
    }

    private static InputActionReference FindReference(InputActionReference[] references, string mapName, string actionName)
    {
        return references.FirstOrDefault(r => r.action != null && r.action.actionMap.name == mapName && r.action.name == actionName);
    }

    private static void SetInput(XRInputValueReader<Vector2> reader, InputActionReference reference)
    {
        reader.inputSourceMode = XRInputValueReader.InputSourceMode.InputActionReference;
        reader.inputActionReference = reference;
    }
}
