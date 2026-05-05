using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 波次管理器：
/// - 依序執行 Inspector 設定的固定波次
/// - 每波開始前倒數
/// - 生成該波怪物
/// - 等待怪物全滅後觸發波次完成事件
/// - 等待外部呼叫 ResumeNextWave() 後進入下一波
/// </summary>
public class WaveManager : MonoBehaviour
{
    [System.Serializable]
    public class WaveData
    {
        public string waveName;
        public int electricBallCount;
        public int turretCount;
        public int airVentCount;
        public int laserPillarCount;
        [Tooltip("怪物生成間隔（秒）")]
        public float spawnInterval = 0.5f;
    }

    [Header("怪物 Prefab")]
    [SerializeField] private GameObject electricBallPrefab;
    [SerializeField] private GameObject turretPrefab;
    [SerializeField] private GameObject airVentPrefab;
    [SerializeField] private GameObject laserPillarPrefab;

    [Header("生成設定")]
    [SerializeField] private float arenaSize = 60f;
    [SerializeField] private float spawnHeightMin = 5f;
    [SerializeField] private float spawnHeightMax = 55f;
    [SerializeField] private float spawnMargin = 2f;
    [SerializeField] private int maxSpawnPositionAttempts = 24;
    [SerializeField] private float spawnDistancePadding = 0.5f;

    [Header("波次設定")]
    [SerializeField] private float wavePrepTime = 3f;
    [SerializeField] private float monsterCheckInterval = 0.5f;
    [SerializeField] private WaveData[] waves;

    [Header("事件")]
    [SerializeField] private UnityEvent onWaveStart;
    [SerializeField] private UnityEvent<int> onWaveCountdown;
    [SerializeField] private UnityEvent onWaveComplete;
    [SerializeField] private UnityEvent onAllWavesComplete;

    private readonly List<MonsterBase> _activeMonsters = new List<MonsterBase>();
    private readonly Dictionary<GameObject, float> _prefabRadiusCache = new Dictionary<GameObject, float>();
    private Coroutine _waveRoutine;
    private bool _waitingForNextWave;

    /// <summary>目前波次（從 1 開始；尚未開始為 0）。</summary>
    public int CurrentWave { get; private set; }

    /// <summary>總波數。</summary>
    public int TotalWaves => waves != null ? waves.Length : 0;

    /// <summary>是否正在跑波次流程。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// 開始波次流程（外部呼叫）。
    /// 若已在執行中則忽略。
    /// </summary>
    private void Start()
    {
        StartWaves();
    }

    public void StartWaves()
    {
        if (IsRunning)
            return;

        if (waves == null || waves.Length == 0)
        {
            Debug.LogWarning("[WaveManager] 未設定 waves，無法開始波次。");
            return;
        }

        IsRunning = true;
        CurrentWave = 0;
        _waitingForNextWave = false;
        _waveRoutine = StartCoroutine(WaveLoopRoutine());
    }

    /// <summary>
    /// 能力選擇完成後呼叫，允許進入下一波。
    /// </summary>
    public void ResumeNextWave()
    {
        _waitingForNextWave = false;
    }

    private IEnumerator WaveLoopRoutine()
    {
        for (int i = 0; i < waves.Length; i++)
        {
            CurrentWave = i + 1;
            WaveData wave = waves[i];

            yield return RunWaveCountdown();

            onWaveStart?.Invoke();

            yield return SpawnWaveMonsters(wave);

            yield return WaitUntilWaveCleared();

            onWaveComplete?.Invoke();

            _waitingForNextWave = true;
            yield return new WaitUntil(() => !_waitingForNextWave);
        }

        onAllWavesComplete?.Invoke();

        IsRunning = false;
        CurrentWave = 0;
        _waveRoutine = null;
    }

    private IEnumerator RunWaveCountdown()
    {
        int total = Mathf.Max(0, Mathf.CeilToInt(wavePrepTime));
        for (int remaining = total; remaining > 0; remaining--)
        {
            onWaveCountdown?.Invoke(remaining);
            yield return new WaitForSeconds(1f);
        }
    }

    private IEnumerator SpawnWaveMonsters(WaveData wave)
    {
        if (wave == null)
            yield break;

        float interval = Mathf.Max(0f, wave.spawnInterval);

        yield return SpawnMany(electricBallPrefab, wave.electricBallCount, interval);
        yield return SpawnMany(turretPrefab, wave.turretCount, interval);
        yield return SpawnMany(airVentPrefab, wave.airVentCount, interval);
        yield return SpawnMany(laserPillarPrefab, wave.laserPillarCount, interval);
    }

    private IEnumerator SpawnMany(GameObject prefab, int count, float interval)
    {
        if (prefab == null || count <= 0)
            yield break;

        for (int i = 0; i < count; i++)
        {
            SpawnSingle(prefab);
            if (interval > 0f && i < count - 1)
                yield return new WaitForSeconds(interval);
        }
    }

    private void SpawnSingle(GameObject prefab)
    {
        Vector3 spawnPos = FindSpawnPosition(prefab);
        GameObject instance = Instantiate(prefab, spawnPos, Quaternion.identity);
        MonsterBase monster = instance.GetComponent<MonsterBase>();
        UpdateRadiusCacheFromInstance(prefab, monster);
        if (monster != null)
            _activeMonsters.Add(monster);
    }

    /// <summary>
    /// 以實例真實半徑更新 Prefab 快取，避免 Prefab 階段半徑低估。
    /// </summary>
    private void UpdateRadiusCacheFromInstance(GameObject prefab, MonsterBase spawnedMonster)
    {
        if (prefab == null || spawnedMonster == null)
            return;

        float instanceRadius = GetMonsterSpawnRadius(spawnedMonster);
        if (instanceRadius <= 0.01f)
            return;

        if (!_prefabRadiusCache.TryGetValue(prefab, out float cached) || instanceRadius > cached)
        {
            _prefabRadiusCache[prefab] = instanceRadius;
        }
    }

    /// <summary>
    /// 尋找不會與既有怪物重疊的生成點。
    /// 若多次嘗試後仍失敗，回傳最後一次候選點。
    /// </summary>
    private Vector3 FindSpawnPosition(GameObject prefab)
    {
        int attempts = Mathf.Max(1, maxSpawnPositionAttempts);
        float newRadius = GetPrefabSpawnRadius(prefab);
        Vector3 lastCandidate = RandomArenaPosition();

        CleanupDeadMonsters();

        for (int i = 0; i < attempts; i++)
        {
            Vector3 candidate = (i == attempts - 1) ? lastCandidate : RandomArenaPosition();
            if (IsSpawnPositionValid(candidate, newRadius))
                return candidate;
            lastCandidate = candidate;
        }

        return lastCandidate;
    }

    private bool IsSpawnPositionValid(Vector3 candidate, float newRadius)
    {
        float newR = Mathf.Max(0.1f, newRadius);
        float padding = Mathf.Max(0f, spawnDistancePadding);

        for (int i = 0; i < _activeMonsters.Count; i++)
        {
            MonsterBase active = _activeMonsters[i];
            if (active == null || active.gameObject == null || active.IsDead || !active.gameObject.activeInHierarchy)
                continue;

            float existingRadius = GetMonsterSpawnRadius(active);
            float minDistance = newR + existingRadius + padding;
            float actualDistance = Vector3.Distance(active.transform.position, candidate);
            if (actualDistance < minDistance)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 取得 Prefab 用於生成間距的「近似半徑」。
    /// ElectricBall 會包含外層球碰撞器（若有）在內。
    /// </summary>
    private float GetPrefabSpawnRadius(GameObject prefab)
    {
        if (prefab == null)
            return 0.5f;
        if (_prefabRadiusCache.TryGetValue(prefab, out float cached))
            return cached;

        float radius = ComputeRadiusFromColliders(prefab.GetComponentsInChildren<Collider>(true), prefab.transform.position);
        // 電擊球以外圈半徑為優先，避免多顆生成時互相重疊。
        ElectricBallMonster electricBall = prefab.GetComponent<ElectricBallMonster>();
        float electricShellRadius = -1f;
        if (electricBall != null)
        {
            electricShellRadius = electricBall.OuterShellRadius;
            radius = Mathf.Max(radius, electricShellRadius);
        }
        if (radius <= 0.01f)
            radius = 0.5f;
        _prefabRadiusCache[prefab] = radius;
        return radius;
    }

    private float GetMonsterSpawnRadius(MonsterBase monster)
    {
        if (monster == null)
            return 0.5f;

        Collider[] colliders = monster.GetComponentsInChildren<Collider>(true);
        float radius = ComputeRadiusFromColliders(colliders, monster.transform.position);
        if (monster is ElectricBallMonster electricBall)
            radius = Mathf.Max(radius, electricBall.OuterShellRadius);
        return radius > 0.01f ? radius : 0.5f;
    }

    private static float ComputeRadiusFromColliders(Collider[] colliders, Vector3 center)
    {
        float maxRadius = 0f;
        if (colliders == null || colliders.Length == 0)
            return maxRadius;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider c = colliders[i];
            if (c == null)
                continue;

            Bounds b = c.bounds;
            Vector2 centerXZ = new Vector2(center.x, center.z);
            Vector2 boundsCenterXZ = new Vector2(b.center.x, b.center.z);
            float offset = Vector2.Distance(centerXZ, boundsCenterXZ);
            float extentXZ = Mathf.Max(b.extents.x, b.extents.z);
            float radius = offset + extentXZ;
            if (radius > maxRadius)
                maxRadius = radius;
        }

        return maxRadius;
    }

    private IEnumerator WaitUntilWaveCleared()
    {
        float interval = Mathf.Max(0.05f, monsterCheckInterval);
        while (true)
        {
            CleanupDeadMonsters();
            if (_activeMonsters.Count == 0)
                break;

            yield return new WaitForSeconds(interval);
        }
    }

    /// <summary>
    /// 清理已死亡或已不存在的怪物引用。
    /// </summary>
    private void CleanupDeadMonsters()
    {
        for (int i = _activeMonsters.Count - 1; i >= 0; i--)
        {
            MonsterBase m = _activeMonsters[i];
            if (m == null || m.gameObject == null || m.IsDead || !m.gameObject.activeInHierarchy)
                _activeMonsters.RemoveAt(i);
        }
    }

    /// <summary>
    /// 從六面牆隨機挑一面生成。
    /// 以本物件位置作為競技場中心，生成點貼近牆面（預留 spawnMargin）。
    /// </summary>
    private Vector3 RandomArenaPosition()
    {
        float half = Mathf.Max(1f, arenaSize * 0.5f);
        float m = Mathf.Clamp(spawnMargin, 0f, half - 0.1f);
        float edge = half - m;

        Vector3 center = transform.position;
        float minX = center.x - edge;
        float maxX = center.x + edge;
        float minY = center.y - edge;
        float maxY = center.y + edge;
        float minZ = center.z - edge;
        float maxZ = center.z + edge;

        // 高度限制（只對 Y 隨機軸生效）。
        float hMin = Mathf.Clamp(spawnHeightMin, minY, maxY);
        float hMax = Mathf.Clamp(spawnHeightMax, minY, maxY);
        if (hMax < hMin)
        {
            float t = hMin;
            hMin = hMax;
            hMax = t;
        }

        int face = UnityEngine.Random.Range(0, 6);
        switch (face)
        {
            case 0: // +X
                return new Vector3(maxX, UnityEngine.Random.Range(hMin, hMax), UnityEngine.Random.Range(minZ, maxZ));
            case 1: // -X
                return new Vector3(minX, UnityEngine.Random.Range(hMin, hMax), UnityEngine.Random.Range(minZ, maxZ));
            case 2: // +Y
                return new Vector3(UnityEngine.Random.Range(minX, maxX), maxY, UnityEngine.Random.Range(minZ, maxZ));
            case 3: // -Y
                return new Vector3(UnityEngine.Random.Range(minX, maxX), minY, UnityEngine.Random.Range(minZ, maxZ));
            case 4: // +Z
                return new Vector3(UnityEngine.Random.Range(minX, maxX), UnityEngine.Random.Range(hMin, hMax), maxZ);
            default: // -Z
                return new Vector3(UnityEngine.Random.Range(minX, maxX), UnityEngine.Random.Range(hMin, hMax), minZ);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        arenaSize = Mathf.Max(1f, arenaSize);
        spawnMargin = Mathf.Max(0f, spawnMargin);
        maxSpawnPositionAttempts = Mathf.Max(1, maxSpawnPositionAttempts);
        spawnDistancePadding = Mathf.Max(0f, spawnDistancePadding);
        wavePrepTime = Mathf.Max(0f, wavePrepTime);
        monsterCheckInterval = Mathf.Max(0.05f, monsterCheckInterval);

        if (waves == null)
            return;

        for (int i = 0; i < waves.Length; i++)
        {
            if (waves[i] == null)
                continue;
            waves[i].spawnInterval = Mathf.Max(0f, waves[i].spawnInterval);
            waves[i].electricBallCount = Mathf.Max(0, waves[i].electricBallCount);
            waves[i].turretCount = Mathf.Max(0, waves[i].turretCount);
            waves[i].airVentCount = Mathf.Max(0, waves[i].airVentCount);
            waves[i].laserPillarCount = Mathf.Max(0, waves[i].laserPillarCount);
        }
    }
#endif
}
