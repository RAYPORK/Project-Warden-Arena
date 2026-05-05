using UnityEngine;

/// <summary>
/// 全域快捷鍵：按 R 一鍵重生所有已死亡敵人。
/// </summary>
public class MonsterRespawnHotkey : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        GameObject host = new GameObject("[MonsterRespawnHotkey]");
        DontDestroyOnLoad(host);
        host.hideFlags = HideFlags.HideAndDontSave;
        host.AddComponent<MonsterRespawnHotkey>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
            MonsterBase.ReviveAllDeadMonsters();
    }
}
