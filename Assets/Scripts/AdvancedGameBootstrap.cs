using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class AdvancedGameBootstrap : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartGame()
    {
        if (FindFirstObjectByType<AdvancedGameWorld>() != null)
        {
            return;
        }

        GameObject root = new GameObject("Infinite Shooter");
        root.AddComponent<AdvancedGameWorld>();
    }
}

internal sealed class AdvancedGameWorld : MonoBehaviour
{
    public static AdvancedGameWorld Instance { get; private set; }

    private const float SecondsPerWave = 30f;
    private const float EnvironmentChunkSize = 28f;
    private const int EnvironmentChunkRadius = 2;
    private const bool PermanentUpgradesEnabled = false;
    private const string PointsKey = "InfiniteShooter.MetaPoints";
    private const string UpgradeKeyPrefix = "InfiniteShooter.Upgrade.";

    private readonly List<AdvancedEnemyController> enemies = new List<AdvancedEnemyController>();
    private readonly List<AdvancedUpgradeOption> levelChoices = new List<AdvancedUpgradeOption>();
    private readonly List<AdvancedPermanentUpgrade> permanentUpgrades = new List<AdvancedPermanentUpgrade>();
    private readonly List<string> acquiredUpgrades = new List<string>();
    private readonly List<Rect> solidRects = new List<Rect>();
    private readonly Dictionary<Vector2Int, Transform> environmentChunks = new Dictionary<Vector2Int, Transform>();
    private readonly Dictionary<string, int> upgradeStacks = new Dictionary<string, int>();

    private Transform sceneryRoot;
    private Transform enemyRoot;
    private Transform projectileRoot;
    private Transform pickupRoot;
    private Transform effectRoot;
    private AdvancedCameraFollow cameraFollow;
    private AdvancedPlayerController player;
    private AdvancedGameState state;
    private AdvancedGameState upgradeReturnState;
    private AdvancedPlayerClass selectedClass = AdvancedPlayerClass.Archer;

    private float nextWaveTimer;
    private float spawnTimer;
    private float survivedSeconds;
    private float noticeTimer;
    private int pendingSpawns;
    private int wave;
    private int level;
    private int xp;
    private int xpToNextLevel;
    private int kills;
    private int bossKills;
    private int metaPoints;
    private int runPointsEarned;
    private string noticeText = "";

    private GUIStyle hudStyle;
    private GUIStyle smallStyle;
    private GUIStyle centeredSmallStyle;
    private GUIStyle titleStyle;
    private GUIStyle buttonStyle;
    private GUIStyle menuButtonStyle;
    private GUIStyle optionButtonStyle;
    private GUIStyle panelHeaderStyle;
    private GUIStyle bigNumberStyle;
    private Vector2 statsScroll;
    private Vector2 permanentScroll;
    private AdvancedGameState statsReturnState;

    public AdvancedPlayerController Player
    {
        get { return player; }
    }

    public bool IsGameplayPaused
    {
        get { return state != AdvancedGameState.Playing; }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildPermanentUpgrades();
        LoadProfile();
        ShowMainMenu();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        Time.timeScale = 1f;
    }

    private void BuildPermanentUpgrades()
    {
        permanentUpgrades.Clear();
        if (PermanentUpgradesEnabled == false)
        {
            return;
        }

        permanentUpgrades.Add(new AdvancedPermanentUpgrade("hull", "Fortified Hull", "+10 max HP per level.", 8, 2, 2, p => p.IncreaseMaxHealth(10f)));
        permanentUpgrades.Add(new AdvancedPermanentUpgrade("rounds", "Sharper Rounds", "+8% damage per level.", 10, 2, 2, p => p.Damage *= 1.08f));
        permanentUpgrades.Add(new AdvancedPermanentUpgrade("boots", "Runner Boots", "+5% movement speed per level.", 6, 2, 3, p => p.MoveSpeed *= 1.05f));
        permanentUpgrades.Add(new AdvancedPermanentUpgrade("magnet", "Magnetic Core", "+0.8 XP magnet range per level.", 8, 2, 2, p => p.MagnetRange += 0.8f));
        permanentUpgrades.Add(new AdvancedPermanentUpgrade("trigger", "Quick Trigger", "+4% fire rate per level.", 7, 3, 3, p => p.FireDelay = Mathf.Max(0.07f, p.FireDelay * 0.96f)));
        permanentUpgrades.Add(new AdvancedPermanentUpgrade("pierce", "Starting Pierce", "+1 bullet pierce per level.", 3, 5, 5, p => p.PierceCount++));
        permanentUpgrades.Add(new AdvancedPermanentUpgrade("scanner", "Salvage Scanner", "More upgrade point drops.", 5, 3, 3, p => p.SalvageBonus += 0.02f));
    }

    private void LoadProfile()
    {
        metaPoints = PlayerPrefs.GetInt(PointsKey, 0);
        for (int i = 0; i < permanentUpgrades.Count; i++)
        {
            AdvancedPermanentUpgrade upgrade = permanentUpgrades[i];
            upgrade.Level = Mathf.Clamp(PlayerPrefs.GetInt(UpgradeKeyPrefix + upgrade.Id, 0), 0, upgrade.MaxLevel);
        }
    }

    private void SaveProfile()
    {
        PlayerPrefs.SetInt(PointsKey, metaPoints);
        for (int i = 0; i < permanentUpgrades.Count; i++)
        {
            AdvancedPermanentUpgrade upgrade = permanentUpgrades[i];
            PlayerPrefs.SetInt(UpgradeKeyPrefix + upgrade.Id, upgrade.Level);
        }

        PlayerPrefs.Save();
    }

    private void ResetRunStats()
    {
        enemies.Clear();
        levelChoices.Clear();
        acquiredUpgrades.Clear();
        upgradeStacks.Clear();
        solidRects.Clear();
        environmentChunks.Clear();
        wave = 0;
        level = 1;
        xp = 0;
        xpToNextLevel = 35;
        nextWaveTimer = 0f;
        spawnTimer = 0f;
        pendingSpawns = 0;
        survivedSeconds = 0f;
        kills = 0;
        bossKills = 0;
        runPointsEarned = 0;
        player = null;
        noticeText = "";
        noticeTimer = 0f;
    }

    private void ClearWorld()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Destroy(transform.GetChild(i).gameObject);
        }

        enemies.Clear();
    }

    private void CreateRoots()
    {
        sceneryRoot = CreateRoot("Scenery");
        enemyRoot = CreateRoot("Enemies");
        projectileRoot = CreateRoot("Projectiles");
        pickupRoot = CreateRoot("Pickups");
        effectRoot = CreateRoot("Effects");
    }

    private Transform CreateRoot(string name)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(transform);
        return root.transform;
    }

    private void ShowMainMenu()
    {
        Time.timeScale = 0f;
        state = AdvancedGameState.MainMenu;
        upgradeReturnState = AdvancedGameState.MainMenu;
        ResetRunStats();
        ClearWorld();
        CreateRoots();
        CreateCamera();
    }

    private void StartRun()
    {
        Time.timeScale = 1f;
        state = AdvancedGameState.Playing;
        ResetRunStats();
        ClearWorld();
        CreateRoots();
        CreateArena();
        CreatePlayer();
        CreateCamera();
        StartNextWave();
        ShowNotice("Wave 1 incoming");
    }

    private void CreateArena()
    {
        environmentChunks.Clear();
        RefreshEnvironmentChunks(Vector2.zero, true);
    }

    private void RefreshEnvironmentChunks(Vector2 focus, bool force)
    {
        int centerX = Mathf.FloorToInt(focus.x / EnvironmentChunkSize);
        int centerY = Mathf.FloorToInt(focus.y / EnvironmentChunkSize);

        for (int x = centerX - EnvironmentChunkRadius; x <= centerX + EnvironmentChunkRadius; x++)
        {
            for (int y = centerY - EnvironmentChunkRadius; y <= centerY + EnvironmentChunkRadius; y++)
            {
                Vector2Int chunk = new Vector2Int(x, y);
                if (force || environmentChunks.ContainsKey(chunk) == false)
                {
                    CreateEnvironmentChunk(chunk);
                }
            }
        }
    }

    private void CreateEnvironmentChunk(Vector2Int chunk)
    {
        if (environmentChunks.ContainsKey(chunk))
        {
            return;
        }

        GameObject chunkObject = new GameObject("Environment Chunk " + chunk.x + "," + chunk.y);
        chunkObject.transform.SetParent(sceneryRoot);
        Transform chunkRoot = chunkObject.transform;
        environmentChunks[chunk] = chunkRoot;

        Vector2 center = ChunkWorldCenter(chunk);
        const int tilesPerAxis = 7;
        const float tileSpacing = 4f;
        for (int x = 0; x < tilesPerAxis; x++)
        {
            for (int y = 0; y < tilesPerAxis; y++)
            {
                Vector2 tileCenter = center + new Vector2((x - 3) * tileSpacing, (y - 3) * tileSpacing);
                GameObject tile = new GameObject("Floor Tile");
                tile.transform.SetParent(chunkRoot);
                tile.transform.position = new Vector3(tileCenter.x, tileCenter.y, 0.02f);
                tile.transform.localScale = new Vector3(4.08f, 4.08f, 1f);
                SpriteRenderer renderer = tile.AddComponent<SpriteRenderer>();
                renderer.sprite = AdvancedGameArt.FloorSprite(chunk.x * 113 + chunk.y * 37 + x * 11 + y * 17);
                renderer.sortingOrder = -45;
            }
        }

        CreateChunkObstacles(chunk, chunkRoot, center);
        CreateChunkFloorDetails(chunk, chunkRoot, center);
    }

    private void CreateChunkObstacles(Vector2Int chunk, Transform chunkRoot, Vector2 center)
    {
        bool originChunk = chunk == Vector2Int.zero;
        int obstacleCount = originChunk ? 5 : 7;
        for (int i = 0; i < obstacleCount; i++)
        {
            float roll = ChunkRandom(chunk, i * 19 + 3);
            Vector2 position = center + new Vector2(ChunkRandom(chunk, i * 23 + 5) * 22f - 11f, ChunkRandom(chunk, i * 29 + 7) * 22f - 11f);
            if (originChunk && position.sqrMagnitude < 42f)
            {
                continue;
            }

            if (IsCircleBlocked(position, 1.2f))
            {
                continue;
            }

            if (roll < 0.32f)
            {
                Vector2 size = new Vector2(Mathf.Lerp(4.2f, 7.8f, ChunkRandom(chunk, i * 31 + 11)), Mathf.Lerp(3.8f, 6.4f, ChunkRandom(chunk, i * 37 + 13)));
                CreateBuilding("Chunk House", position, size, Mathf.Abs(chunk.x * 5 + chunk.y * 7 + i), chunkRoot);
            }
            else if (roll < 0.68f)
            {
                bool horizontal = ChunkRandom(chunk, i * 41 + 17) > 0.45f;
                Vector2 size = horizontal
                    ? new Vector2(Mathf.Lerp(4.5f, 9.5f, ChunkRandom(chunk, i * 43 + 19)), 0.85f)
                    : new Vector2(0.85f, Mathf.Lerp(4.5f, 9.5f, ChunkRandom(chunk, i * 47 + 23)));
                CreateWall("Chunk Wall", position, size, chunkRoot, Mathf.Abs(chunk.x * 13 + chunk.y * 17 + i));
            }
            else
            {
                CreateTree("Chunk Tree", position, Mathf.Lerp(0.95f, 1.55f, ChunkRandom(chunk, i * 53 + 29)), Mathf.Abs(chunk.x + chunk.y + i), chunkRoot);
            }
        }
    }

    private void CreateChunkFloorDetails(Vector2Int chunk, Transform chunkRoot, Vector2 center)
    {
        for (int i = 0; i < 10; i++)
        {
            Vector2 position = center + new Vector2(ChunkRandom(chunk, i * 59 + 31) * 25f - 12.5f, ChunkRandom(chunk, i * 61 + 33) * 25f - 12.5f);
            if (IsCircleBlocked(position, 0.35f))
            {
                continue;
            }

            GameObject decal = new GameObject("Floor Detail");
            decal.transform.SetParent(chunkRoot);
            decal.transform.position = new Vector3(position.x, position.y, -0.04f);
            float scale = Mathf.Lerp(0.35f, 0.9f, ChunkRandom(chunk, i * 67 + 35));
            decal.transform.localScale = new Vector3(scale, scale, 1f);
            decal.transform.rotation = Quaternion.Euler(0f, 0f, ChunkRandom(chunk, i * 71 + 37) * 360f);
            SpriteRenderer renderer = decal.AddComponent<SpriteRenderer>();
            renderer.sprite = AdvancedGameArt.DecorSprite(chunk.x * 97 + chunk.y * 89 + i);
            renderer.sortingOrder = -18;
        }
    }

    private Vector2 ChunkWorldCenter(Vector2Int chunk)
    {
        return new Vector2(chunk.x * EnvironmentChunkSize, chunk.y * EnvironmentChunkSize);
    }

    private float ChunkRandom(Vector2Int chunk, int salt)
    {
        return Mathf.Repeat(Mathf.Sin(chunk.x * 127.1f + chunk.y * 311.7f + salt * 74.7f) * 43758.5453f, 1f);
    }

    private void CreateLine(string name, Sprite sprite, Vector3 position, Vector3 scale)
    {
        GameObject line = new GameObject(name);
        line.transform.SetParent(sceneryRoot);
        line.transform.position = position;
        line.transform.localScale = scale;
        SpriteRenderer renderer = line.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = -40;
    }

    private void CreateCityLayout()
    {
        CreateBuilding("Apartment Block", new Vector2(-15f, 10f), new Vector2(7f, 5f), 0);
        CreateBuilding("Market Hall", new Vector2(12f, 13f), new Vector2(8f, 4.8f), 1);
        CreateBuilding("Workshop", new Vector2(-18f, -12f), new Vector2(6.2f, 6.2f), 2);
        CreateBuilding("Storage House", new Vector2(16f, -10f), new Vector2(7.5f, 5.2f), 3);
        CreateBuilding("Small House", new Vector2(-5f, 20f), new Vector2(4.5f, 4.2f), 4);
        CreateBuilding("Small House", new Vector2(3f, -22f), new Vector2(4.8f, 4.4f), 5);

        CreateWall("North Perimeter", new Vector2(0f, 31f), new Vector2(54f, 1.1f));
        CreateWall("South Perimeter", new Vector2(0f, -31f), new Vector2(54f, 1.1f));
        CreateWall("West Perimeter", new Vector2(-31f, 0f), new Vector2(1.1f, 54f));
        CreateWall("East Perimeter", new Vector2(31f, 0f), new Vector2(1.1f, 54f));

        CreateWall("Broken Wall A", new Vector2(-3f, 8f), new Vector2(8f, 0.8f));
        CreateWall("Broken Wall B", new Vector2(9f, -3f), new Vector2(0.8f, 8f));
        CreateWall("Broken Wall C", new Vector2(-10f, -2f), new Vector2(0.8f, 6f));
        CreateWall("Broken Wall D", new Vector2(4f, 8f), new Vector2(4f, 0.8f));

        CreateTree("Oak Cluster", new Vector2(-24f, 20f), 1.35f, 0);
        CreateTree("Pine Cluster", new Vector2(23f, 20f), 1.3f, 1);
        CreateTree("Red Tree", new Vector2(-24f, -21f), 1.25f, 2);
        CreateTree("Dead Tree", new Vector2(25f, -22f), 1.2f, 3);
        CreateTree("Center Grove A", new Vector2(-1.8f, -14f), 1.1f, 4);
        CreateTree("Center Grove B", new Vector2(8f, 20f), 1.05f, 5);
    }

    private void CreateFloorDetails()
    {
        for (int i = 0; i < 55; i++)
        {
            Vector2 position = new Vector2(PseudoRandom(i, 61) * 54f - 27f, PseudoRandom(i, 67) * 54f - 27f);
            if (position.sqrMagnitude < 18f || IsCircleBlocked(position, 0.45f))
            {
                continue;
            }

            GameObject decal = new GameObject("Floor Scuff");
            decal.transform.SetParent(sceneryRoot);
            decal.transform.position = new Vector3(position.x, position.y, -0.04f);
            float scale = Mathf.Lerp(0.35f, 0.9f, PseudoRandom(i, 71));
            decal.transform.localScale = new Vector3(scale, scale, 1f);
            decal.transform.rotation = Quaternion.Euler(0f, 0f, PseudoRandom(i, 73) * 360f);
            SpriteRenderer renderer = decal.AddComponent<SpriteRenderer>();
            renderer.sprite = AdvancedGameArt.DecorSprite(i);
            renderer.sortingOrder = -18;
        }
    }

    private void CreateBuilding(string name, Vector2 center, Vector2 size, int variant)
    {
        CreateBuilding(name, center, size, variant, sceneryRoot);
    }

    private void CreateBuilding(string name, Vector2 center, Vector2 size, int variant, Transform parent)
    {
        GameObject building = CreateSolidObject(name, center, size, AdvancedGameArt.BuildingSprite(variant), -10, parent);
        building.transform.localScale = new Vector3(size.x, size.y, 1f);
    }

    private void CreateWall(string name, Vector2 center, Vector2 size)
    {
        CreateWall(name, center, size, sceneryRoot, 0);
    }

    private void CreateWall(string name, Vector2 center, Vector2 size, Transform parent, int variant)
    {
        GameObject wall = CreateSolidObject(name, center, size, AdvancedGameArt.WallSprite(variant), -8, parent);
        wall.transform.localScale = new Vector3(size.x, size.y, 1f);
    }

    private void CreateTree(string name, Vector2 center, float size, int variant)
    {
        CreateTree(name, center, size, variant, sceneryRoot);
    }

    private void CreateTree(string name, Vector2 center, float size, int variant, Transform parent)
    {
        GameObject tree = CreateSolidObject(name, center, new Vector2(size * 0.82f, size * 0.82f), AdvancedGameArt.TreeSprite(variant), -7, parent, false);
        tree.transform.localScale = new Vector3(size, size, 1f);
    }

    private GameObject CreateSolidObject(string name, Vector2 center, Vector2 size, Sprite sprite, int sortingOrder)
    {
        return CreateSolidObject(name, center, size, sprite, sortingOrder, sceneryRoot);
    }

    private GameObject CreateSolidObject(string name, Vector2 center, Vector2 size, Sprite sprite, int sortingOrder, Transform parent, bool showFootprint = true)
    {
        GameObject solid = new GameObject(name);
        solid.transform.SetParent(parent);
        solid.transform.position = new Vector3(center.x, center.y, -0.05f);

        SpriteRenderer renderer = solid.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = sortingOrder;

        if (showFootprint)
        {
            GameObject footprint = new GameObject("Hitbox Footprint");
            footprint.transform.SetParent(solid.transform);
            footprint.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            footprint.transform.localScale = Vector3.one * 1.035f;
            SpriteRenderer footprintRenderer = footprint.AddComponent<SpriteRenderer>();
            footprintRenderer.sprite = AdvancedGameArt.FootprintSprite();
            footprintRenderer.sortingOrder = sortingOrder + 1;
        }

        BoxCollider2D collider = solid.AddComponent<BoxCollider2D>();
        collider.size = Vector2.one;
        collider.isTrigger = false;
        solid.AddComponent<AdvancedSolidObstacle>();

        solidRects.Add(new Rect(center.x - size.x * 0.5f, center.y - size.y * 0.5f, size.x, size.y));
        return solid;
    }

    public Vector2 ResolveMovement(Vector2 currentPosition, Vector2 delta, float radius)
    {
        Vector2 result = currentPosition;
        Vector2 xStep = new Vector2(delta.x, 0f);
        if (IsCircleBlocked(result + xStep, radius) == false)
        {
            result += xStep;
        }

        Vector2 yStep = new Vector2(0f, delta.y);
        if (IsCircleBlocked(result + yStep, radius) == false)
        {
            result += yStep;
        }

        return result;
    }

    public bool IsCircleBlocked(Vector2 center, float radius)
    {
        for (int i = 0; i < solidRects.Count; i++)
        {
            if (CircleOverlapsRect(center, radius, solidRects[i]))
            {
                return true;
            }
        }

        return false;
    }

    public Vector2 FindOpenDirection(Vector2 currentPosition, Vector2 desiredDirection, float radius)
    {
        if (desiredDirection.sqrMagnitude < 0.001f)
        {
            return Vector2.zero;
        }

        Vector2 desired = desiredDirection.normalized;
        const float probeDistance = 0.62f;
        if (IsCircleBlocked(currentPosition + desired * probeDistance, radius) == false)
        {
            return desired;
        }

        float[] angles = { 24f, -24f, 48f, -48f, 78f, -78f, 112f, -112f, 150f, -150f };
        Vector2 best = Vector2.zero;
        float bestScore = -999f;
        for (int i = 0; i < angles.Length; i++)
        {
            Vector2 candidate = AdvancedGameMath.Rotate(desired, angles[i]).normalized;
            if (IsCircleBlocked(currentPosition + candidate * probeDistance, radius))
            {
                continue;
            }

            float score = Vector2.Dot(candidate, desired) - Mathf.Abs(angles[i]) * 0.0025f;
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best == Vector2.zero ? desired : best;
    }

    private bool CircleOverlapsRect(Vector2 center, float radius, Rect rect)
    {
        float closestX = Mathf.Clamp(center.x, rect.xMin, rect.xMax);
        float closestY = Mathf.Clamp(center.y, rect.yMin, rect.yMax);
        float dx = center.x - closestX;
        float dy = center.y - closestY;
        return dx * dx + dy * dy < radius * radius;
    }

    private float PseudoRandom(int index, int salt)
    {
        return Mathf.Repeat(Mathf.Sin(index * 12.9898f + salt * 78.233f) * 43758.5453f, 1f);
    }

    private void CreatePlayer()
    {
        GameObject playerObject = new GameObject("Player");
        playerObject.transform.SetParent(transform);
        playerObject.transform.position = Vector3.zero;

        SpriteRenderer renderer = playerObject.AddComponent<SpriteRenderer>();
        renderer.sprite = AdvancedGameArt.HeroSprite(selectedClass, AdvancedHeroAnimation.Idle, 0);
        renderer.sortingOrder = 10;

        CircleCollider2D collider = playerObject.AddComponent<CircleCollider2D>();
        collider.radius = 0.46f;
        collider.isTrigger = true;

        player = playerObject.AddComponent<AdvancedPlayerController>();
        player.Initialize(this, selectedClass);
        ApplyPermanentBonuses(player);
    }

    private void ApplyPermanentBonuses(AdvancedPlayerController target)
    {
        if (PermanentUpgradesEnabled == false)
        {
            return;
        }

        for (int i = 0; i < permanentUpgrades.Count; i++)
        {
            AdvancedPermanentUpgrade upgrade = permanentUpgrades[i];
            for (int levelIndex = 0; levelIndex < upgrade.Level; levelIndex++)
            {
                upgrade.ApplyOneLevel(target);
            }
        }
    }

    private void CreateCamera()
    {
        Camera[] oldCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        for (int i = 0; i < oldCameras.Length; i++)
        {
            if (oldCameras[i].gameObject.transform.IsChildOf(transform) == false)
            {
                oldCameras[i].gameObject.tag = "Untagged";
                Destroy(oldCameras[i].gameObject);
            }
        }

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.transform.SetParent(transform);
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 7.2f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.045f, 0.05f, 0.065f);

        cameraFollow = cameraObject.AddComponent<AdvancedCameraFollow>();
        cameraFollow.Target = player != null ? player.transform : null;
    }

    private void Update()
    {
        HandleGlobalInput();

        if (noticeTimer > 0f)
        {
            noticeTimer -= Time.unscaledDeltaTime;
        }

        if (state != AdvancedGameState.Playing)
        {
            return;
        }

        survivedSeconds += Time.deltaTime;
        nextWaveTimer -= Time.deltaTime;
        spawnTimer -= Time.deltaTime;
        if (player != null)
        {
            RefreshEnvironmentChunks(player.transform.position, false);
        }

        if (nextWaveTimer <= 0f)
        {
            StartNextWave();
        }

        if (pendingSpawns > 0 && spawnTimer <= 0f)
        {
            pendingSpawns--;
            spawnTimer = Mathf.Max(0.09f, 0.42f - wave * 0.012f);
            SpawnEnemy(ChooseEnemyKind());
        }
    }

    private void HandleGlobalInput()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (state == AdvancedGameState.Playing)
            {
                PauseGame();
            }
            else if (state == AdvancedGameState.Paused)
            {
                ResumeGame();
            }
            else if (state == AdvancedGameState.PermanentUpgrades)
            {
                ClosePermanentUpgrades();
            }
            else if (state == AdvancedGameState.Stats)
            {
                CloseStats();
            }
        }

        if (Input.GetKeyDown(KeyCode.Tab) && (state == AdvancedGameState.Playing || state == AdvancedGameState.Paused))
        {
            OpenStats(state);
        }

        if (PermanentUpgradesEnabled && Input.GetKeyDown(KeyCode.U) && (state == AdvancedGameState.Playing || state == AdvancedGameState.Paused))
        {
            OpenPermanentUpgrades(state);
        }
    }

    private void PauseGame()
    {
        state = AdvancedGameState.Paused;
        Time.timeScale = 0f;
    }

    private void ResumeGame()
    {
        state = AdvancedGameState.Playing;
        Time.timeScale = 1f;
    }

    private void OpenPermanentUpgrades(AdvancedGameState returnState)
    {
        if (PermanentUpgradesEnabled == false)
        {
            ShowNotice("Permanent upgrades are disabled for now");
            return;
        }

        upgradeReturnState = returnState;
        state = AdvancedGameState.PermanentUpgrades;
        Time.timeScale = 0f;
    }

    private void ClosePermanentUpgrades()
    {
        state = upgradeReturnState;
        Time.timeScale = state == AdvancedGameState.Playing ? 1f : 0f;
    }

    private void OpenStats(AdvancedGameState returnState)
    {
        statsReturnState = returnState;
        state = AdvancedGameState.Stats;
        Time.timeScale = 0f;
    }

    private void CloseStats()
    {
        state = statsReturnState;
        Time.timeScale = state == AdvancedGameState.Playing ? 1f : 0f;
    }

    private void StartNextWave()
    {
        wave++;
        nextWaveTimer = SecondsPerWave;
        pendingSpawns += 7 + wave * 3;
        ShowNotice("Wave " + wave);

        if (wave % 3 == 0)
        {
            AwardMetaPoints(1, "Wave bonus");
        }

        if (wave % 5 == 0)
        {
            SpawnEnemy(AdvancedEnemyKind.Boss);
            ShowNotice("Boss wave!");
        }
    }

    private void BringNextWaveCloser()
    {
        if (nextWaveTimer > 5f)
        {
            nextWaveTimer = 5f;
            ShowNotice("Next wave in 5 seconds");
        }
    }

    private AdvancedEnemyKind ChooseEnemyKind()
    {
        float roll = UnityEngine.Random.value;

        if (wave <= 1)
        {
            return AdvancedEnemyKind.Melee;
        }

        if (wave == 2)
        {
            return roll < 0.72f ? AdvancedEnemyKind.Melee : AdvancedEnemyKind.Ranged;
        }

        if (wave == 3)
        {
            if (roll < 0.52f) return AdvancedEnemyKind.Melee;
            if (roll < 0.78f) return AdvancedEnemyKind.Ranged;
            return AdvancedEnemyKind.Tank;
        }

        if (roll < 0.34f) return AdvancedEnemyKind.Melee;
        if (roll < 0.56f) return AdvancedEnemyKind.Ranged;
        if (roll < 0.72f) return AdvancedEnemyKind.Tank;
        if (roll < 0.88f) return AdvancedEnemyKind.GlassCannon;
        return AdvancedEnemyKind.Exploder;
    }

    private void SpawnEnemy(AdvancedEnemyKind kind)
    {
        if (player == null)
        {
            return;
        }

        Vector2 offset = UnityEngine.Random.insideUnitCircle.normalized;
        if (offset.sqrMagnitude < 0.1f)
        {
            offset = Vector2.right;
        }

        float distance = kind == AdvancedEnemyKind.Boss ? 15f : UnityEngine.Random.Range(10.5f, 14.5f);
        Vector3 spawnPosition = player.transform.position + (Vector3)(offset * distance);
        for (int attempt = 0; attempt < 10 && IsCircleBlocked(spawnPosition, kind == AdvancedEnemyKind.Boss ? 1.4f : 0.7f); attempt++)
        {
            offset = AdvancedGameMath.Rotate(offset, 41f + attempt * 17f).normalized;
            spawnPosition = player.transform.position + (Vector3)(offset * (distance + attempt * 0.8f));
        }

        GameObject enemyObject = new GameObject(kind + " Enemy");
        enemyObject.transform.SetParent(enemyRoot);
        enemyObject.transform.position = spawnPosition;

        AdvancedEnemyController enemy = enemyObject.AddComponent<AdvancedEnemyController>();
        enemy.Initialize(this, kind, wave);
        enemies.Add(enemy);
    }

    public void SpawnPlayerBullet(Vector2 origin, Vector2 direction, float damage, float speed, int pierce, float sizeMultiplier, AdvancedProjectileKind projectileKind = AdvancedProjectileKind.Bullet, float maxDistance = 0f, float explosionRadius = 0f)
    {
        Color color = projectileKind == AdvancedProjectileKind.Magic ? new Color(0.78f, 0.42f, 1f) : new Color(0.9f, 0.82f, 0.55f);
        float baseSize = projectileKind == AdvancedProjectileKind.Arrow ? 0.34f : projectileKind == AdvancedProjectileKind.Magic ? 0.25f : 0.2f;
        SpawnBullet("Player Bullet", true, origin, direction, damage, speed, pierce, color, baseSize * sizeMultiplier, projectileKind, maxDistance, explosionRadius);
    }

    public void SpawnEnemyBullet(Vector2 origin, Vector2 direction, float damage, float speed)
    {
        SpawnBullet("Enemy Bullet", false, origin, direction, damage, speed, 0, new Color(1f, 0.28f, 0.55f), 0.22f, AdvancedProjectileKind.Bullet, 0f, 0f);
    }

    public void SpawnPlayerMine(Vector2 position, float damage, float radius, bool usePlayerAttackEffects = false)
    {
        GameObject mineObject = new GameObject("Player Mine");
        mineObject.transform.SetParent(projectileRoot);
        mineObject.transform.position = position;
        mineObject.transform.localScale = Vector3.one * 0.62f;

        SpriteRenderer renderer = mineObject.AddComponent<SpriteRenderer>();
        renderer.sprite = AdvancedGameArt.MineSprite();
        renderer.sortingOrder = 6;

        CircleCollider2D collider = mineObject.AddComponent<CircleCollider2D>();
        collider.radius = 0.62f;
        collider.isTrigger = true;

        AdvancedMine mine = mineObject.AddComponent<AdvancedMine>();
        mine.Initialize(this, damage, radius, usePlayerAttackEffects);
    }

    private void SpawnBullet(string name, bool fromPlayer, Vector2 origin, Vector2 direction, float damage, float speed, int pierce, Color color, float size, AdvancedProjectileKind projectileKind, float maxDistance, float explosionRadius)
    {
        if (direction.sqrMagnitude < 0.01f)
        {
            return;
        }

        GameObject bulletObject = new GameObject(name);
        bulletObject.transform.SetParent(projectileRoot);
        bulletObject.transform.position = origin;
        bulletObject.transform.localScale = Vector3.one * size;

        SpriteRenderer renderer = bulletObject.AddComponent<SpriteRenderer>();
        renderer.sprite = fromPlayer ? AdvancedGameArt.PlayerProjectileSprite(projectileKind) : AdvancedGameArt.EnemyBulletSprite();
        renderer.color = color;
        renderer.sortingOrder = fromPlayer ? 12 : 8;

        CircleCollider2D collider = bulletObject.AddComponent<CircleCollider2D>();
        collider.radius = 0.5f;
        collider.isTrigger = true;

        Rigidbody2D body = bulletObject.AddComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        AdvancedBullet bullet = bulletObject.AddComponent<AdvancedBullet>();
        bullet.Initialize(fromPlayer, direction.normalized, damage, speed, pierce, projectileKind, maxDistance, explosionRadius);
    }

    public void PerformPlayerMelee(Vector2 origin, Vector2 direction, float range, float arcDegrees, float damage)
    {
        bool hitSomething = false;
        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            AdvancedEnemyController enemy = enemies[i];
            if (enemy == null)
            {
                continue;
            }

            Vector2 toEnemy = (Vector2)enemy.transform.position - origin;
            if (toEnemy.magnitude > range)
            {
                continue;
            }

            if (Vector2.Angle(direction, toEnemy.normalized) > arcDegrees * 0.5f)
            {
                continue;
            }

            bool critical = false;
            float finalDamage = player != null ? player.RollAttackDamage(damage, enemy, out critical) : damage;
            enemy.TakeDamage(finalDamage, critical);
            if (player != null)
            {
                player.ApplyOnHitEffects(enemy, enemy.transform.position, finalDamage);
            }
            hitSomething = true;
        }

        SpawnBurst(origin + direction * Mathf.Min(range, 1.4f), new Color(0.86f, 0.92f, 1f), hitSomething ? 10 : 4);
        if (cameraFollow != null)
        {
            cameraFollow.Shake(hitSomething ? 0.16f : 0.06f);
        }
    }

    public void DamageEnemiesInRadius(Vector2 origin, float radius, float damage, bool usePlayerAttackEffects = false)
    {
        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            AdvancedEnemyController enemy = enemies[i];
            if (enemy == null)
            {
                continue;
            }

            if (Vector2.Distance(origin, enemy.transform.position) <= radius)
            {
                bool critical = false;
                float finalDamage = usePlayerAttackEffects && player != null ? player.RollAttackDamage(damage, enemy, out critical) : damage;
                enemy.TakeDamage(finalDamage, critical);
                if (usePlayerAttackEffects && player != null)
                {
                    player.ApplyOnHitEffects(enemy, enemy.transform.position, finalDamage, false);
                }
            }
        }

        SpawnBurst(origin, new Color(1f, 0.48f, 0.16f), 16);
        if (cameraFollow != null)
        {
            cameraFollow.Shake(0.18f);
        }
    }

    public void StrikeLightning(Vector2 origin, float damage, float range, int jumps)
    {
        List<AdvancedEnemyController> hitEnemies = new List<AdvancedEnemyController>();
        Vector2 source = origin;
        int arcs = Mathf.Max(1, jumps + 1);
        for (int i = 0; i < arcs; i++)
        {
            AdvancedEnemyController target = FindNearestEnemy(source, range, hitEnemies);
            if (target == null)
            {
                break;
            }

            hitEnemies.Add(target);
            bool critical = false;
            float finalDamage = player != null ? player.RollAttackDamage(damage, target, out critical) : damage;
            target.TakeDamage(finalDamage, critical);
            if (player != null)
            {
                player.ApplyOnHitEffects(target, target.transform.position, finalDamage);
            }

            SpawnLightningArc(source, target.transform.position);
            source = target.transform.position;
        }

        if (hitEnemies.Count > 0 && cameraFollow != null)
        {
            cameraFollow.Shake(0.1f + hitEnemies.Count * 0.025f);
        }
    }

    private AdvancedEnemyController FindNearestEnemy(Vector2 origin, float range, List<AdvancedEnemyController> excluded)
    {
        AdvancedEnemyController best = null;
        float bestDistance = range * range;
        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            AdvancedEnemyController enemy = enemies[i];
            if (enemy == null || enemy.IsAlive == false || excluded.Contains(enemy))
            {
                continue;
            }

            float distance = ((Vector2)enemy.transform.position - origin).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = enemy;
            }
        }

        return best;
    }

    private void SpawnLightningArc(Vector2 from, Vector2 to)
    {
        if (effectRoot == null)
        {
            return;
        }

        GameObject arc = new GameObject("Lightning Arc");
        arc.transform.SetParent(effectRoot);
        LineRenderer line = arc.AddComponent<LineRenderer>();
        line.positionCount = 3;
        line.useWorldSpace = true;
        Vector2 midpoint = (from + to) * 0.5f + UnityEngine.Random.insideUnitCircle * 0.18f;
        line.SetPosition(0, from);
        line.SetPosition(1, midpoint);
        line.SetPosition(2, to);
        line.startWidth = 0.08f;
        line.endWidth = 0.03f;
        line.sortingOrder = 18;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            line.material = new Material(shader);
        }

        Color color = new Color(0.58f, 0.92f, 1f, 1f);
        line.startColor = color;
        line.endColor = Color.white;
        AdvancedLightningArc lightningArc = arc.AddComponent<AdvancedLightningArc>();
        lightningArc.Initialize(line, color);
    }

    public void EnemyKilled(AdvancedEnemyController enemy, int xpReward, Vector3 position, bool isBoss)
    {
        enemies.Remove(enemy);
        kills++;
        SpawnXpPickup(position, xpReward);
        SpawnBurst(position, isBoss ? new Color(1f, 0.28f, 0.65f) : enemy.VisualColor, isBoss ? 20 : 8);

        if (player != null && enemy != null)
        {
            if (player.WildfireTargets > 0 && enemy.HasBurn)
            {
                SpreadBurn(position, player.WildfireTargets, enemy.BurnDurationRemaining, enemy.BurnDamagePerSecond);
            }

            if (player.Contagion && enemy.HasPoison)
            {
                PoisonBurst(position);
            }
        }

        float pointChance = 0.045f + (player != null ? player.SalvageBonus : 0f);
        if (isBoss)
        {
            bossKills++;
            AwardMetaPoints(6 + wave / 5, "Boss defeated");
            int dropCount = player != null ? Mathf.Max(1, player.BossHealthDropCount) : 1;
            float bossHealAmount = player != null && player.FieldRationsHealPercent > 0f ? Mathf.Max(35f, player.MaxHealth * player.FieldRationsHealPercent) : 35f;
            for (int i = 0; i < dropCount; i++)
            {
                SpawnHealthPickup(position, bossHealAmount);
            }
            cameraFollow.Shake(0.45f);
        }
        else if (UnityEngine.Random.value < pointChance)
        {
            AwardMetaPoints(1, "Salvage found");
        }

        float healthDropChance = 0.055f + (player != null ? player.FieldRationsDropChance : 0f);
        if (enemy != null && enemy.IsEliteOrBoss && player != null)
        {
            healthDropChance = Mathf.Max(healthDropChance, player.FieldRationsEliteDropChance);
        }

        if (isBoss == false && UnityEngine.Random.value < healthDropChance)
        {
            float healAmount = player != null && player.FieldRationsHealPercent > 0f ? Mathf.Max(18f, player.MaxHealth * player.FieldRationsHealPercent) : 18f;
            SpawnHealthPickup(position, healAmount);
        }
    }

    private void SpreadBurn(Vector3 position, int targetCount, float duration, float damagePerSecond)
    {
        int applied = 0;
        for (int i = enemies.Count - 1; i >= 0 && applied < targetCount; i--)
        {
            AdvancedEnemyController target = enemies[i];
            if (target == null || target.IsAlive == false)
            {
                continue;
            }

            if (Vector2.Distance(position, target.transform.position) > 3f)
            {
                continue;
            }

            target.ApplyStatus(AdvancedStatusEffect.Burn, Mathf.Max(2f, duration), Mathf.Max(1f, damagePerSecond), 1f);
            applied++;
        }
    }

    private void PoisonBurst(Vector3 position)
    {
        DamageEnemiesInRadius(position, 2.4f, player != null ? player.Damage * 0.2f : 8f, false);
        if (player == null)
        {
            return;
        }

        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            AdvancedEnemyController target = enemies[i];
            if (target != null && target.IsAlive && Vector2.Distance(position, target.transform.position) <= 2.4f)
            {
                target.ApplyStatus(AdvancedStatusEffect.Poison, player.PoisonDuration, player.PoisonDamagePerSecond, 1f);
            }
        }
    }

    public void EnemyExploded(AdvancedEnemyController enemy, Vector3 position)
    {
        enemies.Remove(enemy);
        SpawnBurst(position, new Color(1f, 0.5f, 0.12f), 12);
        if (cameraFollow != null)
        {
            cameraFollow.Shake(0.18f);
        }
    }

    private void SpawnXpPickup(Vector3 position, int amount)
    {
        GameObject pickup = new GameObject("XP Pickup");
        pickup.transform.SetParent(pickupRoot);
        pickup.transform.position = position;
        pickup.transform.localScale = Vector3.one * Mathf.Lerp(0.45f, 0.8f, Mathf.Clamp01(amount / 45f));

        SpriteRenderer renderer = pickup.AddComponent<SpriteRenderer>();
        renderer.sprite = AdvancedGameArt.XpSprite();
        renderer.sortingOrder = 7;

        AdvancedXpPickup xpPickup = pickup.AddComponent<AdvancedXpPickup>();
        xpPickup.Initialize(this, amount);
    }

    private void SpawnHealthPickup(Vector3 position, float amount)
    {
        GameObject pickup = new GameObject("Health Pickup");
        pickup.transform.SetParent(pickupRoot);
        pickup.transform.position = position + (Vector3)(UnityEngine.Random.insideUnitCircle * 0.5f);
        pickup.transform.localScale = Vector3.one * 0.55f;

        SpriteRenderer renderer = pickup.AddComponent<SpriteRenderer>();
        renderer.sprite = AdvancedGameArt.HealthSprite();
        renderer.sortingOrder = 7;

        AdvancedHealthPickup healthPickup = pickup.AddComponent<AdvancedHealthPickup>();
        healthPickup.Initialize(this, amount);
    }

    public void AddXp(int amount)
    {
        if (player != null)
        {
            amount = Mathf.Max(1, Mathf.RoundToInt(amount * player.ExperienceMultiplier));
        }

        xp += amount;
        if (player != null)
        {
            CreateFloatingText("+" + amount + " xp", player.transform.position + Vector3.up * 0.9f, new Color(0.45f, 0.85f, 1f));
        }

        TryLevelUp();
    }

    private void AwardMetaPoints(int amount, string reason)
    {
        metaPoints += amount;
        runPointsEarned += amount;
        SaveProfile();
        ShowNotice(reason + ": +" + amount + " point" + (amount == 1 ? "" : "s"));
    }

    private void TryLevelUp()
    {
        if (state != AdvancedGameState.Playing || xp < xpToNextLevel)
        {
            return;
        }

        xp -= xpToNextLevel;
        level++;
        xpToNextLevel = Mathf.RoundToInt(xpToNextLevel * 1.16f + 12f);
        state = AdvancedGameState.LevelUp;
        Time.timeScale = 0f;
        CreateLevelChoices();
    }

    private void CreateLevelChoices()
    {
        levelChoices.Clear();
        List<AdvancedUpgradeOption> pool = CreateUpgradePool();
        int optionCount = player != null ? Mathf.Clamp(player.LevelUpChoiceCount, 3, 7) : 3;
        for (int i = 0; i < optionCount && pool.Count > 0; i++)
        {
            int index = ChooseWeightedUpgradeIndex(pool);
            AdvancedUpgradeOption selected = pool[index];
            levelChoices.Add(selected);
            pool.RemoveAt(index);
            if (string.IsNullOrEmpty(selected.Family) == false)
            {
                pool.RemoveAll(option => option.Family == selected.Family);
            }
        }
    }

    private int ChooseWeightedUpgradeIndex(List<AdvancedUpgradeOption> pool)
    {
        float totalWeight = 0f;
        for (int i = 0; i < pool.Count; i++)
        {
            totalWeight += UpgradeWeight(pool[i]);
        }

        float roll = UnityEngine.Random.value * totalWeight;
        for (int i = 0; i < pool.Count; i++)
        {
            roll -= UpgradeWeight(pool[i]);
            if (roll <= 0f)
            {
                return i;
            }
        }

        return pool.Count - 1;
    }

    private float UpgradeWeight(AdvancedUpgradeOption option)
    {
        float weight = option.Weight;
        if (option.Rarity != AdvancedUpgradeRarity.Common && player != null)
        {
            weight *= 1f + Mathf.Clamp(player.Luck, 0f, 3f);
        }

        return weight;
    }

    private void AddUpgradeOption(List<AdvancedUpgradeOption> pool, AdvancedUpgradeOption option, int maxStacks = 0, bool allowed = true)
    {
        if (allowed == false)
        {
            return;
        }

        if (maxStacks > 0 && GetUpgradeStack(option.Title) >= maxStacks)
        {
            return;
        }

        pool.Add(option);
    }

    private int GetUpgradeStack(string title)
    {
        int count;
        return upgradeStacks.TryGetValue(title, out count) ? count : 0;
    }

    private bool HasUpgrade(string title)
    {
        return GetUpgradeStack(title) > 0;
    }

    private List<AdvancedUpgradeOption> CreateUpgradePool()
    {
        List<AdvancedUpgradeOption> pool = new List<AdvancedUpgradeOption>();
        AdvancedPlayerClass heroClass = selectedClass;

        AddUpgradeOption(pool, new AdvancedUpgradeOption("Honed Weapon", "+18% direct attack damage.", AdvancedUpgradeRarity.Common, p => p.Damage *= 1.18f, "damage"));
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Faster Fire", "Attack interval -15%. Min 0.15s.", AdvancedUpgradeRarity.Common, p => p.FireDelay = Mathf.Max(0.15f, p.FireDelay * 0.85f), "fire_rate"));
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Move Speed", "+10% movement speed.", AdvancedUpgradeRarity.Common, p => p.MoveSpeed *= 1.1f, "move"));
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Max Health", "+10% max HP and full heal.", AdvancedUpgradeRarity.Common, p => p.IncreaseMaxHealthPercent(0.1f), "health"));
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Fast Rounds", "+20% projectile speed.", AdvancedUpgradeRarity.Common, p => p.BulletSpeed *= 1.2f, "projectile_speed"), 0, heroClass != AdvancedPlayerClass.Knight);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Piercing", "Arrows hit 1 extra enemy.", AdvancedUpgradeRarity.Common, p => p.PierceCount++, "pierce"), 0, heroClass == AdvancedPlayerClass.Archer);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Regen", "Heal 0.5% max HP per second.", AdvancedUpgradeRarity.Common, p => p.HealthRegen += p.MaxHealth * 0.005f, "regen"));
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Magnet", "XP pickup radius x1.35.", AdvancedUpgradeRarity.Common, p => p.MagnetRange *= 1.35f, "magnet"), 5);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Plated Vest", "+2 flat armor.", AdvancedUpgradeRarity.Common, p => p.FlatArmor += 2f, "armor"));
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Student of Battle", "+12% XP gained.", AdvancedUpgradeRarity.Common, p => p.ExperienceMultiplier += 0.12f, "xp_gain"));
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Lucky Find", "+15% Luck for rarity rolls.", AdvancedUpgradeRarity.Common, p => p.AddLuck(0.15f), "luck"), 5);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Expanded Selection I", "Future level-ups show 4 options.", AdvancedUpgradeRarity.Common, p => p.LevelUpChoiceCount = Mathf.Max(p.LevelUpChoiceCount, 4), "choices"), 1, player == null || player.LevelUpChoiceCount < 4);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Field Rations", "Enemies can drop stronger heals.", AdvancedUpgradeRarity.Common, p => p.ImproveFieldRations(), "supplies"));

        AddUpgradeOption(pool, new AdvancedUpgradeOption("Tempered Weapon", "+35% direct attack damage.", AdvancedUpgradeRarity.Uncommon, p => p.Damage *= 1.35f, "damage"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Quickened Strikes", "Attack interval -25%.", AdvancedUpgradeRarity.Uncommon, p => p.FireDelay = Mathf.Max(0.15f, p.FireDelay * 0.75f), "fire_rate"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Fleet Footwork", "+20% movement speed.", AdvancedUpgradeRarity.Uncommon, p => p.MoveSpeed *= 1.2f, "move"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Greater Vitality", "+25% max HP and full heal.", AdvancedUpgradeRarity.Uncommon, p => p.IncreaseMaxHealthPercent(0.25f), "health"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Accelerated Rounds", "+45% projectile speed.", AdvancedUpgradeRarity.Uncommon, p => p.BulletSpeed *= 1.45f, "projectile_speed"), 1, heroClass != AdvancedPlayerClass.Knight);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Deep Piercing", "Arrows pierce 2 extra enemies.", AdvancedUpgradeRarity.Uncommon, p => p.PierceCount += 2, "pierce"), 1, heroClass == AdvancedPlayerClass.Archer);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Rapid Recovery", "Heal 1.2% max HP per second.", AdvancedUpgradeRarity.Uncommon, p => p.HealthRegen += p.MaxHealth * 0.012f, "regen"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Greater Magnet", "XP pickup radius x1.75.", AdvancedUpgradeRarity.Uncommon, p => p.MagnetRange *= 1.75f, "magnet"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Heavy Plate", "+5 flat armor.", AdvancedUpgradeRarity.Uncommon, p => p.FlatArmor += 5f, "armor"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Veteran of Battle", "+30% XP gained.", AdvancedUpgradeRarity.Uncommon, p => p.ExperienceMultiplier += 0.3f, "xp_gain"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Fortunate Find", "+35% Luck for rarity rolls.", AdvancedUpgradeRarity.Uncommon, p => p.AddLuck(0.35f), "luck"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Expanded Selection II", "Future level-ups show 5 options.", AdvancedUpgradeRarity.Uncommon, p => p.LevelUpChoiceCount = Mathf.Max(p.LevelUpChoiceCount, 5), "choices"), 1, player != null && player.LevelUpChoiceCount == 4);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Combat Supplies", "10% heal drops for 18% max HP.", AdvancedUpgradeRarity.Uncommon, p => p.SetSupplyTier(0.1f, 0.4f, 0.18f, 1), "supplies"), 1);

        AddUpgradeOption(pool, new AdvancedUpgradeOption("Mine Layer", "Drop a mine every 2.5s behind you.", AdvancedUpgradeRarity.Uncommon, p => p.EnableMines()), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Keen Eye", "+10 percentage points crit chance.", AdvancedUpgradeRarity.Uncommon, p => p.CritChance = Mathf.Min(0.75f, p.CritChance + 0.1f)));
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Dash Charge", "Dash cooldown -20%. Min 1s.", AdvancedUpgradeRarity.Uncommon, p => p.DashCooldown = Mathf.Max(1f, p.DashCooldown * 0.8f)));
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Big Projectiles", "+20% attack size and +10% damage.", AdvancedUpgradeRarity.Uncommon, p => p.GrowBullets()));
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Executioner", "+30% damage to enemies below 25% HP.", AdvancedUpgradeRarity.Uncommon, p => p.ExecutionerBonus += p.ExecutionerBonus <= 0f ? 0.3f : 0.15f));
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Giant Slayer", "+25% damage to elite enemies and bosses.", AdvancedUpgradeRarity.Uncommon, p => p.GiantSlayerBonus += p.GiantSlayerBonus <= 0f ? 0.25f : 0.15f));
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Vampiric Edge", "Heal for 1% of direct damage dealt.", AdvancedUpgradeRarity.Uncommon, p => p.VampiricPercent += p.VampiricPercent <= 0f ? 0.01f : 0.005f));
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Shockwave", "Every 10s, pulse around you for 30% damage.", AdvancedUpgradeRarity.Uncommon, p => p.ImproveShockwave()), 6);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Berserker", "Missing HP grants up to +35% damage.", AdvancedUpgradeRarity.Uncommon, p => p.ImproveBerserker()), 2);

        if (player == null || player.BurnDamagePerSecond <= 0f)
        {
            AddUpgradeOption(pool, new AdvancedUpgradeOption("Burning Edge", "Every direct hit applies short Burn.", AdvancedUpgradeRarity.Uncommon, p => p.AddBurn()), 1);
        }
        else
        {
            AddUpgradeOption(pool, new AdvancedUpgradeOption("Hotter Flames", "+4% hit damage per second as Burn.", AdvancedUpgradeRarity.Uncommon, p => p.AddBurnDamage()), 5);
            AddUpgradeOption(pool, new AdvancedUpgradeOption("Long Burn", "+2s Burn duration.", AdvancedUpgradeRarity.Uncommon, p => p.AddBurnDuration()), 5);
            AddUpgradeOption(pool, new AdvancedUpgradeOption("Wildfire", "Burn can spread when enemies die.", AdvancedUpgradeRarity.Epic, p => p.WildfireTargets++), 3);
        }

        if (player == null || player.PoisonDamagePerSecond <= 0f)
        {
            AddUpgradeOption(pool, new AdvancedUpgradeOption("Poison Vials", "Every direct hit applies short Poison.", AdvancedUpgradeRarity.Uncommon, p => p.AddPoison()), 1);
        }
        else
        {
            AddUpgradeOption(pool, new AdvancedUpgradeOption("Stronger Toxin", "+1% max HP poison damage per second.", AdvancedUpgradeRarity.Uncommon, p => p.AddPoisonDamage()), 5);
            AddUpgradeOption(pool, new AdvancedUpgradeOption("Lingering Poison", "+2s Poison duration.", AdvancedUpgradeRarity.Uncommon, p => p.AddPoisonDuration()), 5);
            AddUpgradeOption(pool, new AdvancedUpgradeOption("Contagion", "Poisoned kills burst poison nearby.", AdvancedUpgradeRarity.Epic, p => p.Contagion = true), 1);
        }

        if (player == null || player.FreezeDuration <= 0f)
        {
            AddUpgradeOption(pool, new AdvancedUpgradeOption("Frost Oil", "Every direct hit applies a short slow.", AdvancedUpgradeRarity.Uncommon, p => p.AddFreeze()), 1);
        }
        else
        {
            AddUpgradeOption(pool, new AdvancedUpgradeOption("Deep Chill", "+7.5 percentage points slow.", AdvancedUpgradeRarity.Uncommon, p => p.AddFreezeStrength()), 5);
            AddUpgradeOption(pool, new AdvancedUpgradeOption("Long Freeze", "+2s slow duration.", AdvancedUpgradeRarity.Uncommon, p => p.AddFreezeDuration()), 5);
            AddUpgradeOption(pool, new AdvancedUpgradeOption("Brittle", "Frosted enemies take more crit damage.", AdvancedUpgradeRarity.Epic, p => p.BrittleCritDamageBonus += p.BrittleCritDamageBonus <= 0f ? 0.15f : 0.1f));
        }

        bool hasAnyElement = player != null && (player.BurnDamagePerSecond > 0f || player.PoisonDamagePerSecond > 0f || player.FreezeDuration > 0f);
        bool hasBurnAndPoison = player != null && player.BurnDamagePerSecond > 0f && player.PoisonDamagePerSecond > 0f;
        bool hasBurnAndFreeze = player != null && player.BurnDamagePerSecond > 0f && player.FreezeDuration > 0f;
        bool hasMineAndElement = player != null && player.MineInterval > 0f && hasAnyElement;
        bool hasAllElements = player != null && player.BurnDamagePerSecond > 0f && player.PoisonDamagePerSecond > 0f && player.FreezeDuration > 0f;

        AddUpgradeOption(pool, new AdvancedUpgradeOption("Masterwork Weapon", "+70% direct attack damage.", AdvancedUpgradeRarity.Epic, p => p.Damage *= 1.7f, "damage"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Relentless Assault", "Attack interval -40%.", AdvancedUpgradeRarity.Epic, p => p.FireDelay = Mathf.Max(0.15f, p.FireDelay * 0.6f), "fire_rate"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Windrunner", "+35% movement speed.", AdvancedUpgradeRarity.Epic, p => p.MoveSpeed *= 1.35f, "move"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Titan's Vitality", "+60% max HP and full heal.", AdvancedUpgradeRarity.Epic, p => p.IncreaseMaxHealthPercent(0.6f), "health"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Hypervelocity", "+90% projectile speed.", AdvancedUpgradeRarity.Epic, p => p.BulletSpeed *= 1.9f, "projectile_speed"), 1, heroClass != AdvancedPlayerClass.Knight);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Phasing Shots", "Arrows pierce 4 extra enemies.", AdvancedUpgradeRarity.Epic, p => p.PierceCount += 4, "pierce"), 1, heroClass == AdvancedPlayerClass.Archer);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Regenerative Core", "Heal 2.5% max HP per second.", AdvancedUpgradeRarity.Epic, p => p.HealthRegen += p.MaxHealth * 0.025f, "regen"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Gravity Well", "XP radius x2.5 and pickup speed +50%.", AdvancedUpgradeRarity.Epic, p => { p.MagnetRange *= 2.5f; p.XpPickupSpeedMultiplier *= 1.5f; }, "magnet"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Fortress Plate", "+10 flat armor.", AdvancedUpgradeRarity.Epic, p => p.FlatArmor += 10f, "armor"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Master of Battle", "+60% XP gained.", AdvancedUpgradeRarity.Epic, p => p.ExperienceMultiplier += 0.6f, "xp_gain"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Fate's Favor", "+75% Luck for rarity rolls.", AdvancedUpgradeRarity.Epic, p => p.AddLuck(0.75f), "luck"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Expanded Selection III", "Future level-ups show 6 options.", AdvancedUpgradeRarity.Epic, p => p.LevelUpChoiceCount = Mathf.Max(p.LevelUpChoiceCount, 6), "choices"), 1, player != null && player.LevelUpChoiceCount == 5);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Medical Cache", "18% heal drops for 30% max HP.", AdvancedUpgradeRarity.Epic, p => p.SetSupplyTier(0.18f, 1f, 0.3f, 2), "supplies"), 1);

        AddUpgradeOption(pool, new AdvancedUpgradeOption("Lightning Rod", "Every 4s, lightning hits and chains.", AdvancedUpgradeRarity.Epic, p => p.ImproveLightning()), 0);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Explosive Hits", "Direct hits can explode for area damage.", AdvancedUpgradeRarity.Epic, p => p.AddExplosiveHits()), 0);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Critical Force", "+50 percentage points crit damage.", AdvancedUpgradeRarity.Epic, p => p.CritDamageMultiplier += 0.5f));
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Reinforced Armor", "Take 12% less damage before flat armor.", AdvancedUpgradeRarity.Epic, p => p.ArmorPercent = Mathf.Min(0.65f, 1f - ((1f - p.ArmorPercent) * 0.88f))), 5);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Rapid Mines", "Mines drop faster, hit harder, and grow.", AdvancedUpgradeRarity.Epic, p => p.EnableMines(true)), 0, player != null && player.MineInterval > 0f);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Glass Cannon", "+40% damage, but +20% damage taken.", AdvancedUpgradeRarity.Epic, p => p.AddGlassCannon()), 5);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Guardian Angel", "Survive lethal damage once per cooldown.", AdvancedUpgradeRarity.Epic, p => p.EnableGuardianAngel()), 2);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Steam Burst", "Burning frosted enemies bursts steam.", AdvancedUpgradeRarity.Epic, p => p.SteamBurst = true), 1, hasBurnAndFreeze);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Toxic Flame", "Burn and Poison amplify each other.", AdvancedUpgradeRarity.Epic, p => p.AddToxicFlame()), 1, hasBurnAndPoison);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Volatile Mines", "Mines inherit elements and can proc explosions.", AdvancedUpgradeRarity.Epic, p => p.VolatileMines = true), 1, hasMineAndElement);

        AddUpgradeOption(pool, new AdvancedUpgradeOption("Split Arrow", "Fire two side arrows at 45% damage.", AdvancedUpgradeRarity.Epic, p => p.AddSplitArrow()), 3, heroClass == AdvancedPlayerClass.Archer);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Hunter's Mark", "Hits mark enemies to take +20% Archer damage.", AdvancedUpgradeRarity.Epic, p => p.HunterMarkBonus += 0.2f), 1, heroClass == AdvancedPlayerClass.Archer);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Spell Echo", "Spells can repeat at 60% damage.", AdvancedUpgradeRarity.Epic, p => p.AddSpellEcho()), 3, heroClass == AdvancedPlayerClass.Mage);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Arcane Overload", "Every 5th spell is bigger and stronger.", AdvancedUpgradeRarity.Epic, p => p.EnableArcaneOverload()), 1, heroClass == AdvancedPlayerClass.Mage);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Cleave", "Sword swings wider and a little farther.", AdvancedUpgradeRarity.Epic, p => p.AddCleave()), 3, heroClass == AdvancedPlayerClass.Knight);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Counterattack", "After being hit, your next swing retaliates.", AdvancedUpgradeRarity.Epic, p => p.EnableCounterattack()), 2, heroClass == AdvancedPlayerClass.Knight);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Whirlwind", "Every 7s, your next swing is circular.", AdvancedUpgradeRarity.Epic, p => p.EnableWhirlwind()), 1, heroClass == AdvancedPlayerClass.Knight);

        AddUpgradeOption(pool, new AdvancedUpgradeOption("Godforged Weapon", "+140% direct attack damage.", AdvancedUpgradeRarity.Legendary, p => p.Damage *= 2.4f, "damage"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Timebreaker", "Attack interval -60%.", AdvancedUpgradeRarity.Legendary, p => p.FireDelay = Mathf.Max(0.15f, p.FireDelay * 0.4f), "fire_rate"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Unbound Movement", "+60% movement speed.", AdvancedUpgradeRarity.Legendary, p => p.MoveSpeed *= 1.6f, "move"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Colossus Heart", "+120% max HP and full heal.", AdvancedUpgradeRarity.Legendary, p => p.IncreaseMaxHealthPercent(1.2f), "health"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Lightspeed Rounds", "+180% projectile speed.", AdvancedUpgradeRarity.Legendary, p => p.BulletSpeed *= 2.8f, "projectile_speed"), 1, heroClass != AdvancedPlayerClass.Knight);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Infinite Penetration", "Arrows pierce 8 extra enemies.", AdvancedUpgradeRarity.Legendary, p => p.PierceCount += 8, "pierce"), 1, heroClass == AdvancedPlayerClass.Archer);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Undying Body", "Heal 5% max HP per second.", AdvancedUpgradeRarity.Legendary, p => p.HealthRegen += p.MaxHealth * 0.05f, "regen"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Singularity", "XP radius x4 and pickup speed +100%.", AdvancedUpgradeRarity.Legendary, p => { p.MagnetRange *= 4f; p.XpPickupSpeedMultiplier *= 2f; }, "magnet"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Living Fortress", "+20 flat armor.", AdvancedUpgradeRarity.Legendary, p => p.FlatArmor += 20f, "armor"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Legend of Battle", "+120% XP gained.", AdvancedUpgradeRarity.Legendary, p => p.ExperienceMultiplier += 1.2f, "xp_gain"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Chosen by Fortune", "+150% Luck for rarity rolls.", AdvancedUpgradeRarity.Legendary, p => p.AddLuck(1.5f), "luck"), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Expanded Selection IV", "Future level-ups show 7 options.", AdvancedUpgradeRarity.Legendary, p => p.LevelUpChoiceCount = Mathf.Max(p.LevelUpChoiceCount, 7), "choices"), 1, player != null && player.LevelUpChoiceCount == 6);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Divine Provisions", "30% heal drops for 45% max HP.", AdvancedUpgradeRarity.Legendary, p => p.SetSupplyTier(0.3f, 1f, 0.45f, 2), "supplies"), 1);

        AddUpgradeOption(pool, new AdvancedUpgradeOption("Stormcaller", "Lightning becomes faster and chains harder.", AdvancedUpgradeRarity.Legendary, p => p.ImproveLightning(true)), 1, player != null && player.LightningInterval > 0f);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Demolition Expert", "Explosions get 1.5x chance, radius, and damage.", AdvancedUpgradeRarity.Legendary, p => p.AddExplosiveHits(true)), 1, player != null && player.ExplosionChance > 0f);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Assassin's Rhythm", "+15% crit chance and +75% crit damage.", AdvancedUpgradeRarity.Legendary, p => { p.CritChance = Mathf.Min(0.85f, p.CritChance + 0.15f); p.CritDamageMultiplier += 0.75f; }), 3);
        if (hasAnyElement)
        {
            AddUpgradeOption(pool, new AdvancedUpgradeOption("Elemental Arsenal", "Improve all unlocked elements.", AdvancedUpgradeRarity.Legendary, p => { p.AddBurn(true); p.AddPoison(true); p.AddFreeze(true); }), 1);
        }
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Phoenix Heart", "Revive once and blast nearby enemies.", AdvancedUpgradeRarity.Legendary, p => p.EnablePhoenixHeart()), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Worldbreaker", "Direct hits create secondary shockwaves.", AdvancedUpgradeRarity.Legendary, p => p.EnableWorldbreaker()), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Apex Predator", "+35% elite/boss damage and defense.", AdvancedUpgradeRarity.Legendary, p => p.EnableApexPredator()), 1);
        AddUpgradeOption(pool, new AdvancedUpgradeOption("Elemental Tempest", "Periodic storm applies all elements nearby.", AdvancedUpgradeRarity.Legendary, p => p.EnableElementalTempest()), 1, hasAllElements);

        return pool;
    }

    private void ApplyUpgrade(AdvancedUpgradeOption upgrade)
    {
        upgrade.Apply(player);
        upgradeStacks[upgrade.Title] = GetUpgradeStack(upgrade.Title) + 1;
        acquiredUpgrades.Add(upgrade.RarityName + " - " + upgrade.Title);
        state = AdvancedGameState.Playing;
        Time.timeScale = 1f;
        ShowNotice(upgrade.Title + " acquired");
        TryLevelUp();
    }

    private bool TryBuyPermanentUpgrade(AdvancedPermanentUpgrade upgrade)
    {
        if (upgrade.Level >= upgrade.MaxLevel || metaPoints < upgrade.Cost)
        {
            return false;
        }

        metaPoints -= upgrade.Cost;
        upgrade.Level++;
        SaveProfile();
        if (player != null)
        {
            upgrade.ApplyOneLevel(player);
        }

        ShowNotice(upgrade.Title + " upgraded");
        return true;
    }

    public void HealPlayer(float amount)
    {
        if (player != null)
        {
            player.Heal(amount);
        }
    }

    public void DamagePlayer(float amount)
    {
        if (player != null)
        {
            player.TakeDamage(amount);
        }
    }

    public void TriggerGameOver()
    {
        if (state == AdvancedGameState.GameOver)
        {
            return;
        }

        state = AdvancedGameState.GameOver;
        Time.timeScale = 0f;
        SaveProfile();
    }

    public void SpawnBurst(Vector3 position, Color color, int count)
    {
        if (effectRoot == null)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            GameObject shard = new GameObject("Burst Shard");
            shard.transform.SetParent(effectRoot);
            shard.transform.position = position;
            shard.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.08f, 0.18f);

            SpriteRenderer renderer = shard.AddComponent<SpriteRenderer>();
            renderer.sprite = AdvancedGameArt.SquareSprite(color);
            renderer.sortingOrder = 16;

            AdvancedBurstShard burst = shard.AddComponent<AdvancedBurstShard>();
            burst.Initialize(UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(2.4f, 5.5f), UnityEngine.Random.Range(0.3f, 0.7f), color);
        }
    }

    public void CreateFloatingText(string text, Vector3 position, Color color)
    {
        if (effectRoot == null)
        {
            return;
        }

        GameObject textObject = new GameObject("Floating Text");
        textObject.transform.SetParent(effectRoot);
        textObject.transform.position = position;
        TextMesh mesh = textObject.AddComponent<TextMesh>();
        mesh.text = text;
        mesh.fontSize = 42;
        mesh.characterSize = 0.07f;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.color = color;

        AdvancedFloatingText floatingText = textObject.AddComponent<AdvancedFloatingText>();
        floatingText.Initialize(color);
    }

    private void ShowNotice(string text)
    {
        noticeText = text;
        noticeTimer = 2.4f;
    }

    private void OnGUI()
    {
        EnsureGuiStyles();
        if (state == AdvancedGameState.MainMenu)
        {
            DrawMainMenu();
            return;
        }

        DrawHud();
        if (noticeTimer > 0f && string.IsNullOrEmpty(noticeText) == false)
        {
            DrawNotice();
        }

        if (state != AdvancedGameState.Playing)
        {
            DrawDimOverlay();
        }

        if (state == AdvancedGameState.Paused)
        {
            DrawPausePanel();
        }
        else if (state == AdvancedGameState.LevelUp)
        {
            DrawLevelUpPanel();
        }
        else if (state == AdvancedGameState.PermanentUpgrades)
        {
            DrawPermanentUpgradePanel();
        }
        else if (state == AdvancedGameState.Stats)
        {
            DrawStatsPanel();
        }
        else if (state == AdvancedGameState.GameOver)
        {
            DrawGameOverPanel();
        }
    }

    private void EnsureGuiStyles()
    {
        if (hudStyle != null)
        {
            return;
        }

        hudStyle = new GUIStyle(GUI.skin.label);
        hudStyle.fontSize = 23;
        hudStyle.normal.textColor = Color.white;

        smallStyle = new GUIStyle(GUI.skin.label);
        smallStyle.fontSize = 18;
        smallStyle.normal.textColor = new Color(0.82f, 0.88f, 0.95f);
        smallStyle.wordWrap = true;

        centeredSmallStyle = new GUIStyle(smallStyle);
        centeredSmallStyle.alignment = TextAnchor.MiddleCenter;

        titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = 34;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.alignment = TextAnchor.MiddleCenter;
        titleStyle.normal.textColor = Color.white;

        bigNumberStyle = new GUIStyle(titleStyle);
        bigNumberStyle.fontSize = 44;
        bigNumberStyle.normal.textColor = new Color(0.5f, 0.9f, 1f);

        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 19;
        buttonStyle.wordWrap = true;
        buttonStyle.alignment = TextAnchor.MiddleCenter;

        menuButtonStyle = new GUIStyle(buttonStyle);
        menuButtonStyle.fontSize = 20;

        optionButtonStyle = new GUIStyle(buttonStyle);
        optionButtonStyle.fontSize = 17;
        optionButtonStyle.padding = new RectOffset(16, 16, 10, 10);

        panelHeaderStyle = new GUIStyle(hudStyle);
        panelHeaderStyle.fontSize = 24;
        panelHeaderStyle.fontStyle = FontStyle.Bold;
        panelHeaderStyle.normal.textColor = new Color(0.75f, 0.9f, 1f);
    }

    private void DrawDimOverlay()
    {
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), AdvancedGameArt.Pixel(new Color(0f, 0f, 0f, 0.62f)));
    }

    private void DrawMainMenu()
    {
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), AdvancedGameArt.Pixel(new Color(0.035f, 0.045f, 0.055f)));
        Rect panel = CenterRect(760f, 610f);
        GUI.Box(panel, "");
        GUILayout.BeginArea(new Rect(panel.x + 32f, panel.y + 22f, panel.width - 64f, panel.height - 44f));
        GUILayout.Label("Infinite Shooter", titleStyle, GUILayout.Height(48f));
        GUILayout.Label("Top-down survivor prototype", centeredSmallStyle, GUILayout.Height(26f));
        GUILayout.Label("Upgrade points: " + metaPoints, bigNumberStyle, GUILayout.Height(54f));
        GUILayout.Label("Choose your hero", panelHeaderStyle, GUILayout.Height(28f));
        GUILayout.BeginHorizontal();
        DrawClassCard(AdvancedPlayerClass.Archer, "Archer", "Bow shots with pierce.");
        DrawClassCard(AdvancedPlayerClass.Knight, "Knight", "Armored melee fighter.");
        DrawClassCard(AdvancedPlayerClass.Mage, "Mage", "Exploding magic blasts.");
        GUILayout.EndHorizontal();
        GUILayout.Space(10f);

        if (GUILayout.Button("Start Run", menuButtonStyle, GUILayout.Height(52f)))
        {
            StartRun();
        }

        if (PermanentUpgradesEnabled)
        {
            GUILayout.Space(8f);
            if (GUILayout.Button("Permanent Upgrades", menuButtonStyle, GUILayout.Height(48f)))
            {
                OpenPermanentUpgrades(AdvancedGameState.MainMenu);
            }
        }
        else
        {
            GUILayout.Space(6f);
            GUILayout.Label("Permanent upgrades disabled for this test build.", centeredSmallStyle, GUILayout.Height(28f));
        }

        GUILayout.Space(8f);
        if (GUILayout.Button("Quit", menuButtonStyle, GUILayout.Height(44f)))
        {
            Application.Quit();
        }

        GUILayout.Space(10f);
        GUILayout.Label(PermanentUpgradesEnabled
            ? "Controls: WASD move, mouse aim, left click shoot, Space dash, Tab stats, U upgrades, Esc pause."
            : "Controls: WASD move, mouse aim, left click shoot, Space dash, Tab stats, Esc pause.", centeredSmallStyle, GUILayout.Height(54f));
        GUILayout.EndArea();
    }

    private void DrawClassCard(AdvancedPlayerClass heroClass, string title, string description)
    {
        Color oldColor = GUI.backgroundColor;
        GUI.backgroundColor = selectedClass == heroClass ? new Color(0.25f, 0.78f, 1f) : new Color(0.86f, 0.9f, 0.95f);
        string prefix = selectedClass == heroClass ? "SELECTED\n" : "Pick\n";
        if (GUILayout.Button(prefix + title + "\n" + description, optionButtonStyle, GUILayout.Height(92f), GUILayout.Width(206f)))
        {
            selectedClass = heroClass;
        }
        GUI.backgroundColor = oldColor;
    }

    private void DrawHud()
    {
        if (player == null)
        {
            return;
        }

        Rect box = new Rect(18f, 16f, 470f, 252f);
        GUI.Box(box, "");
        DrawBar(new Rect(38f, 38f, 285f, 22f), player.HealthPercent, new Color(0.18f, 0.95f, 0.38f), new Color(0.2f, 0.05f, 0.07f));
        GUI.Label(new Rect(334f, 34f, 130f, 28f), Mathf.CeilToInt(player.Health) + "/" + Mathf.CeilToInt(player.MaxHealth), smallStyle);

        DrawBar(new Rect(38f, 74f, 285f, 22f), xpToNextLevel > 0 ? Mathf.Clamp01((float)xp / xpToNextLevel) : 0f, new Color(0.35f, 0.72f, 1f), new Color(0.05f, 0.1f, 0.2f));
        GUI.Label(new Rect(334f, 70f, 130f, 28f), xp + "/" + xpToNextLevel + " XP", smallStyle);

        DrawBar(new Rect(38f, 110f, 285f, 18f), player.DashFill, new Color(1f, 0.72f, 0.24f), new Color(0.18f, 0.12f, 0.05f));
        GUI.Label(new Rect(334f, 104f, 130f, 28f), "Dash", smallStyle);

        GUI.Label(new Rect(38f, 142f, 400f, 30f), "Level " + level + "   Wave " + wave + "   Kills " + kills, hudStyle);
        GUI.Label(new Rect(38f, 172f, 410f, 26f), "Next wave in " + Mathf.CeilToInt(Mathf.Max(0f, nextWaveTimer)) + "s   Enemies " + enemies.Count, smallStyle);
        GUI.Label(new Rect(38f, 196f, 410f, 26f), "Upgrade points " + metaPoints + " (+" + runPointsEarned + " this run)", smallStyle);

        bool canBringWaveCloser = state == AdvancedGameState.Playing && nextWaveTimer > 5f;
        GUI.enabled = canBringWaveCloser;
        if (GUI.Button(new Rect(38f, 224f, 410f, 30f), canBringWaveCloser ? "Next wave in 5s" : "Wave starts soon", buttonStyle))
        {
            BringNextWaveCloser();
        }
        GUI.enabled = true;
    }

    private void DrawNotice()
    {
        Rect rect = new Rect(Screen.width * 0.5f - 220f, 80f, 440f, 38f);
        GUI.Box(rect, "");
        GUI.Label(rect, noticeText, titleStyle);
    }

    private void DrawBar(Rect rect, float fill, Color fillColor, Color backgroundColor)
    {
        GUI.DrawTexture(rect, AdvancedGameArt.Pixel(backgroundColor));
        Rect fillRect = rect;
        fillRect.width *= Mathf.Clamp01(fill);
        GUI.DrawTexture(fillRect, AdvancedGameArt.Pixel(fillColor));
    }

    private void DrawPausePanel()
    {
        Rect panel = CenterRect(480f, 430f);
        GUI.Box(panel, "");
        GUILayout.BeginArea(new Rect(panel.x + 28f, panel.y + 24f, panel.width - 56f, panel.height - 48f));
        GUILayout.Label("Paused", titleStyle, GUILayout.Height(52f));

        if (GUILayout.Button("Resume", menuButtonStyle, GUILayout.Height(54f))) ResumeGame();
        GUILayout.Space(8f);
        if (GUILayout.Button("Stats / Current Build", menuButtonStyle, GUILayout.Height(54f))) OpenStats(AdvancedGameState.Paused);
        if (PermanentUpgradesEnabled)
        {
            GUILayout.Space(8f);
            if (GUILayout.Button("Permanent Upgrades", menuButtonStyle, GUILayout.Height(54f))) OpenPermanentUpgrades(AdvancedGameState.Paused);
        }
        GUILayout.Space(8f);
        if (GUILayout.Button("Restart Run", menuButtonStyle, GUILayout.Height(54f))) StartRun();
        GUILayout.Space(8f);
        if (GUILayout.Button("Main Menu", menuButtonStyle, GUILayout.Height(54f))) ShowMainMenu();

        GUILayout.EndArea();
    }

    private void DrawLevelUpPanel()
    {
        Rect panel = CenterRect(860f, Mathf.Min(760f, Screen.height - 32f));
        GUI.Box(panel, "");
        GUILayout.BeginArea(new Rect(panel.x + 34f, panel.y + 26f, panel.width - 68f, panel.height - 52f));
        GUILayout.Label("Level up! Choose an upgrade", titleStyle, GUILayout.Height(56f));
        GUILayout.Space(8f);

        float optionSpacing = levelChoices.Count >= 6 ? 6f : 10f;
        float availableHeight = Mathf.Max(260f, panel.height - 130f);
        float optionHeight = levelChoices.Count > 0 ? Mathf.Clamp((availableHeight - optionSpacing * (levelChoices.Count - 1)) / levelChoices.Count, 72f, 116f) : 116f;
        for (int i = 0; i < levelChoices.Count; i++)
        {
            AdvancedUpgradeOption upgrade = levelChoices[i];
            Color oldColor = GUI.backgroundColor;
            GUI.backgroundColor = upgrade.RarityColor;
            if (GUILayout.Button(upgrade.RarityName + " - " + upgrade.Title + "\n" + upgrade.Description, optionButtonStyle, GUILayout.Height(optionHeight)))
            {
                ApplyUpgrade(upgrade);
            }

            GUI.backgroundColor = oldColor;
            GUILayout.Space(optionSpacing);
        }

        GUILayout.EndArea();
    }

    private void DrawPermanentUpgradePanel()
    {
        Rect panel = CenterRect(820f, 620f);
        GUI.Box(panel, "");
        GUILayout.BeginArea(new Rect(panel.x + 28f, panel.y + 22f, panel.width - 56f, panel.height - 44f));
        GUILayout.Label("Permanent Upgrades", titleStyle, GUILayout.Height(48f));
        if (PermanentUpgradesEnabled == false)
        {
            GUILayout.Label("Permanent upgrades are disabled for now.", centeredSmallStyle, GUILayout.Height(70f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(upgradeReturnState == AdvancedGameState.MainMenu ? "Back to Menu" : "Back", menuButtonStyle, GUILayout.Height(52f)))
            {
                ClosePermanentUpgrades();
            }

            GUILayout.EndArea();
            return;
        }

        GUILayout.Label("Spend upgrade points during or between runs. Points: " + metaPoints, centeredSmallStyle, GUILayout.Height(26f));
        GUILayout.Space(10f);

        permanentScroll = GUILayout.BeginScrollView(permanentScroll, false, true, GUILayout.Height(panel.height - 160f));
        for (int i = 0; i < permanentUpgrades.Count; i++)
        {
            AdvancedPermanentUpgrade upgrade = permanentUpgrades[i];
            GUILayout.BeginHorizontal(GUILayout.Height(54f));
            GUILayout.Label(upgrade.Title + "  " + upgrade.Level + "/" + upgrade.MaxLevel + "\n" + upgrade.Description, smallStyle, GUILayout.Width(530f));

            bool canBuy = upgrade.Level < upgrade.MaxLevel && metaPoints >= upgrade.Cost;
            GUI.enabled = canBuy;
            string label = upgrade.Level >= upgrade.MaxLevel ? "MAX" : "Buy (" + upgrade.Cost + ")";
            if (GUILayout.Button(label, buttonStyle, GUILayout.Width(150f), GUILayout.Height(50f)))
            {
                TryBuyPermanentUpgrade(upgrade);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Space(5f);
        }
        GUILayout.EndScrollView();

        GUILayout.Space(10f);
        if (GUILayout.Button(upgradeReturnState == AdvancedGameState.MainMenu ? "Back to Menu" : "Back", menuButtonStyle, GUILayout.Height(52f)))
        {
            ClosePermanentUpgrades();
        }

        GUILayout.EndArea();
    }

    private void DrawStatsPanel()
    {
        Rect panel = CenterRect(820f, 650f);
        GUI.Box(panel, "");
        GUILayout.BeginArea(new Rect(panel.x + 34f, panel.y + 26f, panel.width - 68f, panel.height - 52f));
        GUILayout.Label("Current Build", titleStyle, GUILayout.Height(58f));

        statsScroll = GUILayout.BeginScrollView(statsScroll, false, true, GUILayout.Height(panel.height - 150f));
        if (player == null)
        {
            GUILayout.Label("No active run stats yet.", centeredSmallStyle, GUILayout.Height(32f));
        }
        else
        {
            GUILayout.Label("Core Stats", panelHeaderStyle, GUILayout.Height(32f));
            DrawStatLine("Class", player.Class.ToString());
            DrawStatLine("Damage", FormatNumber(player.Damage));
            DrawStatLine("Attack speed", FormatNumber(1f / Mathf.Max(0.01f, player.FireDelay)) + " attacks/s");
            DrawStatLine("Attack delay", FormatNumber(player.FireDelay) + "s");
            DrawStatLine("Projectile speed", player.BulletSpeed > 0f ? FormatNumber(player.BulletSpeed) : "Melee");
            if (player.Class == AdvancedPlayerClass.Knight)
            {
                DrawStatLine("Melee range", FormatNumber(player.MeleeRange) + " / " + Mathf.RoundToInt(player.MeleeArcDegrees) + " deg");
            }
            else if (player.Class == AdvancedPlayerClass.Mage)
            {
                DrawStatLine("Magic range", FormatNumber(player.MagicRange) + " / radius " + FormatNumber(player.MagicExplosionRadius));
            }
            else
            {
                DrawStatLine("Arrow range", "Unlimited");
            }
            DrawStatLine("Pierce", player.PierceCount.ToString());
            DrawStatLine("Move speed", FormatNumber(player.MoveSpeed));
            DrawStatLine("Dash cooldown", FormatNumber(player.DashCooldown) + "s");
            DrawStatLine("XP gain", Mathf.RoundToInt(player.ExperienceMultiplier * 100f) + "%");
            DrawStatLine("Luck", "+" + Mathf.RoundToInt(player.Luck * 100f) + "%");
            DrawStatLine("Upgrade options", player.LevelUpChoiceCount.ToString());
            DrawStatLine("XP pull speed", Mathf.RoundToInt(player.XpPickupSpeedMultiplier * 100f) + "%");
            GUILayout.Space(10f);

            GUILayout.Label("Defense", panelHeaderStyle, GUILayout.Height(32f));
            DrawStatLine("Health", Mathf.CeilToInt(player.Health) + "/" + Mathf.CeilToInt(player.MaxHealth));
            DrawStatLine("Regen", FormatNumber(player.HealthRegen) + " hp/s");
            DrawStatLine("Flat armor", FormatNumber(player.FlatArmor));
            DrawStatLine("Armor percent", Mathf.RoundToInt(player.ArmorPercent * 100f) + "%");
            DrawStatLine("Damage taken", Mathf.RoundToInt(player.DamageTakenMultiplier * 100f) + "%");
            DrawStatLine("Guardian", player.GuardianAngelAvailable ? "Ready / " + FormatNumber(player.GuardianAngelCooldown) + "s cd" : "Locked");
            DrawStatLine("Phoenix", player.PhoenixHeartAvailable ? "Ready" : "Locked");
            GUILayout.Space(10f);

            GUILayout.Label("Crits", panelHeaderStyle, GUILayout.Height(32f));
            DrawStatLine("Crit chance", Mathf.RoundToInt(player.CritChance * 100f) + "%");
            DrawStatLine("Crit damage", Mathf.RoundToInt(player.CritDamageMultiplier * 100f) + "%");
            DrawStatLine("Brittle bonus", Mathf.RoundToInt(player.BrittleCritDamageBonus * 100f) + "%");
            GUILayout.Space(10f);

            GUILayout.Label("Weapons & Effects", panelHeaderStyle, GUILayout.Height(32f));
            DrawStatLine("Burn", player.BurnDamagePerSecond > 0f ? Mathf.RoundToInt(player.BurnDamagePerSecond * 100f) + "% hit dmg/s for " + FormatNumber(player.BurnDuration) + "s" : "Locked");
            DrawStatLine("Poison", player.PoisonDamagePerSecond > 0f ? FormatNumber(player.PoisonDamagePerSecond * 100f) + "% max HP/s for " + FormatNumber(player.PoisonDuration) + "s" : "Locked");
            DrawStatLine("Freeze", player.FreezeDuration > 0f ? Mathf.RoundToInt((1f - player.FreezeSlowMultiplier) * 100f) + "% slow for " + FormatNumber(player.FreezeDuration) + "s" : "Locked");
            DrawStatLine("Mines", player.MineInterval > 0f ? FormatNumber(player.MineDamage) + " dmg / " + FormatNumber(player.MineInterval) + "s" : "Locked");
            DrawStatLine("Lightning", player.LightningInterval > 0f ? FormatNumber(player.LightningDamage) + " dmg / " + FormatNumber(player.LightningInterval) + "s, jumps " + player.LightningJumps : "Locked");
            DrawStatLine("Explosions", player.ExplosionChance > 0f ? Mathf.RoundToInt(player.ExplosionChance * 100f) + "% chance, radius " + FormatNumber(player.ExplosionRadius) : "Locked");
            DrawStatLine("Shockwave", player.ShockwaveInterval > 0f ? FormatNumber(player.ShockwaveInterval) + "s / " + Mathf.RoundToInt(player.ShockwaveDamageMultiplier * 100f) + "% dmg" : "Locked");
            DrawStatLine("Vampiric", player.VampiricPercent > 0f ? FormatNumber(player.VampiricPercent * 100f) + "% lifesteal" : "Locked");
            DrawStatLine("Class perk", ClassPerkSummary(player));
            GUILayout.Space(10f);
        }

        GUILayout.Label("Chosen Upgrades", panelHeaderStyle, GUILayout.Height(32f));
        if (acquiredUpgrades.Count == 0)
        {
            GUILayout.Label("No level-up upgrades chosen yet.", smallStyle, GUILayout.Height(28f));
        }
        else
        {
            for (int i = 0; i < acquiredUpgrades.Count; i++)
            {
                GUILayout.Label((i + 1) + ". " + acquiredUpgrades[i], smallStyle, GUILayout.Height(28f));
            }
        }

        GUILayout.EndScrollView();
        GUILayout.Space(12f);
        if (GUILayout.Button(statsReturnState == AdvancedGameState.Playing ? "Back to Game" : "Back", menuButtonStyle, GUILayout.Height(54f)))
        {
            CloseStats();
        }

        GUILayout.EndArea();
    }

    private void DrawStatLine(string label, string value)
    {
        GUILayout.BeginHorizontal(GUILayout.Height(28f));
        GUILayout.Label(label, smallStyle, GUILayout.Width(250f));
        GUILayout.Label(value, smallStyle);
        GUILayout.EndHorizontal();
    }

    private string ClassPerkSummary(AdvancedPlayerController target)
    {
        if (target.Class == AdvancedPlayerClass.Archer)
        {
            return target.SideArrowCount > 0 ? target.SideArrowCount + " side arrows, mark +" + Mathf.RoundToInt(target.HunterMarkBonus * 100f) + "%" : "Locked";
        }

        if (target.Class == AdvancedPlayerClass.Mage)
        {
            return target.SpellEchoChance > 0f || target.ArcaneOverload
                ? Mathf.RoundToInt(target.SpellEchoChance * 100f) + "% echo, overload " + (target.ArcaneOverload ? "on" : "off")
                : "Locked";
        }

        return target.WhirlwindEnabled || target.CounterattackEnabled
            ? "counter " + (target.CounterattackEnabled ? "on" : "off") + ", whirlwind " + (target.WhirlwindEnabled ? "on" : "off")
            : "Locked";
    }

    private void DrawGameOverPanel()
    {
        Rect panel = CenterRect(500f, 390f);
        GUI.Box(panel, "");
        GUILayout.BeginArea(new Rect(panel.x + 28f, panel.y + 24f, panel.width - 56f, panel.height - 48f));
        GUILayout.Label("Game Over", titleStyle, GUILayout.Height(50f));
        GUILayout.Label("Wave " + wave + "   Level " + level + "   Kills " + kills, hudStyle, GUILayout.Height(30f));
        GUILayout.Label("Bosses defeated: " + bossKills + "   Upgrade points gained: " + runPointsEarned, smallStyle, GUILayout.Height(26f));
        GUILayout.Label("Survived: " + FormatTime(survivedSeconds), smallStyle, GUILayout.Height(28f));
        GUILayout.Space(18f);

        if (GUILayout.Button("Run Again", buttonStyle, GUILayout.Height(54f))) StartRun();
        GUILayout.Space(8f);
        if (PermanentUpgradesEnabled)
        {
            if (GUILayout.Button("Permanent Upgrades", buttonStyle, GUILayout.Height(50f))) OpenPermanentUpgrades(AdvancedGameState.GameOver);
            GUILayout.Space(8f);
        }
        if (GUILayout.Button("Main Menu", buttonStyle, GUILayout.Height(50f))) ShowMainMenu();

        GUILayout.EndArea();
    }

    private Rect CenterRect(float width, float height)
    {
        float safeWidth = Mathf.Min(width, Mathf.Max(320f, Screen.width - 32f));
        float safeHeight = Mathf.Min(height, Mathf.Max(260f, Screen.height - 32f));
        return new Rect(Screen.width * 0.5f - safeWidth * 0.5f, Screen.height * 0.5f - safeHeight * 0.5f, safeWidth, safeHeight);
    }

    private string FormatTime(float seconds)
    {
        int total = Mathf.FloorToInt(seconds);
        return total / 60 + ":" + (total % 60).ToString("00");
    }

    private string FormatNumber(float value)
    {
        return value.ToString("0.##");
    }
}

internal sealed class AdvancedPlayerController : MonoBehaviour
{
    private AdvancedGameWorld world;
    private Transform barrel;
    private SpriteRenderer weaponRenderer;
    private SpriteRenderer spriteRenderer;
    private AdvancedPlayerClass playerClass;
    private Vector2 aimDirection = Vector2.right;
    private Vector2 dashDirection = Vector2.right;
    private Vector2 lastMoveDirection = Vector2.right;
    private float shotCooldown;
    private float invulnerabilityTimer;
    private float dashTimer;
    private float dashCooldownTimer;
    private float attackAnimTimer;
    private float drawTimer;
    private float mineTimer;
    private float lightningTimer;
    private float shockwaveTimer;
    private float elementalTempestTimer;
    private float elementalTempestActiveTimer;
    private float elementalTempestTickTimer;
    private float counterattackTimer;
    private float guardianAngelCooldownTimer;
    private float whirlwindTimer;
    private float bulletScale = 1f;
    private int spellCounter;
    private bool isDrawing;
    private bool facingLeft;

    public float MoveSpeed { get; set; }
    public float Damage { get; set; }
    public float FireDelay { get; set; }
    public float BulletSpeed { get; set; }
    public float MaxHealth { get; private set; }
    public float Health { get; private set; }
    public float HealthRegen { get; set; }
    public float MagnetRange { get; set; }
    public float DashCooldown { get; set; }
    public float SalvageBonus { get; set; }
    public float CritChance { get; set; }
    public float CritDamageMultiplier { get; set; }
    public float FlatArmor { get; set; }
    public float ArmorPercent { get; set; }
    public float BurnDamagePerSecond { get; set; }
    public float BurnDuration { get; set; }
    public float PoisonDamagePerSecond { get; set; }
    public float PoisonDuration { get; set; }
    public float FreezeDuration { get; set; }
    public float FreezeSlowMultiplier { get; set; }
    public float MineInterval { get; set; }
    public float MineDamage { get; set; }
    public float MineRadius { get; set; }
    public float LightningInterval { get; set; }
    public float LightningDamage { get; set; }
    public float LightningRange { get; set; }
    public int LightningJumps { get; set; }
    public float ExplosionChance { get; set; }
    public float ExplosionRadius { get; set; }
    public float ExplosionDamageMultiplier { get; set; }
    public int PierceCount { get; set; }
    public float MeleeRange { get; set; }
    public float MeleeArcDegrees { get; set; }
    public float MagicRange { get; set; }
    public float MagicExplosionRadius { get; set; }
    public float ExperienceMultiplier { get; set; }
    public float Luck { get; set; }
    public int LevelUpChoiceCount { get; set; }
    public float XpPickupSpeedMultiplier { get; set; }
    public float FieldRationsDropChance { get; set; }
    public float FieldRationsEliteDropChance { get; set; }
    public float FieldRationsHealPercent { get; set; }
    public int BossHealthDropCount { get; set; }
    public float ExecutionerBonus { get; set; }
    public float GiantSlayerBonus { get; set; }
    public float EliteDamageReduction { get; set; }
    public float DamageTakenMultiplier { get; set; }
    public float VampiricPercent { get; set; }
    public float ShockwaveInterval { get; set; }
    public float ShockwaveDamageMultiplier { get; set; }
    public float ShockwaveRadius { get; set; }
    public float BerserkerMaxBonus { get; set; }
    public float BrittleCritDamageBonus { get; set; }
    public float HunterMarkBonus { get; set; }
    public int SideArrowCount { get; set; }
    public float SideArrowDamageMultiplier { get; set; }
    public float SpellEchoChance { get; set; }
    public float SpellEchoDamageMultiplier { get; set; }
    public float GuardianAngelCooldown { get; set; }
    public float WorldbreakerDamageMultiplier { get; set; }
    public float WorldbreakerRadius { get; set; }
    public int WildfireTargets { get; set; }
    public bool Contagion { get; set; }
    public bool SteamBurst { get; set; }
    public bool ToxicFlame { get; set; }
    public bool VolatileMines { get; set; }
    public bool GuardianAngelAvailable { get; set; }
    public bool PhoenixHeartAvailable { get; set; }
    public bool ApexPredator { get; set; }
    public bool ArcaneOverload { get; set; }
    public bool CounterattackEnabled { get; set; }
    public bool WhirlwindEnabled { get; set; }
    public bool ElementalTempestEnabled { get; set; }

    public float HealthPercent { get { return MaxHealth <= 0f ? 0f : Mathf.Clamp01(Health / MaxHealth); } }
    public float DashFill { get { return DashCooldown <= 0f ? 1f : 1f - Mathf.Clamp01(dashCooldownTimer / DashCooldown); } }
    public AdvancedPlayerClass Class { get { return playerClass; } }

    public void Initialize(AdvancedGameWorld owner, AdvancedPlayerClass heroClass)
    {
        world = owner;
        playerClass = heroClass;
        HealthRegen = 0f;
        MagnetRange = 2.2f;
        SalvageBonus = 0f;
        CritChance = 0.05f;
        CritDamageMultiplier = 1.5f;
        FlatArmor = 0f;
        ArmorPercent = 0f;
        BurnDamagePerSecond = 0f;
        BurnDuration = 0f;
        PoisonDamagePerSecond = 0f;
        PoisonDuration = 0f;
        FreezeDuration = 0f;
        FreezeSlowMultiplier = 1f;
        MineInterval = 0f;
        MineDamage = 0f;
        MineRadius = 1.5f;
        LightningInterval = 0f;
        LightningDamage = 0f;
        LightningRange = 10f;
        LightningJumps = 0;
        ExplosionChance = 0f;
        ExplosionRadius = 1.8f;
        ExplosionDamageMultiplier = 0.8f;
        MeleeRange = 1.85f;
        MeleeArcDegrees = 112f;
        MagicRange = 5.7f;
        MagicExplosionRadius = 1.25f;
        ExperienceMultiplier = 1f;
        Luck = 0f;
        LevelUpChoiceCount = 3;
        XpPickupSpeedMultiplier = 1f;
        FieldRationsDropChance = 0f;
        FieldRationsEliteDropChance = 0f;
        FieldRationsHealPercent = 0f;
        BossHealthDropCount = 1;
        ExecutionerBonus = 0f;
        GiantSlayerBonus = 0f;
        EliteDamageReduction = 0f;
        DamageTakenMultiplier = 1f;
        VampiricPercent = 0f;
        ShockwaveInterval = 0f;
        ShockwaveDamageMultiplier = 0f;
        ShockwaveRadius = 10f;
        BerserkerMaxBonus = 0f;
        BrittleCritDamageBonus = 0f;
        HunterMarkBonus = 0f;
        SideArrowCount = 0;
        SideArrowDamageMultiplier = 0.45f;
        SpellEchoChance = 0f;
        SpellEchoDamageMultiplier = 0.6f;
        GuardianAngelCooldown = 60f;
        WorldbreakerDamageMultiplier = 0f;
        WorldbreakerRadius = 2.2f;
        WildfireTargets = 0;
        Contagion = false;
        SteamBurst = false;
        ToxicFlame = false;
        VolatileMines = false;
        GuardianAngelAvailable = false;
        PhoenixHeartAvailable = false;
        ApexPredator = false;
        ArcaneOverload = false;
        CounterattackEnabled = false;
        WhirlwindEnabled = false;
        ElementalTempestEnabled = false;
        ConfigureClassStats(heroClass);
        Health = MaxHealth;
        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = AdvancedGameArt.HeroSprite(playerClass, AdvancedHeroAnimation.Idle, 0);

        GameObject barrelObject = new GameObject("Aim Barrel");
        barrelObject.transform.SetParent(transform);
        barrelObject.transform.localPosition = new Vector3(0f, 0.54f, -0.01f);
        barrelObject.transform.localScale = WeaponScale();

        weaponRenderer = barrelObject.AddComponent<SpriteRenderer>();
        weaponRenderer.sprite = AdvancedGameArt.WeaponSprite(playerClass, 0);
        weaponRenderer.sortingOrder = 11;
        barrel = barrelObject.transform;
    }

    private void ConfigureClassStats(AdvancedPlayerClass heroClass)
    {
        PierceCount = 0;
        DashCooldown = 2.4f;

        switch (heroClass)
        {
            case AdvancedPlayerClass.Knight:
                MoveSpeed = 4.7f;
                Damage = 40f;
                FireDelay = 0.62f;
                BulletSpeed = 0f;
                MaxHealth = 150f;
                DashCooldown = 2.8f;
                ArmorPercent = 0.12f;
                MeleeRange = 2.35f;
                MeleeArcDegrees = 118f;
                break;
            case AdvancedPlayerClass.Mage:
                MoveSpeed = 4.55f;
                Damage = 42f;
                FireDelay = 0.95f;
                BulletSpeed = 8.2f;
                MaxHealth = 82f;
                MagicRange = 7f;
                MagicExplosionRadius = 1.65f;
                break;
            default:
                MoveSpeed = 5.6f;
                Damage = 20f;
                FireDelay = 0.38f;
                BulletSpeed = 17f;
                MaxHealth = 78f;
                PierceCount = 1;
                break;
        }
    }

    private Vector3 WeaponScale()
    {
        switch (playerClass)
        {
            case AdvancedPlayerClass.Knight:
                return new Vector3(1.05f, 1.05f, 1f);
            case AdvancedPlayerClass.Mage:
                return new Vector3(0.75f, 0.75f, 1f);
            default:
                return new Vector3(0.8f, 0.8f, 1f);
        }
    }

    private void Update()
    {
        if (world == null || world.IsGameplayPaused)
        {
            return;
        }

        HandleMovement();
        HandleAiming();
        HandleShooting();
        HandlePassiveWeapons();
        Regenerate();
        UpdateTimers();
        UpdateAnimation();
    }

    private void HandleMovement()
    {
        Vector2 input = Vector2.zero;
        if (Input.GetKey(KeyCode.W)) input.y += 1f;
        if (Input.GetKey(KeyCode.S)) input.y -= 1f;
        if (Input.GetKey(KeyCode.D)) input.x += 1f;
        if (Input.GetKey(KeyCode.A)) input.x -= 1f;
        if (input.sqrMagnitude > 1f) input.Normalize();
        if (input.x < -0.05f)
        {
            facingLeft = true;
        }
        else if (input.x > 0.05f)
        {
            facingLeft = false;
        }

        if (input.sqrMagnitude > 0.05f)
        {
            lastMoveDirection = input.normalized;
        }

        if (Input.GetKeyDown(KeyCode.Space) && dashCooldownTimer <= 0f)
        {
            dashDirection = input.sqrMagnitude > 0.05f ? input.normalized : aimDirection;
            dashTimer = 0.14f;
            dashCooldownTimer = DashCooldown;
            invulnerabilityTimer = Mathf.Max(invulnerabilityTimer, 0.18f);
        }

        float speed = dashTimer > 0f ? MoveSpeed * 4.3f : MoveSpeed;
        Vector2 direction = dashTimer > 0f ? dashDirection : input;
        Vector2 currentPosition = transform.position;
        Vector2 nextPosition = world.ResolveMovement(currentPosition, direction * speed * Time.deltaTime, 0.46f);
        transform.position = nextPosition;
    }

    private void HandleAiming()
    {
        Camera camera = Camera.main;
        if (camera == null) return;

        Vector3 mouseWorld = camera.ScreenToWorldPoint(Input.mousePosition);
        Vector2 direction = (Vector2)(mouseWorld - transform.position);
        if (direction.sqrMagnitude > 0.05f)
        {
            aimDirection = direction.normalized;
        }

        transform.rotation = Quaternion.identity;
        barrel.localPosition = new Vector3(aimDirection.x * 0.54f, aimDirection.y * 0.54f, -0.01f);
        barrel.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(aimDirection.y, aimDirection.x) * Mathf.Rad2Deg + WeaponRotationOffset());
        barrel.localScale = WeaponScale() * (attackAnimTimer > 0f ? 1.12f : 1f);
    }

    private float WeaponRotationOffset()
    {
        return playerClass == AdvancedPlayerClass.Mage ? -90f : 0f;
    }

    private void HandleShooting()
    {
        shotCooldown -= Time.deltaTime;
        if (playerClass == AdvancedPlayerClass.Archer)
        {
            HandleArcherAttack();
            return;
        }

        if (Input.GetMouseButton(0) == false || shotCooldown > 0f)
        {
            return;
        }

        shotCooldown = FireDelay;
        attackAnimTimer = playerClass == AdvancedPlayerClass.Knight ? 0.28f : 0.22f;

        if (playerClass == AdvancedPlayerClass.Knight)
        {
            Vector2 origin = transform.position;
            float attackDamage = Damage;
            float range = MeleeRange + bulletScale * 0.12f;
            float arc = MeleeArcDegrees;
            if (CounterattackEnabled && counterattackTimer > 0f)
            {
                attackDamage *= 1.8f;
                counterattackTimer = 0f;
                world.DamageEnemiesInRadius(origin + aimDirection * Mathf.Min(range, 1.8f), 1.45f, Damage * 0.6f, false);
            }

            if (WhirlwindEnabled && whirlwindTimer <= 0f)
            {
                attackDamage *= 1.4f;
                range += 0.35f;
                arc = 360f;
                whirlwindTimer = Mathf.Max(3f, 7f * Mathf.Clamp(FireDelay / 0.62f, 0.45f, 1f));
            }

            world.PerformPlayerMelee(origin, aimDirection, range, arc, attackDamage);
            return;
        }

        Vector2 magicOrigin = (Vector2)transform.position + aimDirection * 0.72f;
        spellCounter++;
        float magicDamage = Damage;
        float magicScale = bulletScale * 1.15f;
        float magicRadius = MagicExplosionRadius;
        if (ArcaneOverload && spellCounter % 5 == 0)
        {
            magicDamage *= 1.8f;
            magicScale *= 1.4f;
            magicRadius *= 1.4f;
        }

        world.SpawnPlayerBullet(magicOrigin, aimDirection, magicDamage, BulletSpeed, 0, magicScale, AdvancedProjectileKind.Magic, MagicRange, magicRadius);
        if (SpellEchoChance > 0f && UnityEngine.Random.value < SpellEchoChance)
        {
            StartCoroutine(SpawnMagicEcho(magicOrigin, aimDirection, magicDamage * SpellEchoDamageMultiplier, magicScale, magicRadius));
        }
    }

    private void HandleArcherAttack()
    {
        if (Input.GetMouseButton(0) == false)
        {
            isDrawing = false;
            drawTimer = 0f;
            return;
        }

        if (shotCooldown > 0f)
        {
            return;
        }

        if (isDrawing == false)
        {
            isDrawing = true;
            drawTimer = 0.22f;
            attackAnimTimer = 0.32f;
        }

        drawTimer -= Time.deltaTime;
        if (drawTimer > 0f)
        {
            return;
        }

        isDrawing = false;
        shotCooldown = FireDelay;
        Vector2 origin = (Vector2)transform.position + aimDirection * (0.72f + bulletScale * 0.08f);
        world.SpawnPlayerBullet(origin, aimDirection, Damage, BulletSpeed, PierceCount, bulletScale, AdvancedProjectileKind.Arrow, 0f, 0f);
        for (int i = 0; i < SideArrowCount; i++)
        {
            int side = i % 2 == 0 ? 1 : -1;
            int layer = i / 2 + 1;
            Vector2 sideDirection = AdvancedGameMath.Rotate(aimDirection, side * 15f * layer).normalized;
            world.SpawnPlayerBullet(origin, sideDirection, Damage * SideArrowDamageMultiplier, BulletSpeed, Mathf.Max(0, PierceCount - 1), bulletScale * 0.95f, AdvancedProjectileKind.Arrow, 0f, 0f);
        }
    }

    private IEnumerator SpawnMagicEcho(Vector2 origin, Vector2 direction, float damage, float scale, float radius)
    {
        yield return new WaitForSeconds(0.25f);
        if (world != null && world.IsGameplayPaused == false)
        {
            world.SpawnPlayerBullet(origin, direction, damage, BulletSpeed, 0, scale, AdvancedProjectileKind.Magic, MagicRange, radius);
        }
    }

    private void HandlePassiveWeapons()
    {
        if (MineInterval > 0f)
        {
            mineTimer -= Time.deltaTime;
            if (mineTimer <= 0f)
            {
                mineTimer = MineInterval;
                Vector2 dropDirection = lastMoveDirection.sqrMagnitude > 0.05f ? lastMoveDirection.normalized : aimDirection;
                Vector2 dropPosition = (Vector2)transform.position - dropDirection * 0.72f;
                world.SpawnPlayerMine(dropPosition, Mathf.Max(MineDamage, Damage * 1.1f), MineRadius, VolatileMines);
            }
        }

        if (LightningInterval > 0f)
        {
            lightningTimer -= Time.deltaTime;
            if (lightningTimer <= 0f)
            {
                lightningTimer = LightningInterval;
                world.StrikeLightning(transform.position, Mathf.Max(LightningDamage, Damage * 1.1f), LightningRange, LightningJumps);
            }
        }

        if (ShockwaveInterval > 0f)
        {
            shockwaveTimer -= Time.deltaTime;
            if (shockwaveTimer <= 0f)
            {
                shockwaveTimer = ShockwaveInterval;
                world.DamageEnemiesInRadius(transform.position, ShockwaveRadius, Damage * ShockwaveDamageMultiplier, true);
            }
        }

        if (ElementalTempestEnabled)
        {
            if (elementalTempestActiveTimer <= 0f)
            {
                elementalTempestTimer -= Time.deltaTime;
                if (elementalTempestTimer <= 0f)
                {
                    elementalTempestTimer = 12f;
                    elementalTempestActiveTimer = 4f;
                    elementalTempestTickTimer = 0f;
                }
            }
            else
            {
                elementalTempestActiveTimer -= Time.deltaTime;
                elementalTempestTickTimer -= Time.deltaTime;
                if (elementalTempestTickTimer <= 0f)
                {
                    elementalTempestTickTimer = 0.5f;
                    world.DamageEnemiesInRadius(transform.position, 4.2f, Damage * 0.45f, true);
                }
            }
        }
    }

    private void Regenerate()
    {
        if (HealthRegen > 0f && Health < MaxHealth)
        {
            Health = Mathf.Min(MaxHealth, Health + HealthRegen * Time.deltaTime);
        }
    }

    private void UpdateTimers()
    {
        if (invulnerabilityTimer > 0f)
        {
            invulnerabilityTimer -= Time.deltaTime;
            if (spriteRenderer != null)
            {
                spriteRenderer.color = Mathf.FloorToInt(invulnerabilityTimer * 20f) % 2 == 0 ? Color.white : new Color(0.7f, 0.9f, 1f, 0.65f);
            }
        }
        else if (spriteRenderer != null)
        {
            spriteRenderer.color = Color.white;
        }

        if (dashTimer > 0f) dashTimer -= Time.deltaTime;
        if (dashCooldownTimer > 0f) dashCooldownTimer -= Time.deltaTime;
        if (attackAnimTimer > 0f) attackAnimTimer -= Time.deltaTime;
        if (counterattackTimer > 0f) counterattackTimer -= Time.deltaTime;
        if (guardianAngelCooldownTimer > 0f) guardianAngelCooldownTimer -= Time.deltaTime;
        if (whirlwindTimer > 0f) whirlwindTimer -= Time.deltaTime;
    }

    private void UpdateAnimation()
    {
        if (spriteRenderer == null)
        {
            return;
        }

        AdvancedHeroAnimation animation = AdvancedHeroAnimation.Idle;
        if (attackAnimTimer > 0f || isDrawing)
        {
            animation = playerClass == AdvancedPlayerClass.Archer ? AdvancedHeroAnimation.Draw : AdvancedHeroAnimation.Attack;
        }
        else
        {
            bool moving = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D) || dashTimer > 0f;
            animation = moving ? AdvancedHeroAnimation.Walk : AdvancedHeroAnimation.Idle;
        }

        int frame = Mathf.FloorToInt(Time.time * (animation == AdvancedHeroAnimation.Walk ? 8f : 10f));
        spriteRenderer.sprite = AdvancedGameArt.HeroSprite(playerClass, animation, frame);
        spriteRenderer.flipX = facingLeft;

        if (weaponRenderer != null)
        {
            int weaponFrame = animation == AdvancedHeroAnimation.Idle ? 0 : frame % 4;
            weaponRenderer.sprite = AdvancedGameArt.WeaponSprite(playerClass, weaponFrame);
        }
    }

    public float RollAttackDamage(float baseDamage, out bool critical)
    {
        return RollAttackDamage(baseDamage, null, out critical);
    }

    public float RollAttackDamage(float baseDamage, AdvancedEnemyController enemy, out bool critical)
    {
        critical = UnityEngine.Random.value < CritChance;
        float damage = baseDamage;
        if (BerserkerMaxBonus > 0f)
        {
            float missingHealthBonus = Mathf.Clamp((1f - HealthPercent) * 0.5f, 0f, BerserkerMaxBonus);
            damage *= 1f + missingHealthBonus;
        }

        if (enemy != null)
        {
            if (enemy.HealthPercent <= 0.25f)
            {
                damage *= 1f + ExecutionerBonus;
            }

            if (enemy.IsEliteOrBoss)
            {
                damage *= 1f + GiantSlayerBonus + (ApexPredator ? 0.35f : 0f);
            }
        }

        float critMultiplier = CritDamageMultiplier;
        if (critical && enemy != null && enemy.HasFreeze)
        {
            critMultiplier += BrittleCritDamageBonus;
        }

        return critical ? damage * critMultiplier : damage;
    }

    public void ApplyOnHitEffects(AdvancedEnemyController enemy, Vector3 hitPosition, float dealtDamage, bool allowExplosion = true)
    {
        if (enemy == null)
        {
            return;
        }

        if (BurnDamagePerSecond > 0f && BurnDuration > 0f)
        {
            enemy.ApplyStatus(AdvancedStatusEffect.Burn, Mathf.Max(1f, BurnDuration), Mathf.Max(1f, dealtDamage * BurnDamagePerSecond), 1f);
        }

        if (PoisonDamagePerSecond > 0f && PoisonDuration > 0f)
        {
            enemy.ApplyStatus(AdvancedStatusEffect.Poison, Mathf.Max(1f, PoisonDuration), Mathf.Max(0.001f, PoisonDamagePerSecond), 1f);
        }

        if (FreezeDuration > 0f)
        {
            enemy.ApplyStatus(AdvancedStatusEffect.Freeze, Mathf.Max(0.6f, FreezeDuration), 0f, Mathf.Clamp(FreezeSlowMultiplier, 0.25f, 0.98f));
        }

        if (HunterMarkBonus > 0f)
        {
            enemy.ApplyMark(5f, 1f + HunterMarkBonus);
        }

        if (VampiricPercent > 0f && dealtDamage > 0f)
        {
            Heal(dealtDamage * VampiricPercent);
        }

        if (SteamBurst && BurnDamagePerSecond > 0f && enemy.HasFreeze)
        {
            world.DamageEnemiesInRadius(hitPosition, 2f, dealtDamage * 1.4f, false);
        }

        if (WorldbreakerDamageMultiplier > 0f && allowExplosion)
        {
            world.DamageEnemiesInRadius(hitPosition, WorldbreakerRadius, dealtDamage * WorldbreakerDamageMultiplier, false);
        }

        if (allowExplosion && ExplosionChance > 0f && UnityEngine.Random.value < ExplosionChance)
        {
            float explosionDamage = Mathf.Max(6f, dealtDamage * ExplosionDamageMultiplier);
            world.DamageEnemiesInRadius(hitPosition, ExplosionRadius, explosionDamage);
        }
    }

    public void TakeDamage(float amount)
    {
        if (invulnerabilityTimer > 0f || world.IsGameplayPaused)
        {
            return;
        }

        float percentReduction = Mathf.Clamp01(ArmorPercent + (ApexPredator ? EliteDamageReduction : 0f));
        float mitigated = Mathf.Max(1f, amount * (1f - percentReduction) - FlatArmor);
        mitigated *= Mathf.Max(0.1f, DamageTakenMultiplier);
        if (Health - mitigated <= 0f)
        {
            if (PhoenixHeartAvailable)
            {
                PhoenixHeartAvailable = false;
                Health = MaxHealth * 0.5f;
                invulnerabilityTimer = 2f;
                world.DamageEnemiesInRadius(transform.position, 5f, Damage * 5f, false);
                world.CreateFloatingText("Phoenix!", transform.position + Vector3.up * 0.9f, new Color(1f, 0.52f, 0.2f));
                return;
            }

            if (GuardianAngelAvailable && guardianAngelCooldownTimer <= 0f)
            {
                Health = Mathf.Max(1f, MaxHealth * 0.25f);
                guardianAngelCooldownTimer = GuardianAngelCooldown;
                invulnerabilityTimer = 2f;
                world.CreateFloatingText("Guardian!", transform.position + Vector3.up * 0.9f, new Color(0.65f, 0.9f, 1f));
                return;
            }
        }

        Health -= mitigated;
        if (CounterattackEnabled)
        {
            counterattackTimer = 2f;
        }

        invulnerabilityTimer = 0.12f;
        world.SpawnBurst(transform.position, new Color(0.42f, 0.8f, 1f), 5);
        if (Health <= 0f)
        {
            Health = 0f;
            world.TriggerGameOver();
        }
    }

    public void Heal(float amount)
    {
        Health = Mathf.Min(MaxHealth, Health + amount);
        world.CreateFloatingText("+" + Mathf.CeilToInt(amount) + " hp", transform.position + Vector3.up * 0.8f, new Color(0.24f, 1f, 0.4f));
    }

    public void IncreaseMaxHealth(float amount)
    {
        MaxHealth += amount;
        Health = MaxHealth;
    }

    public void IncreaseMaxHealthPercent(float percent)
    {
        IncreaseMaxHealth(MaxHealth * percent);
    }

    public void GrowBullets()
    {
        bulletScale *= 1.2f;
        Damage *= 1.1f;
        transform.localScale = Vector3.one * Mathf.Min(1.35f, 1f + (bulletScale - 1f) * 0.15f);
    }

    public void EnableMines(bool major = false)
    {
        if (MineInterval <= 0f)
        {
            MineInterval = major ? 1.75f : 2.5f;
            MineDamage = Damage * (major ? 1.55f : 1.1f);
            MineRadius = major ? 1.8f : 1.5f;
            mineTimer = 0.65f;
            return;
        }

        MineInterval = Mathf.Max(0.95f, MineInterval * (major ? 0.65f : 0.82f));
        MineDamage *= major ? 1.4f : 1.2f;
        MineRadius = Mathf.Min(2.8f, MineRadius * (major ? 1.2f : 1.1f));
    }

    public void AddBurn(bool major = false)
    {
        if (BurnDamagePerSecond <= 0f)
        {
            BurnDamagePerSecond = 0.12f;
            BurnDuration = 3f;
            return;
        }

        AddBurnDamage(major);
        AddBurnDuration(major);
    }

    public void AddPoison(bool major = false)
    {
        if (PoisonDamagePerSecond <= 0f)
        {
            PoisonDamagePerSecond = 0.01f;
            PoisonDuration = 5f;
            return;
        }

        AddPoisonDamage(major);
        AddPoisonDuration(major);
    }

    public void AddFreeze(bool major = false)
    {
        if (FreezeDuration <= 0f)
        {
            FreezeDuration = 2.5f;
            FreezeSlowMultiplier = 0.8f;
            return;
        }

        AddFreezeDuration(major);
        AddFreezeStrength(major);
    }

    public void AddBurnDamage(bool major = false)
    {
        BurnDamagePerSecond += major ? 0.06f : 0.04f;
    }

    public void AddBurnDuration(bool major = false)
    {
        BurnDuration += major ? 3f : 2f;
    }

    public void AddPoisonDamage(bool major = false)
    {
        PoisonDamagePerSecond += major ? 0.015f : 0.01f;
    }

    public void AddPoisonDuration(bool major = false)
    {
        PoisonDuration += major ? 3f : 2f;
    }

    public void AddFreezeDuration(bool major = false)
    {
        FreezeDuration += major ? 3f : 2f;
    }

    public void AddFreezeStrength(bool major = false)
    {
        FreezeSlowMultiplier = Mathf.Max(0.35f, FreezeSlowMultiplier - (major ? 0.1f : 0.075f));
    }

    public void ImproveLightning(bool major = false)
    {
        if (LightningInterval <= 0f)
        {
            LightningInterval = major ? 2.75f : 4f;
            LightningDamage = Damage * (major ? 2.3f : 1.7f);
            LightningRange = major ? 12f : 10f;
            LightningJumps = major ? 5 : 2;
            lightningTimer = 0.4f;
            return;
        }

        LightningInterval = Mathf.Max(2.25f, LightningInterval * (major ? 0.7f : 0.9f));
        LightningDamage *= major ? 1.5f : 1.2f;
        LightningRange += major ? 2f : 1f;
        LightningJumps = Mathf.Min(5, LightningJumps + (major ? 2 : 1));
    }

    public void AddExplosiveHits(bool major = false)
    {
        if (major)
        {
            ExplosionChance = Mathf.Min(0.8f, Mathf.Max(0.15f, ExplosionChance) * 1.5f);
            ExplosionRadius = Mathf.Min(3.2f, Mathf.Max(1.8f, ExplosionRadius) * 1.5f);
            ExplosionDamageMultiplier *= 1.5f;
            return;
        }

        ExplosionChance = ExplosionChance <= 0f ? 0.15f : Mathf.Min(0.7f, ExplosionChance + 0.05f);
        ExplosionRadius = Mathf.Min(2.9f, Mathf.Max(ExplosionRadius, 1.8f) + 0.2f);
        ExplosionDamageMultiplier = Mathf.Max(0.8f, ExplosionDamageMultiplier + 0.08f);
    }

    public void ImproveFieldRations()
    {
        FieldRationsDropChance += FieldRationsDropChance <= 0f ? 0.04f : 0.02f;
        FieldRationsEliteDropChance = Mathf.Max(FieldRationsEliteDropChance, 0.2f);
        FieldRationsHealPercent += FieldRationsHealPercent <= 0f ? 0.1f : 0.02f;
    }

    public void SetSupplyTier(float normalDropChance, float eliteDropChance, float healPercent, int bossDropCount)
    {
        FieldRationsDropChance = Mathf.Max(FieldRationsDropChance, normalDropChance);
        FieldRationsEliteDropChance = Mathf.Max(FieldRationsEliteDropChance, eliteDropChance);
        FieldRationsHealPercent = Mathf.Max(FieldRationsHealPercent, healPercent);
        BossHealthDropCount = Mathf.Max(BossHealthDropCount, bossDropCount);
    }

    public void AddLuck(float amount)
    {
        Luck = Mathf.Min(3f, Luck + amount);
    }

    public void ImproveShockwave()
    {
        if (ShockwaveInterval <= 0f)
        {
            ShockwaveInterval = 10f;
            ShockwaveDamageMultiplier = 0.3f;
            ShockwaveRadius = 10f;
            shockwaveTimer = 1f;
            return;
        }

        ShockwaveInterval = Mathf.Max(5f, ShockwaveInterval - 1f);
        ShockwaveDamageMultiplier += 0.1f;
    }

    public void ImproveBerserker()
    {
        BerserkerMaxBonus = BerserkerMaxBonus <= 0f ? 0.35f : 0.5f;
    }

    public void AddGlassCannon()
    {
        Damage *= 1.4f;
        DamageTakenMultiplier *= 1.2f;
    }

    public void EnableGuardianAngel()
    {
        if (GuardianAngelAvailable)
        {
            GuardianAngelCooldown = 45f;
            return;
        }

        GuardianAngelAvailable = true;
        GuardianAngelCooldown = 60f;
    }

    public void AddToxicFlame()
    {
        ToxicFlame = true;
        BurnDamagePerSecond *= 1.25f;
        PoisonDamagePerSecond *= 1.35f;
    }

    public void AddSplitArrow()
    {
        SideArrowCount = Mathf.Min(6, SideArrowCount + (SideArrowCount <= 0 ? 2 : 1));
        SideArrowDamageMultiplier = Mathf.Min(0.75f, SideArrowDamageMultiplier + 0.1f);
    }

    public void AddSpellEcho()
    {
        SpellEchoChance = SpellEchoChance <= 0f ? 0.2f : Mathf.Min(0.55f, SpellEchoChance + 0.1f);
        SpellEchoDamageMultiplier = Mathf.Min(0.85f, SpellEchoDamageMultiplier + 0.08f);
    }

    public void EnableArcaneOverload()
    {
        ArcaneOverload = true;
    }

    public void AddCleave()
    {
        MeleeArcDegrees = Mathf.Min(180f, MeleeArcDegrees + 24f);
        MeleeRange += 0.18f;
    }

    public void EnableCounterattack()
    {
        CounterattackEnabled = true;
        Damage *= 1.05f;
    }

    public void EnableWhirlwind()
    {
        WhirlwindEnabled = true;
        whirlwindTimer = 3f;
    }

    public void EnablePhoenixHeart()
    {
        PhoenixHeartAvailable = true;
    }

    public void EnableWorldbreaker()
    {
        WorldbreakerDamageMultiplier = 0.7f;
        WorldbreakerRadius = 2.2f;
    }

    public void EnableApexPredator()
    {
        ApexPredator = true;
        EliteDamageReduction = 0.15f;
    }

    public void EnableElementalTempest()
    {
        ElementalTempestEnabled = true;
        elementalTempestTimer = 2f;
    }
}

internal sealed class AdvancedEnemyController : MonoBehaviour
{
    private AdvancedGameWorld world;
    private AdvancedEnemyKind kind;
    private SpriteRenderer bodyRenderer;
    private Transform healthFill;
    private float maxHealth;
    private float health;
    private float speed;
    private float damage;
    private float attackRange;
    private float preferredRange;
    private float attackDelay;
    private float attackTimer;
    private float specialTimer;
    private float burnTimer;
    private float burnDamagePerSecond;
    private float poisonTimer;
    private float poisonDamagePerSecond;
    private float freezeTimer;
    private float freezeSlowMultiplier = 1f;
    private float markTimer;
    private float markDamageMultiplier = 1f;
    private float statusTickTimer;
    private int xpReward;
    private bool isDead;

    public Color VisualColor { get; private set; }
    public bool IsAlive { get { return isDead == false; } }
    public float HealthPercent { get { return maxHealth <= 0f ? 0f : Mathf.Clamp01(health / maxHealth); } }
    public bool IsBoss { get { return kind == AdvancedEnemyKind.Boss; } }
    public bool IsEliteOrBoss { get { return kind == AdvancedEnemyKind.Tank || kind == AdvancedEnemyKind.Boss; } }
    public bool HasBurn { get { return burnTimer > 0f; } }
    public bool HasPoison { get { return poisonTimer > 0f; } }
    public bool HasFreeze { get { return freezeTimer > 0f; } }
    public float BurnDurationRemaining { get { return burnTimer; } }
    public float BurnDamagePerSecond { get { return burnDamagePerSecond; } }

    public void Initialize(AdvancedGameWorld owner, AdvancedEnemyKind enemyKind, int wave)
    {
        world = owner;
        kind = enemyKind;
        ConfigureBaseStats();

        float waveIndex = Mathf.Max(0, wave - 1);
        maxHealth *= 1f + waveIndex * 0.16f + waveIndex * waveIndex * 0.004f;
        health = maxHealth;
        damage *= 1f + waveIndex * 0.105f;
        speed *= 1f + Mathf.Min(0.34f, waveIndex * 0.012f);
        xpReward = Mathf.RoundToInt(xpReward * (1f + waveIndex * 0.045f));
        BuildVisuals();
    }

    private void ConfigureBaseStats()
    {
        switch (kind)
        {
            case AdvancedEnemyKind.Ranged:
                maxHealth = 34f; speed = 2.15f; damage = 8f; attackRange = 8.2f; preferredRange = 5.3f; attackDelay = 1.45f; xpReward = 12; transform.localScale = Vector3.one * 0.92f; break;
            case AdvancedEnemyKind.Tank:
                maxHealth = 150f; speed = 1.25f; damage = 9f; attackRange = 0.95f; preferredRange = 0f; attackDelay = 1.1f; xpReward = 20; transform.localScale = Vector3.one * 1.45f; break;
            case AdvancedEnemyKind.GlassCannon:
                maxHealth = 24f; speed = 3.7f; damage = 18f; attackRange = 0.74f; preferredRange = 0f; attackDelay = 0.46f; xpReward = 14; transform.localScale = Vector3.one * 0.78f; break;
            case AdvancedEnemyKind.Exploder:
                maxHealth = 42f; speed = 2.95f; damage = 26f; attackRange = 1.05f; preferredRange = 0f; attackDelay = 0.1f; xpReward = 11; transform.localScale = Vector3.one * 1.05f; break;
            case AdvancedEnemyKind.Boss:
                maxHealth = 520f; speed = 1.15f; damage = 18f; attackRange = 1.5f; preferredRange = 0f; attackDelay = 0.65f; xpReward = 95; transform.localScale = Vector3.one * 2.45f; break;
            default:
                maxHealth = 48f; speed = 2.55f; damage = 11f; attackRange = 0.78f; preferredRange = 0f; attackDelay = 0.78f; xpReward = 9; transform.localScale = Vector3.one; break;
        }
    }

    private void BuildVisuals()
    {
        VisualColor = EnemyColor();

        bodyRenderer = gameObject.AddComponent<SpriteRenderer>();
        bodyRenderer.sprite = AdvancedGameArt.EnemySprite(kind);
        bodyRenderer.color = Color.white;
        bodyRenderer.sortingOrder = kind == AdvancedEnemyKind.Boss ? 10 : 9;

        CircleCollider2D collider = gameObject.AddComponent<CircleCollider2D>();
        collider.radius = kind == AdvancedEnemyKind.Boss ? 0.54f : 0.48f;
        collider.isTrigger = true;

        Rigidbody2D body = gameObject.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;

        float width = kind == AdvancedEnemyKind.Boss ? 1.0f : 0.72f;
        float y = kind == AdvancedEnemyKind.Boss ? 0.56f : 0.7f;
        GameObject barBack = new GameObject("Health Bar Back");
        barBack.transform.SetParent(transform);
        barBack.transform.localPosition = new Vector3(0f, y, -0.02f);
        barBack.transform.localScale = new Vector3(width, 0.08f, 1f);
        SpriteRenderer backRenderer = barBack.AddComponent<SpriteRenderer>();
        backRenderer.sprite = AdvancedGameArt.SquareSprite(new Color(0.03f, 0.03f, 0.04f));
        backRenderer.sortingOrder = 14;

        GameObject fill = new GameObject("Health Bar Fill");
        fill.transform.SetParent(transform);
        fill.transform.localPosition = new Vector3(0f, y, -0.03f);
        fill.transform.localScale = new Vector3(width - 0.04f, 0.045f, 1f);
        SpriteRenderer fillRenderer = fill.AddComponent<SpriteRenderer>();
        fillRenderer.sprite = AdvancedGameArt.SquareSprite(new Color(0.18f, 1f, 0.32f));
        fillRenderer.sortingOrder = 15;
        healthFill = fill.transform;
    }

    private Color EnemyColor()
    {
        switch (kind)
        {
            case AdvancedEnemyKind.Ranged: return new Color(0.77f, 0.35f, 1f);
            case AdvancedEnemyKind.Tank: return new Color(0.34f, 0.86f, 0.42f);
            case AdvancedEnemyKind.GlassCannon: return new Color(1f, 0.86f, 0.22f);
            case AdvancedEnemyKind.Exploder: return new Color(1f, 0.45f, 0.12f);
            case AdvancedEnemyKind.Boss: return new Color(1f, 0.22f, 0.55f);
            default: return new Color(1f, 0.24f, 0.24f);
        }
    }

    private void Update()
    {
        if (world == null || world.Player == null || world.IsGameplayPaused)
        {
            return;
        }

        attackTimer -= Time.deltaTime;
        specialTimer -= Time.deltaTime;
        UpdateStatuses();
        if (isDead)
        {
            return;
        }

        if (kind == AdvancedEnemyKind.Ranged) UpdateRanged();
        else if (kind == AdvancedEnemyKind.Boss) UpdateBoss();
        else UpdateMelee();

        if (bodyRenderer != null)
        {
            bodyRenderer.sprite = AdvancedGameArt.EnemySprite(kind, Mathf.FloorToInt(Time.time * 8f));
        }
    }

    private void UpdateMelee()
    {
        Vector2 toPlayer = world.Player.transform.position - transform.position;
        float distance = toPlayer.magnitude;
        if (kind == AdvancedEnemyKind.Exploder && distance <= attackRange)
        {
            Explode();
            return;
        }

        if (distance > attackRange)
        {
            Move(toPlayer.normalized);
        }
        else if (attackTimer <= 0f)
        {
            world.Player.TakeDamage(damage);
            attackTimer = attackDelay;
        }
    }

    private void UpdateRanged()
    {
        Vector2 toPlayer = world.Player.transform.position - transform.position;
        float distance = toPlayer.magnitude;
        Vector2 direction = distance > 0.1f ? toPlayer / distance : Vector2.right;
        if (distance > preferredRange + 0.6f) Move(direction);
        else if (distance < preferredRange - 1.0f) Move(-direction);

        if (distance <= attackRange && attackTimer <= 0f)
        {
            world.SpawnEnemyBullet((Vector2)transform.position + direction * 0.6f, direction, damage, 7.6f);
            attackTimer = attackDelay;
        }
    }

    private void UpdateBoss()
    {
        Vector2 toPlayer = world.Player.transform.position - transform.position;
        float distance = toPlayer.magnitude;
        Vector2 direction = distance > 0.1f ? toPlayer / distance : Vector2.right;
        if (distance > attackRange) Move(direction);
        else if (attackTimer <= 0f)
        {
            world.Player.TakeDamage(damage);
            attackTimer = attackDelay;
        }

        if (specialTimer <= 0f)
        {
            specialTimer = 2.2f;
            for (int i = 0; i < 12; i++)
            {
                Vector2 shotDirection = AdvancedGameMath.Rotate(Vector2.right, i * 30f);
                world.SpawnEnemyBullet((Vector2)transform.position + shotDirection * 0.9f, shotDirection, damage * 0.55f, 6.2f);
            }
        }
    }

    private void Move(Vector2 direction)
    {
        if (direction.sqrMagnitude > 0.01f)
        {
            Vector2 currentPosition = transform.position;
            float radius = kind == AdvancedEnemyKind.Boss ? 1.25f : 0.5f;
            Vector2 steeringDirection = world.FindOpenDirection(currentPosition, direction.normalized, radius);
            float speedMultiplier = freezeTimer > 0f ? freezeSlowMultiplier : 1f;
            Vector2 nextPosition = world.ResolveMovement(currentPosition, steeringDirection * speed * speedMultiplier * Time.deltaTime, radius);
            transform.position = nextPosition;
            if (bodyRenderer != null && Mathf.Abs(steeringDirection.x) > 0.05f)
            {
                bodyRenderer.flipX = steeringDirection.x < 0f;
            }
        }
    }

    private void Explode()
    {
        if (isDead) return;
        isDead = true;
        world.DamagePlayer(damage);
        world.EnemyExploded(this, transform.position);
        Destroy(gameObject);
    }

    public void ApplyStatus(AdvancedStatusEffect effect, float duration, float damagePerSecond, float slowMultiplier)
    {
        if (isDead)
        {
            return;
        }

        switch (effect)
        {
            case AdvancedStatusEffect.Burn:
                burnTimer = Mathf.Max(burnTimer, duration);
                burnDamagePerSecond = Mathf.Max(burnDamagePerSecond, damagePerSecond);
                break;
            case AdvancedStatusEffect.Poison:
                poisonTimer = Mathf.Max(poisonTimer, duration);
                poisonDamagePerSecond = Mathf.Max(poisonDamagePerSecond, damagePerSecond);
                break;
            case AdvancedStatusEffect.Freeze:
                freezeTimer = Mathf.Max(freezeTimer, duration);
                float adjustedSlow = kind == AdvancedEnemyKind.Boss ? 1f - ((1f - slowMultiplier) * 0.25f) : slowMultiplier;
                freezeSlowMultiplier = Mathf.Min(freezeSlowMultiplier, adjustedSlow);
                break;
        }

        if (bodyRenderer != null)
        {
            bodyRenderer.color = StatusColor();
        }
    }

    private void UpdateStatuses()
    {
        if (burnTimer > 0f) burnTimer -= Time.deltaTime;
        if (poisonTimer > 0f) poisonTimer -= Time.deltaTime;
        if (freezeTimer > 0f) freezeTimer -= Time.deltaTime;
        if (markTimer > 0f) markTimer -= Time.deltaTime;
        if (freezeTimer <= 0f) freezeSlowMultiplier = 1f;
        if (markTimer <= 0f) markDamageMultiplier = 1f;

        float damagePerSecond = 0f;
        if (burnTimer > 0f) damagePerSecond += burnDamagePerSecond;
        if (poisonTimer > 0f) damagePerSecond += maxHealth * poisonDamagePerSecond * (kind == AdvancedEnemyKind.Boss ? 0.5f : 1f);
        if (damagePerSecond > 0f)
        {
            statusTickTimer -= Time.deltaTime;
            if (statusTickTimer <= 0f)
            {
                statusTickTimer = 0.5f;
                TakeDamage(damagePerSecond * 0.5f);
            }
        }

        if (bodyRenderer != null && IsInvoking(nameof(RestoreColor)) == false)
        {
            bodyRenderer.color = StatusColor();
        }
    }

    public void TakeDamage(float amount)
    {
        TakeDamage(amount, false);
    }

    public void TakeDamage(float amount, bool critical)
    {
        if (isDead) return;

        if (markTimer > 0f)
        {
            amount *= markDamageMultiplier;
        }

        health -= amount;
        UpdateHealthBar();
        string text = critical ? "CRIT " + Mathf.CeilToInt(amount) : Mathf.CeilToInt(amount).ToString();
        world.CreateFloatingText(text, transform.position + Vector3.up * 0.55f, critical ? new Color(1f, 0.86f, 0.22f) : VisualColor);
        if (bodyRenderer != null)
        {
            bodyRenderer.color = Color.white;
            CancelInvoke(nameof(RestoreColor));
            Invoke(nameof(RestoreColor), 0.06f);
        }

        if (health <= 0f)
        {
            isDead = true;
            world.EnemyKilled(this, xpReward, transform.position, kind == AdvancedEnemyKind.Boss);
            Destroy(gameObject);
        }
    }

    private void RestoreColor()
    {
        if (bodyRenderer != null)
        {
            bodyRenderer.color = StatusColor();
        }
    }

    private Color StatusColor()
    {
        if (freezeTimer > 0f) return new Color(0.58f, 0.9f, 1f);
        if (poisonTimer > 0f) return new Color(0.48f, 1f, 0.32f);
        if (burnTimer > 0f) return new Color(1f, 0.55f, 0.22f);
        return Color.white;
    }

    public void ApplyMark(float duration, float damageMultiplier)
    {
        markTimer = Mathf.Max(markTimer, duration);
        markDamageMultiplier = Mathf.Max(markDamageMultiplier, damageMultiplier);
    }

    private void UpdateHealthBar()
    {
        if (healthFill == null) return;
        float percent = Mathf.Clamp01(health / maxHealth);
        float fullWidth = kind == AdvancedEnemyKind.Boss ? 0.96f : 0.68f;
        float y = kind == AdvancedEnemyKind.Boss ? 0.56f : 0.7f;
        healthFill.localScale = new Vector3(fullWidth * percent, 0.045f, 1f);
        healthFill.localPosition = new Vector3(-fullWidth * 0.5f * (1f - percent), y, -0.03f);
    }
}

internal sealed class AdvancedXpPickup : MonoBehaviour
{
    private AdvancedGameWorld world;
    private int amount;

    public void Initialize(AdvancedGameWorld owner, int xpAmount)
    {
        world = owner;
        amount = xpAmount;
    }

    private void Update()
    {
        if (world == null || world.Player == null || world.IsGameplayPaused) return;
        Vector2 toPlayer = world.Player.transform.position - transform.position;
        float distance = toPlayer.magnitude;
        if (distance <= 0.45f)
        {
            world.AddXp(amount);
            Destroy(gameObject);
            return;
        }

        if (distance <= world.Player.MagnetRange)
        {
            float speed = Mathf.Lerp(6f, 15f, 1f - distance / Mathf.Max(0.1f, world.Player.MagnetRange));
            transform.position += (Vector3)(toPlayer.normalized * speed * world.Player.XpPickupSpeedMultiplier * Time.deltaTime);
        }
    }
}

internal sealed class AdvancedHealthPickup : MonoBehaviour
{
    private AdvancedGameWorld world;
    private float amount;

    public void Initialize(AdvancedGameWorld owner, float healAmount)
    {
        world = owner;
        amount = healAmount;
    }

    private void Update()
    {
        if (world == null || world.Player == null || world.IsGameplayPaused) return;
        Vector2 toPlayer = world.Player.transform.position - transform.position;
        float distance = toPlayer.magnitude;
        if (distance <= 0.55f)
        {
            world.HealPlayer(amount);
            Destroy(gameObject);
            return;
        }

        if (distance <= world.Player.MagnetRange * 0.7f)
        {
            transform.position += (Vector3)(toPlayer.normalized * 7.5f * Time.deltaTime);
        }
    }
}

internal sealed class AdvancedSolidObstacle : MonoBehaviour
{
}

internal sealed class AdvancedBullet : MonoBehaviour
{
    private bool fromPlayer;
    private float damage;
    private float maxDistance;
    private float travelledDistance;
    private float explosionRadius;
    private int pierceRemaining;
    private float lifetime = 3.4f;
    private Rigidbody2D body;
    private AdvancedProjectileKind projectileKind;

    public void Initialize(bool playerBullet, Vector2 direction, float bulletDamage, float speed, int pierce, AdvancedProjectileKind kind, float projectileMaxDistance, float projectileExplosionRadius)
    {
        fromPlayer = playerBullet;
        damage = bulletDamage;
        pierceRemaining = pierce;
        projectileKind = kind;
        maxDistance = projectileMaxDistance;
        explosionRadius = projectileExplosionRadius;
        body = GetComponent<Rigidbody2D>();
        body.linearVelocity = direction.normalized * speed;
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
    }

    private void Update()
    {
        lifetime -= Time.deltaTime;
        if (body != null)
        {
            travelledDistance += body.linearVelocity.magnitude * Time.deltaTime;
        }

        if (fromPlayer && maxDistance > 0f && travelledDistance >= maxDistance)
        {
            ExplodeOrDestroy();
            return;
        }

        if (lifetime <= 0f)
        {
            ExplodeOrDestroy();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<AdvancedSolidObstacle>() != null)
        {
            ExplodeOrDestroy();
            return;
        }

        if (fromPlayer)
        {
            AdvancedEnemyController enemy = other.GetComponent<AdvancedEnemyController>();
            if (enemy == null) return;

            if (projectileKind == AdvancedProjectileKind.Magic)
            {
                ExplodeOrDestroy();
                return;
            }

            AdvancedPlayerController player = AdvancedGameWorld.Instance != null ? AdvancedGameWorld.Instance.Player : null;
            bool critical = false;
            float finalDamage = player != null ? player.RollAttackDamage(damage, enemy, out critical) : damage;
            enemy.TakeDamage(finalDamage, critical);
            if (player != null)
            {
                player.ApplyOnHitEffects(enemy, transform.position, finalDamage);
            }
            if (pierceRemaining <= 0) Destroy(gameObject);
            else pierceRemaining--;
            return;
        }

        AdvancedPlayerController playerHit = other.GetComponent<AdvancedPlayerController>();
        if (playerHit == null) return;
        playerHit.TakeDamage(damage);
        Destroy(gameObject);
    }

    private void ExplodeOrDestroy()
    {
        if (fromPlayer && projectileKind == AdvancedProjectileKind.Magic && explosionRadius > 0f && AdvancedGameWorld.Instance != null)
        {
            AdvancedGameWorld.Instance.DamageEnemiesInRadius(transform.position, explosionRadius, damage, true);
        }

        Destroy(gameObject);
    }
}

internal sealed class AdvancedMine : MonoBehaviour
{
    private AdvancedGameWorld world;
    private SpriteRenderer spriteRenderer;
    private float damage;
    private float radius;
    private float armTimer = 0.35f;
    private float lifetime = 18f;
    private bool usePlayerAttackEffects;
    private bool exploded;

    public void Initialize(AdvancedGameWorld owner, float mineDamage, float explosionRadius, bool inheritsPlayerAttackEffects)
    {
        world = owner;
        damage = mineDamage;
        radius = explosionRadius;
        usePlayerAttackEffects = inheritsPlayerAttackEffects;
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        armTimer -= Time.deltaTime;
        lifetime -= Time.deltaTime;
        float pulse = 1f + Mathf.Sin(Time.time * 8f) * 0.08f;
        transform.localScale = Vector3.one * 0.62f * pulse;
        if (spriteRenderer != null)
        {
            spriteRenderer.color = armTimer > 0f ? new Color(1f, 1f, 1f, 0.55f) : Color.white;
        }

        if (lifetime <= 0f)
        {
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (armTimer > 0f || exploded)
        {
            return;
        }

        if (other.GetComponent<AdvancedEnemyController>() == null)
        {
            return;
        }

        exploded = true;
        if (world != null)
        {
            world.DamageEnemiesInRadius(transform.position, radius, damage, usePlayerAttackEffects);
        }

        Destroy(gameObject);
    }
}

internal sealed class AdvancedLightningArc : MonoBehaviour
{
    private LineRenderer line;
    private Color color;
    private float life = 0.16f;

    public void Initialize(LineRenderer lineRenderer, Color arcColor)
    {
        line = lineRenderer;
        color = arcColor;
    }

    private void Update()
    {
        life -= Time.deltaTime;
        if (line != null)
        {
            Color faded = color;
            faded.a = Mathf.Clamp01(life / 0.16f);
            line.startColor = faded;
            line.endColor = new Color(1f, 1f, 1f, faded.a);
            line.startWidth = Mathf.Max(0f, line.startWidth - Time.deltaTime * 0.22f);
        }

        if (life <= 0f)
        {
            Destroy(gameObject);
        }
    }
}

internal sealed class AdvancedFloatingText : MonoBehaviour
{
    private TextMesh textMesh;
    private Color baseColor;
    private float life = 0.8f;

    public void Initialize(Color color)
    {
        baseColor = color;
        textMesh = GetComponent<TextMesh>();
    }

    private void Update()
    {
        life -= Time.deltaTime;
        transform.position += Vector3.up * 0.9f * Time.deltaTime;
        if (textMesh != null)
        {
            Color color = baseColor;
            color.a = Mathf.Clamp01(life / 0.8f);
            textMesh.color = color;
        }

        if (life <= 0f) Destroy(gameObject);
    }
}

internal sealed class AdvancedBurstShard : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private Vector2 velocity;
    private Color color;
    private float life;
    private float maxLife;

    public void Initialize(Vector2 startVelocity, float duration, Color shardColor)
    {
        velocity = startVelocity;
        life = duration;
        maxLife = duration;
        color = shardColor;
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        life -= Time.deltaTime;
        transform.position += (Vector3)(velocity * Time.deltaTime);
        velocity *= 0.9f;
        if (spriteRenderer != null)
        {
            Color faded = color;
            faded.a = Mathf.Clamp01(life / maxLife);
            spriteRenderer.color = faded;
        }

        if (life <= 0f) Destroy(gameObject);
    }
}

internal sealed class AdvancedCameraFollow : MonoBehaviour
{
    private float shake;

    public Transform Target { get; set; }

    private void LateUpdate()
    {
        Vector3 targetPosition = Target != null ? Target.position + new Vector3(0f, 0f, -10f) : new Vector3(0f, 0f, -10f);
        if (shake > 0f)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * shake;
            targetPosition += new Vector3(offset.x, offset.y, 0f);
            shake = Mathf.Max(0f, shake - Time.unscaledDeltaTime * 1.8f);
        }

        transform.position = Vector3.Lerp(transform.position, targetPosition, (Target != null ? 10f : 3f) * Time.unscaledDeltaTime);
    }

    public void Shake(float amount)
    {
        shake = Mathf.Max(shake, amount);
    }
}

internal sealed class AdvancedUpgradeOption
{
    public readonly string Title;
    public readonly string Description;
    public readonly AdvancedUpgradeRarity Rarity;
    public readonly Action<AdvancedPlayerController> Apply;
    public readonly string Family;

    public float Weight
    {
        get
        {
            switch (Rarity)
            {
                case AdvancedUpgradeRarity.Uncommon: return 28f;
                case AdvancedUpgradeRarity.Epic: return 13f;
                case AdvancedUpgradeRarity.Legendary: return 4f;
                default: return 55f;
            }
        }
    }

    public string RarityName
    {
        get { return Rarity.ToString().ToUpperInvariant(); }
    }

    public Color RarityColor
    {
        get
        {
            switch (Rarity)
            {
                case AdvancedUpgradeRarity.Uncommon: return new Color(0.42f, 0.86f, 0.48f);
                case AdvancedUpgradeRarity.Epic: return new Color(0.72f, 0.42f, 1f);
                case AdvancedUpgradeRarity.Legendary: return new Color(1f, 0.72f, 0.22f);
                default: return new Color(0.82f, 0.88f, 0.95f);
            }
        }
    }

    public AdvancedUpgradeOption(string title, string description, AdvancedUpgradeRarity rarity, Action<AdvancedPlayerController> apply, string family = null)
    {
        Title = title;
        Description = description;
        Rarity = rarity;
        Apply = apply;
        Family = family;
    }
}

internal sealed class AdvancedPermanentUpgrade
{
    public readonly string Id;
    public readonly string Title;
    public readonly string Description;
    public readonly int MaxLevel;
    public readonly int BaseCost;
    public readonly int CostStep;
    public readonly Action<AdvancedPlayerController> ApplyOneLevel;

    public int Level;
    public int Cost { get { return BaseCost + Level * CostStep; } }

    public AdvancedPermanentUpgrade(string id, string title, string description, int maxLevel, int baseCost, int costStep, Action<AdvancedPlayerController> applyOneLevel)
    {
        Id = id;
        Title = title;
        Description = description;
        MaxLevel = maxLevel;
        BaseCost = baseCost;
        CostStep = costStep;
        ApplyOneLevel = applyOneLevel;
    }
}

internal enum AdvancedGameState
{
    MainMenu,
    Playing,
    Paused,
    LevelUp,
    PermanentUpgrades,
    Stats,
    GameOver
}

internal enum AdvancedPlayerClass
{
    Archer,
    Knight,
    Mage
}

internal enum AdvancedHeroAnimation
{
    Idle,
    Walk,
    Attack,
    Draw
}

internal enum AdvancedUpgradeRarity
{
    Common,
    Uncommon,
    Epic,
    Legendary
}

internal enum AdvancedStatusEffect
{
    Burn,
    Poison,
    Freeze
}

internal enum AdvancedProjectileKind
{
    Bullet,
    Arrow,
    Magic
}

internal enum AdvancedEnemyKind
{
    Melee,
    Ranged,
    Tank,
    GlassCannon,
    Exploder,
    Boss
}

internal static class AdvancedGameMath
{
    public static Vector2 Rotate(Vector2 vector, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        return new Vector2(vector.x * cos - vector.y * sin, vector.x * sin + vector.y * cos);
    }
}

internal static class AdvancedGameArt
{
    private const string PixelCrawlerRoot = "Assets/Pixel Crawler - Free Pack/";
    private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();
    private static readonly Dictionary<string, Texture2D> PixelCache = new Dictionary<string, Texture2D>();
    private static readonly Dictionary<string, Texture2D> AssetTextureCache = new Dictionary<string, Texture2D>();

    private static Sprite PixelCrawlerFrame(string key, string relativePath, int frameWidth, int frameHeight, int frame, float pixelsPerUnit)
    {
        return PixelCrawlerFrame(key, relativePath, frameWidth, frameHeight, frame, pixelsPerUnit, new Vector2(0.5f, 0.42f));
    }

    private static Sprite PixelCrawlerFrame(string key, string relativePath, int frameWidth, int frameHeight, int frame, float pixelsPerUnit, Vector2 pivot)
    {
        Texture2D texture = LoadPixelCrawlerTexture(relativePath);
        if (texture == null)
        {
            return null;
        }

        int frameCount = Mathf.Max(1, texture.width / frameWidth);
        int safeFrame = Mathf.Abs(frame) % frameCount;
        string cacheKey = "pc-frame-" + key + "-" + safeFrame + "-" + Mathf.RoundToInt(pivot.x * 100f) + "-" + Mathf.RoundToInt(pivot.y * 100f);
        Sprite sprite;
        if (SpriteCache.TryGetValue(cacheKey, out sprite)) return sprite;

        Rect rect = new Rect(safeFrame * frameWidth, texture.height - frameHeight, frameWidth, frameHeight);
        sprite = Sprite.Create(texture, rect, pivot, pixelsPerUnit);
        SpriteCache[cacheKey] = sprite;
        return sprite;
    }

    private static Sprite PixelCrawlerRegion(string key, string relativePath, int x, int yFromTop, int width, int height, float pixelsPerUnit, Vector2 pivot)
    {
        Texture2D texture = LoadPixelCrawlerTexture(relativePath);
        if (texture == null)
        {
            return null;
        }

        string cacheKey = "pc-region-" + key + "-" + x + "-" + yFromTop + "-" + width + "-" + height;
        Sprite sprite;
        if (SpriteCache.TryGetValue(cacheKey, out sprite)) return sprite;

        Rect rect = new Rect(x, texture.height - yFromTop - height, width, height);
        sprite = Sprite.Create(texture, rect, pivot, pixelsPerUnit);
        SpriteCache[cacheKey] = sprite;
        return sprite;
    }

    private static Texture2D LoadPixelCrawlerTexture(string relativePath)
    {
        Texture2D cachedTexture;
        if (AssetTextureCache.TryGetValue(relativePath, out cachedTexture))
        {
            return cachedTexture;
        }

        Texture2D fileTexture = LoadTextureFromProjectFile(relativePath);
        if (fileTexture != null)
        {
            AssetTextureCache[relativePath] = fileTexture;
            return fileTexture;
        }

#if UNITY_EDITOR
        string path = PixelCrawlerRoot + relativePath;
        Type assetDatabaseType = Type.GetType("UnityEditor.AssetDatabase, UnityEditor");
        if (assetDatabaseType == null)
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                assetDatabaseType = assembly.GetType("UnityEditor.AssetDatabase");
                if (assetDatabaseType != null)
                {
                    break;
                }
            }
        }

        if (assetDatabaseType == null)
        {
            return null;
        }

        System.Reflection.MethodInfo loadMethod = assetDatabaseType.GetMethod("LoadAssetAtPath", new Type[] { typeof(string), typeof(Type) });
        if (loadMethod == null)
        {
            return null;
        }

        Texture2D texture = loadMethod.Invoke(null, new object[] { path, typeof(Texture2D) }) as Texture2D;
        if (texture != null)
        {
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            AssetTextureCache[relativePath] = texture;
        }

        return texture;
#else
        return null;
#endif
    }

    private static Texture2D LoadTextureFromProjectFile(string relativePath)
    {
        try
        {
            string projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
            string assetPath = PixelCrawlerRoot.Replace("/", System.IO.Path.DirectorySeparatorChar.ToString());
            string texturePath = System.IO.Path.Combine(projectRoot, assetPath, relativePath.Replace("/", System.IO.Path.DirectorySeparatorChar.ToString()));
            if (System.IO.File.Exists(texturePath) == false)
            {
                return null;
            }

            byte[] bytes = System.IO.File.ReadAllBytes(texturePath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (ImageConversion.LoadImage(texture, bytes, false) == false)
            {
                return null;
            }

            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }
        catch
        {
            return null;
        }
    }

    private static Sprite PixelHeroSprite(AdvancedPlayerClass heroClass, AdvancedHeroAnimation animation, int frame)
    {
        string character;
        switch (heroClass)
        {
            case AdvancedPlayerClass.Knight:
                character = "Knight";
                break;
            case AdvancedPlayerClass.Mage:
                character = "Wizzard";
                break;
            default:
                character = "Rogue";
                break;
        }

        string motion = animation == AdvancedHeroAnimation.Walk ? "Run" : "Idle";
        if (motion == "Idle")
        {
            return PixelCrawlerFrame("hero-" + character + "-Idle", "Entities/Npc's/" + character + "/Idle/Idle-Sheet.png", 32, 32, frame, 32f);
        }

        return PixelCrawlerFrame("hero-" + character + "-Run", "Entities/Npc's/" + character + "/Run/Run-Sheet.png", 64, 64, frame, 32f, new Vector2(0.5f, 0.3f));
    }

    private static Sprite PixelEnemySprite(AdvancedEnemyKind kind, int frame)
    {
        switch (kind)
        {
            case AdvancedEnemyKind.Ranged:
                return PixelCrawlerFrame("enemy-skeleton-mage-run", "Entities/Mobs/Skeleton Crew/Skeleton - Mage/Run/Run-Sheet.png", 64, 64, frame, 32f, new Vector2(0.5f, 0.3f));
            case AdvancedEnemyKind.Tank:
                return PixelCrawlerFrame("enemy-orc-warrior-run", "Entities/Mobs/Orc Crew/Orc - Warrior/Run/Run-Sheet.png", 64, 64, frame, 32f, new Vector2(0.5f, 0.3f));
            case AdvancedEnemyKind.GlassCannon:
                return PixelCrawlerFrame("enemy-skeleton-rogue-run", "Entities/Mobs/Skeleton Crew/Skeleton - Rogue/Run/Run-Sheet.png", 64, 64, frame, 32f, new Vector2(0.5f, 0.3f));
            case AdvancedEnemyKind.Exploder:
                return PixelCrawlerFrame("enemy-orc-rogue-run", "Entities/Mobs/Orc Crew/Orc - Rogue/Run/Run-Sheet.png", 64, 64, frame, 32f, new Vector2(0.5f, 0.3f));
            case AdvancedEnemyKind.Boss:
                return PixelCrawlerFrame("enemy-orc-boss-run", "Entities/Mobs/Orc Crew/Orc/Run/Run-Sheet.png", 64, 64, frame, 32f, new Vector2(0.5f, 0.3f));
            default:
                return PixelCrawlerFrame("enemy-skeleton-warrior-run", "Entities/Mobs/Skeleton Crew/Skeleton - Warrior/Run/Run-Sheet.png", 64, 64, frame, 32f, new Vector2(0.5f, 0.3f));
        }
    }

    public static Sprite SquareSprite(Color color)
    {
        string key = "square-" + ColorKey(color);
        Sprite sprite;
        if (SpriteCache.TryGetValue(key, out sprite)) return sprite;

        Texture2D texture = new Texture2D(8, 8);
        texture.filterMode = FilterMode.Point;
        Color[] pixels = new Color[64];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        texture.SetPixels(pixels);
        texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f), 8f);
        SpriteCache[key] = sprite;
        return sprite;
    }

    public static Sprite HeroSprite(AdvancedPlayerClass heroClass, AdvancedHeroAnimation animation, int frame)
    {
        Sprite assetSprite = PixelHeroSprite(heroClass, animation, frame);
        if (assetSprite != null) return assetSprite;

        string key = "hero-" + heroClass + "-" + animation + "-" + frame;
        Sprite sprite;
        if (SpriteCache.TryGetValue(key, out sprite)) return sprite;

        Color body;
        Color accent;
        Color skin = new Color(0.86f, 0.66f, 0.48f, 1f);
        Color outline = new Color(0.08f, 0.08f, 0.1f, 1f);

        switch (heroClass)
        {
            case AdvancedPlayerClass.Knight:
                body = new Color(0.58f, 0.64f, 0.72f, 1f);
                accent = new Color(0.28f, 0.35f, 0.44f, 1f);
                break;
            case AdvancedPlayerClass.Mage:
                body = new Color(0.42f, 0.18f, 0.76f, 1f);
                accent = new Color(0.9f, 0.72f, 1f, 1f);
                break;
            default:
                body = new Color(0.28f, 0.58f, 0.28f, 1f);
                accent = new Color(0.84f, 0.68f, 0.34f, 1f);
                break;
        }

        float walkShift = animation == AdvancedHeroAnimation.Walk ? Mathf.Sin(frame * Mathf.PI * 0.5f) * 0.08f : 0f;
        float attackLean = animation == AdvancedHeroAnimation.Attack || animation == AdvancedHeroAnimation.Draw ? 0.08f : 0f;
        Texture2D texture = TransparentTexture(64);

        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float nx = (x + 0.5f) / 64f * 2f - 1f;
                float ny = (y + 0.5f) / 64f * 2f - 1f;
                Color pixel = new Color(0f, 0f, 0f, 0f);

                bool cloak = heroClass == AdvancedPlayerClass.Mage && ny < 0.2f && ny > -0.8f && Mathf.Abs(nx) < 0.42f + (0.2f - ny) * 0.28f;
                bool bodyMask = ny > -0.62f && ny < 0.42f && Mathf.Abs(nx - attackLean) < 0.36f;
                bool headMask = (nx * nx / 0.16f + (ny - 0.48f) * (ny - 0.48f) / 0.13f) < 1f;
                bool hoodOrHelm = heroClass != AdvancedPlayerClass.Archer && (nx * nx / 0.24f + (ny - 0.5f) * (ny - 0.5f) / 0.18f) < 1f;
                bool leftLeg = ny < -0.45f && ny > -0.9f && Mathf.Abs(nx + 0.14f + walkShift) < 0.11f;
                bool rightLeg = ny < -0.45f && ny > -0.9f && Mathf.Abs(nx - 0.14f - walkShift) < 0.11f;
                bool leftArm = ny > -0.28f && ny < 0.18f && Mathf.Abs(nx + 0.42f - attackLean) < 0.12f;
                bool rightArm = ny > -0.28f && ny < 0.18f && Mathf.Abs(nx - 0.42f - attackLean) < 0.12f;

                bool outlineMask = ny > -0.88f && ny < 0.72f && Mathf.Abs(nx) < 0.56f && (Mathf.Abs(nx) > 0.48f || ny < -0.78f || ny > 0.62f);
                if (outlineMask) pixel = outline;
                if (cloak || bodyMask || leftLeg || rightLeg || leftArm || rightArm) pixel = body;
                if (leftLeg || rightLeg) pixel = new Color(body.r * 0.7f, body.g * 0.7f, body.b * 0.7f, 1f);
                if (headMask) pixel = heroClass == AdvancedPlayerClass.Knight ? new Color(0.72f, 0.76f, 0.82f, 1f) : skin;
                if (hoodOrHelm && ny > 0.42f) pixel = heroClass == AdvancedPlayerClass.Knight ? accent : body;

                bool chest = ny > -0.25f && ny < 0.18f && Mathf.Abs(nx) < 0.18f;
                if (chest) pixel = accent;

                bool eye = ny > 0.45f && ny < 0.53f && Mathf.Abs(nx) < 0.16f;
                if (eye) pixel = new Color(0.05f, 0.06f, 0.08f, 1f);

                texture.SetPixel(x, y, pixel);
            }
        }

        texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f);
        SpriteCache[key] = sprite;
        return sprite;
    }

    public static Sprite WeaponSprite(AdvancedPlayerClass heroClass, int frame)
    {
        Sprite assetSprite = WeaponAssetSprite(heroClass, frame);
        if (assetSprite != null)
        {
            return assetSprite;
        }

        string key = "weapon-" + heroClass + "-" + frame;
        Sprite sprite;
        if (SpriteCache.TryGetValue(key, out sprite)) return sprite;

        Texture2D texture = TransparentTexture(64);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float nx = (x + 0.5f) / 64f * 2f - 1f;
                float ny = (y + 0.5f) / 64f * 2f - 1f;
                Color pixel = new Color(0f, 0f, 0f, 0f);

                if (heroClass == AdvancedPlayerClass.Knight)
                {
                    float swing = (frame % 4 - 1.5f) * 0.08f;
                    bool blade = Mathf.Abs(nx - swing) < 0.055f && ny > -0.25f && ny < 0.88f;
                    bool guard = Mathf.Abs(ny + 0.24f) < 0.045f && Mathf.Abs(nx) < 0.34f;
                    bool handle = Mathf.Abs(nx) < 0.06f && ny > -0.72f && ny < -0.22f;
                    if (blade) pixel = new Color(0.9f, 0.95f, 1f, 1f);
                    if (guard) pixel = new Color(0.78f, 0.64f, 0.22f, 1f);
                    if (handle) pixel = new Color(0.34f, 0.22f, 0.12f, 1f);
                }
                else if (heroClass == AdvancedPlayerClass.Mage)
                {
                    bool staff = Mathf.Abs(nx) < 0.045f && ny > -0.78f && ny < 0.64f;
                    bool orb = nx * nx / 0.12f + (ny - 0.72f) * (ny - 0.72f) / 0.12f < 1f;
                    if (staff) pixel = new Color(0.55f, 0.34f, 0.15f, 1f);
                    if (orb) pixel = frame % 2 == 0 ? new Color(0.75f, 0.35f, 1f, 1f) : new Color(0.95f, 0.75f, 1f, 1f);
                }
                else
                {
                    float draw = frame % 4 * 0.025f;
                    bool bow = Mathf.Abs(Mathf.Sqrt((nx + 0.1f) * (nx + 0.1f) / 0.28f + ny * ny / 0.95f) - 1f) < 0.05f && nx < 0.25f;
                    bool stringLine = Mathf.Abs(nx - 0.28f + draw) < 0.025f && Mathf.Abs(ny) < 0.76f;
                    bool arrow = Mathf.Abs(ny) < 0.025f && nx > -0.48f && nx < 0.62f;
                    if (bow) pixel = new Color(0.55f, 0.34f, 0.14f, 1f);
                    if (stringLine) pixel = new Color(0.9f, 0.86f, 0.75f, 1f);
                    if (arrow) pixel = new Color(0.86f, 0.76f, 0.44f, 1f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f);
        SpriteCache[key] = sprite;
        return sprite;
    }

    private static Sprite WeaponAssetSprite(AdvancedPlayerClass heroClass, int frame)
    {
        switch (heroClass)
        {
            case AdvancedPlayerClass.Knight:
                return PixelCrawlerRegion("weapon-knight-wood-sword", "Weapons/Wood/Wood.png", 32, 0, 16, 16, 16f, new Vector2(0.18f, 0.5f));
            case AdvancedPlayerClass.Mage:
                return PixelCrawlerRegion("weapon-mage-wood-staff", "Weapons/Wood/Wood.png", 96, 16, 16, 48, 24f, new Vector2(0.5f, 0.18f));
            default:
                int bowFrame = Mathf.Abs(frame) % 2;
                int x = bowFrame == 0 ? 48 : 64;
                return PixelCrawlerRegion("weapon-archer-wood-bow-" + bowFrame, "Weapons/Wood/Wood.png", x, 48, 16, 32, 24f, new Vector2(0.5f, 0.5f));
        }
    }

    public static Sprite PlayerProjectileSprite(AdvancedProjectileKind kind)
    {
        switch (kind)
        {
            case AdvancedProjectileKind.Arrow:
                return TwoToneSprite("projectile-arrow-v2", new Color(1f, 0.86f, 0.34f), new Color(0.25f, 0.14f, 0.04f), (x, y) => Mathf.Abs(y) < 0.12f && x > -0.82f && x < 0.76f, (x, y) => x > 0.46f && Mathf.Abs(y) < 0.24f);
            case AdvancedProjectileKind.Magic:
                return StarSprite("projectile-magic", new Color(0.82f, 0.4f, 1f), 9);
            default:
                return DiamondSprite("player-bullet", Color.white, new Color(0.5f, 0.95f, 1f));
        }
    }

    public static Sprite PlayerSprite()
    {
        string key = "player-v2";
        Sprite sprite;
        if (SpriteCache.TryGetValue(key, out sprite)) return sprite;

        Texture2D texture = TransparentTexture(64);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float nx = (x + 0.5f) / 64f * 2f - 1f;
                float ny = (y + 0.5f) / 64f * 2f - 1f;
                Color pixel = new Color(0f, 0f, 0f, 0f);
                bool body = ny > -0.72f && ny < 0.72f && Mathf.Abs(nx) < 0.44f + (ny + 0.72f) * 0.28f;
                bool cockpit = (nx * nx / 0.08f + (ny - 0.15f) * (ny - 0.15f) / 0.12f) < 1f;
                bool wing = ny < -0.08f && ny > -0.72f && Mathf.Abs(nx) > 0.34f && Mathf.Abs(nx) < 0.78f && Mathf.Abs(nx) + ny < 0.42f;
                if (body || wing) pixel = new Color(0.22f, 0.64f, 1f, 1f);
                if (cockpit) pixel = new Color(0.86f, 0.98f, 1f, 1f);
                if (ny > 0.62f && Mathf.Abs(nx) < 0.22f) pixel = new Color(1f, 0.77f, 0.24f, 1f);
                texture.SetPixel(x, y, pixel);
            }
        }

        texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f);
        SpriteCache[key] = sprite;
        return sprite;
    }

    public static Sprite EnemySprite(AdvancedEnemyKind kind)
    {
        return EnemySprite(kind, 0);
    }

    public static Sprite EnemySprite(AdvancedEnemyKind kind, int frame)
    {
        Sprite assetSprite = PixelEnemySprite(kind, frame);
        if (assetSprite != null) return assetSprite;

        return EnemyFallbackSprite(kind);
    }

    public static Sprite EnemyFallbackSprite(AdvancedEnemyKind kind)
    {
        switch (kind)
        {
            case AdvancedEnemyKind.Ranged: return DiamondSprite("enemy-ranged", new Color(0.77f, 0.35f, 1f), new Color(0.95f, 0.78f, 1f));
            case AdvancedEnemyKind.Tank: return BlockSprite("enemy-tank", new Color(0.34f, 0.86f, 0.42f), new Color(0.18f, 0.45f, 0.22f));
            case AdvancedEnemyKind.GlassCannon: return TriangleSprite("enemy-glass", new Color(1f, 0.86f, 0.22f), new Color(1f, 0.48f, 0.12f));
            case AdvancedEnemyKind.Exploder: return StarSprite("enemy-exploder", new Color(1f, 0.45f, 0.12f), 10);
            case AdvancedEnemyKind.Boss: return BossSprite();
            default: return StarSprite("enemy-melee", new Color(1f, 0.24f, 0.24f), 8);
        }
    }

    public static Sprite FloorSprite(int variant)
    {
        int safe = Mathf.Abs(variant);
        string key = "floor-grass-clean-" + safe % 8;
        Sprite sprite;
        if (SpriteCache.TryGetValue(key, out sprite)) return sprite;

        Color baseColor = Color.Lerp(new Color(0.17f, 0.31f, 0.16f), new Color(0.22f, 0.38f, 0.18f), (safe % 8) / 7f);
        Texture2D texture = new Texture2D(16, 16);
        texture.filterMode = FilterMode.Point;

        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                float noise = Mathf.Repeat(Mathf.Sin((x + safe * 11) * 7.13f + (y + safe * 3) * 13.71f) * 127.1f, 1f);
                float shade = noise > 0.84f ? 0.08f : noise < 0.08f ? -0.05f : 0f;
                texture.SetPixel(x, y, new Color(baseColor.r + shade, baseColor.g + shade, baseColor.b + shade, 1f));
            }
        }

        texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0f, 0f, 16f, 16f), new Vector2(0.5f, 0.5f), 16f);
        SpriteCache[key] = sprite;
        return sprite;
    }

    public static Sprite DecorSprite(int variant)
    {
        int choice = Mathf.Abs(variant) % 6;
        Sprite assetSprite = null;
        switch (choice)
        {
            case 0:
                assetSprite = PixelCrawlerRegion("decor-bush-a", "Environment/Props/Static/Vegetation.png", 0, 0, 32, 32, 32f, new Vector2(0.5f, 0.5f));
                break;
            case 1:
                assetSprite = PixelCrawlerRegion("decor-bush-b", "Environment/Props/Static/Vegetation.png", 64, 0, 32, 32, 32f, new Vector2(0.5f, 0.5f));
                break;
            case 2:
                assetSprite = PixelCrawlerRegion("decor-grass-a", "Environment/Props/Static/Vegetation.png", 0, 128, 32, 32, 32f, new Vector2(0.5f, 0.5f));
                break;
            case 3:
                assetSprite = PixelCrawlerRegion("decor-flowers-a", "Environment/Props/Static/Vegetation.png", 128, 128, 32, 32, 32f, new Vector2(0.5f, 0.5f));
                break;
            case 4:
                assetSprite = PixelCrawlerRegion("decor-rock-a", "Environment/Props/Static/Rocks.png", 0, 0, 48, 48, 48f, new Vector2(0.5f, 0.5f));
                break;
            default:
                assetSprite = PixelCrawlerRegion("decor-rock-b", "Environment/Props/Static/Rocks.png", 48, 48, 48, 48, 48f, new Vector2(0.5f, 0.5f));
                break;
        }

        if (assetSprite != null) return assetSprite;
        return SquareSprite(new Color(0.14f, 0.34f, 0.13f, 0.75f));
    }

    public static Sprite TreeSprite(int variant)
    {
        int choice = Mathf.Abs(variant) % 4;
        int x = choice == 0 ? 0 : choice == 1 ? 96 : choice == 2 ? 96 : 192;
        int y = choice == 2 ? 192 : 0;
        Sprite assetSprite = PixelCrawlerRegion("tree-model-03-" + choice, "Environment/Props/Static/Trees/Model_03/Size_04.png", x, y, 96, 192, 128f, new Vector2(0.5f, 0.28f));
        if (assetSprite != null) return assetSprite;
        return BlockSprite("tree-fallback-" + choice, new Color(0.16f, 0.42f, 0.15f), new Color(0.09f, 0.2f, 0.08f));
    }

    public static Sprite XpSprite() { return DiamondSprite("xp", new Color(0.35f, 0.82f, 1f), new Color(0.8f, 1f, 1f)); }
    public static Sprite PlayerBulletSprite() { return DiamondSprite("player-bullet", Color.white, new Color(0.5f, 0.95f, 1f)); }
    public static Sprite EnemyBulletSprite() { return CircleSprite(Color.white); }
    public static Sprite RockSprite() { return BlockSprite("rock", new Color(0.22f, 0.27f, 0.32f), new Color(0.13f, 0.16f, 0.2f)); }
    public static Sprite CrystalSprite() { return DiamondSprite("crystal", new Color(0.22f, 0.8f, 1f, 0.86f), new Color(0.75f, 1f, 1f, 0.92f)); }

    public static Sprite WallSprite()
    {
        return WallSprite(0);
    }

    public static Sprite WallSprite(int variant)
    {
        int choice = Mathf.Abs(variant) % 3;
        Color main = choice == 0 ? new Color(0.42f, 0.28f, 0.16f) : choice == 1 ? new Color(0.48f, 0.48f, 0.42f) : new Color(0.36f, 0.25f, 0.17f);
        Color accent = choice == 1 ? new Color(0.22f, 0.22f, 0.2f) : new Color(0.18f, 0.11f, 0.07f);
        return TwoToneSprite("wall-clear-" + choice, main, accent,
            (x, y) => Mathf.Abs(x) < 0.92f && Mathf.Abs(y) < 0.38f,
            (x, y) => Mathf.Abs(x) > 0.76f || Mathf.Abs(y) > 0.24f || Mathf.Abs(y + 0.02f) < 0.035f);
    }

    public static Sprite BuildingSprite(int variant)
    {
        string key = "building-clear-v3-" + Mathf.Abs(variant) % 6;
        Sprite sprite;
        if (SpriteCache.TryGetValue(key, out sprite)) return sprite;

        int style = Mathf.Abs(variant) % 6;
        Color wall = style % 2 == 0 ? new Color(0.56f, 0.34f, 0.18f) : new Color(0.42f, 0.38f, 0.31f);
        Color roof = style < 3 ? new Color(0.27f, 0.18f, 0.13f) : new Color(0.22f, 0.32f, 0.28f);
        Color trim = new Color(0.09f, 0.065f, 0.045f);
        Color window = new Color(0.72f, 0.9f, 1f, 0.9f);
        Color door = new Color(0.18f, 0.11f, 0.06f);

        Texture2D texture = TransparentTexture(64);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float nx = (x + 0.5f) / 64f * 2f - 1f;
                float ny = (y + 0.5f) / 64f * 2f - 1f;
                Color pixel = new Color(0f, 0f, 0f, 0f);

                bool body = Mathf.Abs(nx) < 0.78f && ny > -0.72f && ny < 0.42f;
                bool roofMask = Mathf.Abs(nx) < 0.9f - Mathf.Max(0f, ny - 0.25f) * 0.45f && ny > 0.12f && ny < 0.82f;
                bool outline = (body || roofMask) && (Mathf.Abs(nx) > 0.72f || ny < -0.66f || ny > 0.74f);
                bool roofStripe = roofMask && (Mathf.Abs(nx) < 0.035f || Mathf.Abs(ny - 0.47f) < 0.035f);
                bool plank = body && Mathf.Repeat((nx + 1f) * 5f + style * 0.3f, 1f) < 0.08f;
                bool windows = body && ny > -0.18f && ny < 0.16f && (Mathf.Abs(nx + 0.42f) < 0.13f || Mathf.Abs(nx - 0.42f) < 0.13f);
                bool doorMask = body && ny < -0.32f && Mathf.Abs(nx) < 0.18f;

                if (body) pixel = wall;
                if (roofMask) pixel = roof;
                if (roofStripe || plank) pixel = new Color(trim.r * 1.25f, trim.g * 1.25f, trim.b * 1.25f, 1f);
                if (windows) pixel = window;
                if (doorMask) pixel = door;
                if (outline) pixel = trim;
                texture.SetPixel(x, y, pixel);
            }
        }

        texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f);
        SpriteCache[key] = sprite;
        return sprite;
    }

    public static Sprite FootprintSprite()
    {
        string key = "hitbox-footprint-v1";
        Sprite sprite;
        if (SpriteCache.TryGetValue(key, out sprite)) return sprite;

        Texture2D texture = TransparentTexture(32);
        Color fill = new Color(0f, 0f, 0f, 0.1f);
        Color border = new Color(0.95f, 0.85f, 0.42f, 0.72f);
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                bool edge = x < 2 || y < 2 || x > 29 || y > 29;
                bool inner = x >= 2 && y >= 2 && x <= 29 && y <= 29;
                texture.SetPixel(x, y, edge ? border : inner ? fill : Color.clear);
            }
        }

        texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0f, 0f, 32f, 32f), new Vector2(0.5f, 0.5f), 32f);
        SpriteCache[key] = sprite;
        return sprite;
    }

    public static Sprite HealthSprite()
    {
        return MaskSprite("health", new Color(0.25f, 1f, 0.4f), (x, y) =>
        {
            bool vertical = Mathf.Abs(x) < 0.22f && Mathf.Abs(y) < 0.7f;
            bool horizontal = Mathf.Abs(y) < 0.22f && Mathf.Abs(x) < 0.7f;
            return vertical || horizontal ? 1f : 0f;
        });
    }

    public static Sprite MineSprite()
    {
        return TwoToneSprite("mine", new Color(0.12f, 0.13f, 0.15f), new Color(1f, 0.45f, 0.16f), (x, y) =>
        {
            float radius = Mathf.Sqrt(x * x + y * y);
            return radius < 0.68f || (Mathf.Abs(x) < 0.12f && Mathf.Abs(y) < 0.9f) || (Mathf.Abs(y) < 0.12f && Mathf.Abs(x) < 0.9f);
        }, (x, y) => Mathf.Sqrt(x * x + y * y) < 0.28f);
    }

    public static Texture2D Pixel(Color color)
    {
        string key = ColorKey(color);
        Texture2D texture;
        if (PixelCache.TryGetValue(key, out texture)) return texture;
        texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        PixelCache[key] = texture;
        return texture;
    }

    private static Sprite CircleSprite(Color color)
    {
        return MaskSprite("circle-" + ColorKey(color), color, (x, y) => Mathf.Clamp01((1f - Mathf.Sqrt(x * x + y * y)) * 14f));
    }

    private static Sprite StarSprite(string key, Color color, int points)
    {
        return MaskSprite(key, color, (x, y) =>
        {
            float angle = Mathf.Atan2(y, x);
            float radius = Mathf.Sqrt(x * x + y * y);
            float edge = 0.58f + 0.18f * Mathf.Sin(angle * points);
            return Mathf.Clamp01((edge - radius) * 14f);
        });
    }

    private static Sprite DiamondSprite(string key, Color color, Color highlight)
    {
        return TwoToneSprite(key, color, highlight, (x, y) => Mathf.Abs(x) + Mathf.Abs(y) < 0.86f, (x, y) => Mathf.Abs(x) + Mathf.Abs(y - 0.15f) < 0.35f);
    }

    private static Sprite TriangleSprite(string key, Color color, Color highlight)
    {
        return TwoToneSprite(key, color, highlight, (x, y) => y > -0.72f && y < 0.72f && Mathf.Abs(x) < (0.78f - y * 0.55f), (x, y) => y > -0.1f && y < 0.35f && Mathf.Abs(x) < 0.25f);
    }

    private static Sprite BlockSprite(string key, Color color, Color shadow)
    {
        return TwoToneSprite(key, color, shadow, (x, y) => Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) < 0.72f, (x, y) => x < -0.18f || y < -0.18f);
    }

    private static Sprite BossSprite()
    {
        return TwoToneSprite("boss-v2", new Color(1f, 0.22f, 0.55f), new Color(0.45f, 0.08f, 0.34f), (x, y) =>
        {
            float radius = Mathf.Sqrt(x * x + y * y);
            float angle = Mathf.Atan2(y, x);
            return radius < 0.76f + 0.1f * Mathf.Sin(angle * 8f);
        }, (x, y) => Mathf.Sqrt(x * x + y * y) < 0.34f);
    }

    private static Sprite MaskSprite(string key, Color color, Func<float, float, float> alphaMask)
    {
        Sprite sprite;
        if (SpriteCache.TryGetValue(key, out sprite)) return sprite;
        Texture2D texture = TransparentTexture(64);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float nx = (x + 0.5f) / 64f * 2f - 1f;
                float ny = (y + 0.5f) / 64f * 2f - 1f;
                float alpha = Mathf.Clamp01(alphaMask(nx, ny));
                texture.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
            }
        }

        texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f);
        SpriteCache[key] = sprite;
        return sprite;
    }

    private static Sprite TwoToneSprite(string key, Color color, Color accent, Func<float, float, bool> mainMask, Func<float, float, bool> accentMask)
    {
        Sprite sprite;
        if (SpriteCache.TryGetValue(key, out sprite)) return sprite;
        Texture2D texture = TransparentTexture(64);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float nx = (x + 0.5f) / 64f * 2f - 1f;
                float ny = (y + 0.5f) / 64f * 2f - 1f;
                Color pixel = new Color(0f, 0f, 0f, 0f);
                if (mainMask(nx, ny)) pixel = color;
                if (accentMask(nx, ny)) pixel = accent;
                texture.SetPixel(x, y, pixel);
            }
        }

        texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f);
        SpriteCache[key] = sprite;
        return sprite;
    }

    private static Texture2D TransparentTexture(int size)
    {
        Texture2D texture = new Texture2D(size, size);
        texture.filterMode = FilterMode.Point;
        Color clear = new Color(0f, 0f, 0f, 0f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                texture.SetPixel(x, y, clear);
            }
        }

        return texture;
    }

    private static string ColorKey(Color color)
    {
        return Mathf.RoundToInt(color.r * 255f) + "-"
            + Mathf.RoundToInt(color.g * 255f) + "-"
            + Mathf.RoundToInt(color.b * 255f) + "-"
            + Mathf.RoundToInt(color.a * 255f);
    }
}
