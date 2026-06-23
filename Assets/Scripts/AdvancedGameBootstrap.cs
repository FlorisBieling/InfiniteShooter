using System;
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
    private const string PointsKey = "InfiniteShooter.MetaPoints";
    private const string UpgradeKeyPrefix = "InfiniteShooter.Upgrade.";

    private readonly List<AdvancedEnemyController> enemies = new List<AdvancedEnemyController>();
    private readonly List<AdvancedUpgradeOption> levelChoices = new List<AdvancedUpgradeOption>();
    private readonly List<AdvancedPermanentUpgrade> permanentUpgrades = new List<AdvancedPermanentUpgrade>();
    private readonly List<Rect> solidRects = new List<Rect>();

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
    private GUIStyle bigNumberStyle;

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
        solidRects.Clear();
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
        CreateArena();
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
        GameObject floor = new GameObject("Floor");
        floor.transform.SetParent(sceneryRoot);
        floor.transform.localScale = new Vector3(220f, 220f, 1f);
        SpriteRenderer floorRenderer = floor.AddComponent<SpriteRenderer>();
        floorRenderer.sprite = AdvancedGameArt.SquareSprite(new Color(0.065f, 0.075f, 0.095f));
        floorRenderer.sortingOrder = -50;

        for (int x = -44; x <= 44; x += 4)
        {
            for (int y = -44; y <= 44; y += 4)
            {
                float tone = Mathf.Abs(Mathf.Sin(x * 17.17f + y * 3.91f));
                GameObject tile = new GameObject("Floor Tile");
                tile.transform.SetParent(sceneryRoot);
                tile.transform.position = new Vector3(x, y, 0.02f);
                tile.transform.localScale = new Vector3(3.86f, 3.86f, 1f);
                SpriteRenderer renderer = tile.AddComponent<SpriteRenderer>();
                renderer.sprite = AdvancedGameArt.SquareSprite(tone > 0.58f ? new Color(0.1f, 0.118f, 0.145f) : new Color(0.082f, 0.096f, 0.12f));
                renderer.sortingOrder = -45;
            }
        }

        Sprite grid = AdvancedGameArt.SquareSprite(new Color(0.24f, 0.3f, 0.36f, 0.34f));
        for (int i = -48; i <= 48; i += 4)
        {
            CreateLine("Grid V " + i, grid, new Vector3(i, 0f, 0f), new Vector3(0.025f, 100f, 1f));
            CreateLine("Grid H " + i, grid, new Vector3(0f, i, 0f), new Vector3(100f, 0.025f, 1f));
        }

        CreateCityLayout();
        CreateFloorDetails();
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
    }

    private void CreateFloorDetails()
    {
        Sprite decalSprite = AdvancedGameArt.SquareSprite(new Color(0.18f, 0.2f, 0.23f, 0.45f));
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
            decal.transform.localScale = new Vector3(Mathf.Lerp(0.5f, 1.4f, PseudoRandom(i, 71)), 0.08f, 1f);
            decal.transform.rotation = Quaternion.Euler(0f, 0f, PseudoRandom(i, 73) * 360f);
            SpriteRenderer renderer = decal.AddComponent<SpriteRenderer>();
            renderer.sprite = decalSprite;
            renderer.sortingOrder = -18;
        }
    }

    private void CreateBuilding(string name, Vector2 center, Vector2 size, int variant)
    {
        GameObject building = CreateSolidObject(name, center, size, AdvancedGameArt.BuildingSprite(variant), -10);
        building.transform.localScale = new Vector3(size.x, size.y, 1f);
    }

    private void CreateWall(string name, Vector2 center, Vector2 size)
    {
        GameObject wall = CreateSolidObject(name, center, size, AdvancedGameArt.WallSprite(), -8);
        wall.transform.localScale = new Vector3(size.x, size.y, 1f);
    }

    private GameObject CreateSolidObject(string name, Vector2 center, Vector2 size, Sprite sprite, int sortingOrder)
    {
        GameObject solid = new GameObject(name);
        solid.transform.SetParent(sceneryRoot);
        solid.transform.position = new Vector3(center.x, center.y, -0.05f);

        SpriteRenderer renderer = solid.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = sortingOrder;

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
        }

        if (Input.GetKeyDown(KeyCode.U) && (state == AdvancedGameState.Playing || state == AdvancedGameState.Paused))
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
        upgradeReturnState = returnState;
        state = AdvancedGameState.PermanentUpgrades;
        Time.timeScale = 0f;
    }

    private void ClosePermanentUpgrades()
    {
        state = upgradeReturnState;
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
        SpawnBullet("Player Bullet", true, origin, direction, damage, speed, pierce, color, 0.18f * sizeMultiplier, projectileKind, maxDistance, explosionRadius);
    }

    public void SpawnEnemyBullet(Vector2 origin, Vector2 direction, float damage, float speed)
    {
        SpawnBullet("Enemy Bullet", false, origin, direction, damage, speed, 0, new Color(1f, 0.28f, 0.55f), 0.22f, AdvancedProjectileKind.Bullet, 0f, 0f);
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

            enemy.TakeDamage(damage);
            hitSomething = true;
        }

        SpawnBurst(origin + direction * Mathf.Min(range, 1.4f), new Color(0.86f, 0.92f, 1f), hitSomething ? 10 : 4);
        if (cameraFollow != null)
        {
            cameraFollow.Shake(hitSomething ? 0.16f : 0.06f);
        }
    }

    public void DamageEnemiesInRadius(Vector2 origin, float radius, float damage)
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
                enemy.TakeDamage(damage);
            }
        }

        SpawnBurst(origin, new Color(0.8f, 0.35f, 1f), 14);
        if (cameraFollow != null)
        {
            cameraFollow.Shake(0.18f);
        }
    }

    public void EnemyKilled(AdvancedEnemyController enemy, int xpReward, Vector3 position, bool isBoss)
    {
        enemies.Remove(enemy);
        kills++;
        SpawnXpPickup(position, xpReward);
        SpawnBurst(position, isBoss ? new Color(1f, 0.28f, 0.65f) : enemy.VisualColor, isBoss ? 20 : 8);

        float pointChance = 0.045f + (player != null ? player.SalvageBonus : 0f);
        if (isBoss)
        {
            bossKills++;
            AwardMetaPoints(6 + wave / 5, "Boss defeated");
            SpawnHealthPickup(position, 35f);
            cameraFollow.Shake(0.45f);
        }
        else if (UnityEngine.Random.value < pointChance)
        {
            AwardMetaPoints(1, "Salvage found");
        }

        if (UnityEngine.Random.value < 0.055f)
        {
            SpawnHealthPickup(position, 18f);
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
        xpToNextLevel = Mathf.RoundToInt(xpToNextLevel * 1.25f + 18f);
        state = AdvancedGameState.LevelUp;
        Time.timeScale = 0f;
        CreateLevelChoices();
    }

    private void CreateLevelChoices()
    {
        levelChoices.Clear();
        List<AdvancedUpgradeOption> pool = CreateUpgradePool();
        for (int i = 0; i < 3 && pool.Count > 0; i++)
        {
            int index = UnityEngine.Random.Range(0, pool.Count);
            levelChoices.Add(pool[index]);
            pool.RemoveAt(index);
        }
    }

    private List<AdvancedUpgradeOption> CreateUpgradePool()
    {
        return new List<AdvancedUpgradeOption>
        {
            new AdvancedUpgradeOption("More Damage", "+25% bullet damage.", p => p.Damage *= 1.25f),
            new AdvancedUpgradeOption("Faster Fire", "Shoot 18% faster.", p => p.FireDelay = Mathf.Max(0.07f, p.FireDelay * 0.82f)),
            new AdvancedUpgradeOption("Move Speed", "+15% movement speed.", p => p.MoveSpeed *= 1.15f),
            new AdvancedUpgradeOption("Max Health", "+25 max HP and full heal.", p => p.IncreaseMaxHealth(25f)),
            new AdvancedUpgradeOption("Fast Rounds", "+25% bullet speed.", p => p.BulletSpeed *= 1.25f),
            new AdvancedUpgradeOption("Piercing", "Bullets hit 1 extra enemy.", p => p.PierceCount++),
            new AdvancedUpgradeOption("Regen", "Slowly heal during combat.", p => p.HealthRegen += 1.4f),
            new AdvancedUpgradeOption("Multi Shot", "Add 1 extra spread shot.", p => p.MultiShot = Mathf.Min(6, p.MultiShot + 1)),
            new AdvancedUpgradeOption("Big Bullets", "+20% bullet size and +10% damage.", p => p.GrowBullets()),
            new AdvancedUpgradeOption("Magnet", "XP pickups fly in from farther away.", p => p.MagnetRange += 1.4f),
            new AdvancedUpgradeOption("Dash Charge", "Dash cooldown is 25% shorter.", p => p.DashCooldown = Mathf.Max(0.65f, p.DashCooldown * 0.75f))
        };
    }

    private void ApplyUpgrade(AdvancedUpgradeOption upgrade)
    {
        upgrade.Apply(player);
        state = AdvancedGameState.Playing;
        Time.timeScale = 1f;
        ShowNotice(upgrade.Title + " acquired");
        TryLevelUp();
    }

    private void RerollLevelChoices()
    {
        if (metaPoints <= 0)
        {
            return;
        }

        metaPoints--;
        SaveProfile();
        CreateLevelChoices();
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
        hudStyle.fontSize = 18;
        hudStyle.normal.textColor = Color.white;

        smallStyle = new GUIStyle(GUI.skin.label);
        smallStyle.fontSize = 14;
        smallStyle.normal.textColor = new Color(0.82f, 0.88f, 0.95f);
        smallStyle.wordWrap = true;

        centeredSmallStyle = new GUIStyle(smallStyle);
        centeredSmallStyle.alignment = TextAnchor.MiddleCenter;

        titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = 30;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.alignment = TextAnchor.MiddleCenter;
        titleStyle.normal.textColor = Color.white;

        bigNumberStyle = new GUIStyle(titleStyle);
        bigNumberStyle.fontSize = 48;
        bigNumberStyle.normal.textColor = new Color(0.5f, 0.9f, 1f);

        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 17;
        buttonStyle.wordWrap = true;
        buttonStyle.alignment = TextAnchor.MiddleCenter;
    }

    private void DrawMainMenu()
    {
        Rect panel = CenterRect(590f, 620f);
        GUI.Box(panel, "");
        GUILayout.BeginArea(new Rect(panel.x + 34f, panel.y + 26f, panel.width - 68f, panel.height - 52f));
        GUILayout.Label("Infinite Shooter", titleStyle, GUILayout.Height(56f));
        GUILayout.Label("Top-down survivor prototype", centeredSmallStyle, GUILayout.Height(26f));
        GUILayout.Space(12f);
        GUILayout.Label("Upgrade points: " + metaPoints, bigNumberStyle, GUILayout.Height(58f));
        GUILayout.Space(10f);
        GUILayout.Label("Choose your hero", centeredSmallStyle, GUILayout.Height(24f));
        GUILayout.BeginHorizontal();
        DrawClassCard(AdvancedPlayerClass.Archer, "Archer", "Draws a bow and fires piercing arrows.");
        DrawClassCard(AdvancedPlayerClass.Knight, "Knight", "Armored melee fighter with a wide sword swing.");
        DrawClassCard(AdvancedPlayerClass.Mage, "Mage", "Shorter range wand blasts that explode.");
        GUILayout.EndHorizontal();
        GUILayout.Space(10f);

        if (GUILayout.Button("Start Run", buttonStyle, GUILayout.Height(58f)))
        {
            StartRun();
        }

        GUILayout.Space(10f);
        if (GUILayout.Button("Permanent Upgrades", buttonStyle, GUILayout.Height(54f)))
        {
            OpenPermanentUpgrades(AdvancedGameState.MainMenu);
        }

        GUILayout.Space(10f);
        if (GUILayout.Button("Quit", buttonStyle, GUILayout.Height(46f)))
        {
            Application.Quit();
        }

        GUILayout.Space(16f);
        GUILayout.Label("Controls: WASD move, mouse aim, left click shoot, Space dash, U upgrades, Esc pause.", centeredSmallStyle, GUILayout.Height(60f));
        GUILayout.EndArea();
    }

    private void DrawClassCard(AdvancedPlayerClass heroClass, string title, string description)
    {
        Color oldColor = GUI.backgroundColor;
        GUI.backgroundColor = selectedClass == heroClass ? new Color(0.42f, 0.74f, 1f) : Color.white;
        string prefix = selectedClass == heroClass ? "Selected\n" : "";
        if (GUILayout.Button(prefix + title + "\n" + description, buttonStyle, GUILayout.Height(94f), GUILayout.Width(154f)))
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

        Rect box = new Rect(18f, 16f, 365f, 178f);
        GUI.Box(box, "");
        DrawBar(new Rect(36f, 34f, 220f, 18f), player.HealthPercent, new Color(0.18f, 0.95f, 0.38f), new Color(0.2f, 0.05f, 0.07f));
        GUI.Label(new Rect(266f, 30f, 105f, 24f), Mathf.CeilToInt(player.Health) + "/" + Mathf.CeilToInt(player.MaxHealth), smallStyle);

        DrawBar(new Rect(36f, 64f, 220f, 18f), xpToNextLevel > 0 ? Mathf.Clamp01((float)xp / xpToNextLevel) : 0f, new Color(0.35f, 0.72f, 1f), new Color(0.05f, 0.1f, 0.2f));
        GUI.Label(new Rect(266f, 60f, 105f, 24f), xp + "/" + xpToNextLevel + " XP", smallStyle);

        DrawBar(new Rect(36f, 94f, 220f, 14f), player.DashFill, new Color(1f, 0.72f, 0.24f), new Color(0.18f, 0.12f, 0.05f));
        GUI.Label(new Rect(266f, 88f, 105f, 24f), "Dash", smallStyle);

        GUI.Label(new Rect(36f, 120f, 330f, 24f), "Level " + level + "   Wave " + wave + "   Kills " + kills, hudStyle);
        GUI.Label(new Rect(36f, 146f, 330f, 22f), "Next wave in " + Mathf.CeilToInt(Mathf.Max(0f, nextWaveTimer)) + "s   Enemies " + enemies.Count, smallStyle);
        GUI.Label(new Rect(36f, 167f, 330f, 22f), "Upgrade points " + metaPoints + " (+" + runPointsEarned + " this run)", smallStyle);
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
        Rect panel = CenterRect(420f, 330f);
        GUI.Box(panel, "");
        GUILayout.BeginArea(new Rect(panel.x + 28f, panel.y + 24f, panel.width - 56f, panel.height - 48f));
        GUILayout.Label("Paused", titleStyle, GUILayout.Height(52f));

        if (GUILayout.Button("Resume", buttonStyle, GUILayout.Height(52f))) ResumeGame();
        GUILayout.Space(8f);
        if (GUILayout.Button("Permanent Upgrades", buttonStyle, GUILayout.Height(52f))) OpenPermanentUpgrades(AdvancedGameState.Paused);
        GUILayout.Space(8f);
        if (GUILayout.Button("Restart Run", buttonStyle, GUILayout.Height(52f))) StartRun();
        GUILayout.Space(8f);
        if (GUILayout.Button("Main Menu", buttonStyle, GUILayout.Height(52f))) ShowMainMenu();

        GUILayout.EndArea();
    }

    private void DrawLevelUpPanel()
    {
        Rect panel = CenterRect(520f, 430f);
        GUI.Box(panel, "");
        GUILayout.BeginArea(new Rect(panel.x + 26f, panel.y + 22f, panel.width - 52f, panel.height - 44f));
        GUILayout.Label("Level up! Choose an upgrade", titleStyle, GUILayout.Height(52f));
        GUILayout.Space(8f);

        for (int i = 0; i < levelChoices.Count; i++)
        {
            AdvancedUpgradeOption upgrade = levelChoices[i];
            if (GUILayout.Button(upgrade.Title + "\n" + upgrade.Description, buttonStyle, GUILayout.Height(76f)))
            {
                ApplyUpgrade(upgrade);
            }

            GUILayout.Space(8f);
        }

        GUI.enabled = metaPoints > 0;
        if (GUILayout.Button("Reroll choices (1 upgrade point)", buttonStyle, GUILayout.Height(44f)))
        {
            RerollLevelChoices();
        }
        GUI.enabled = true;
        GUILayout.EndArea();
    }

    private void DrawPermanentUpgradePanel()
    {
        Rect panel = CenterRect(760f, 560f);
        GUI.Box(panel, "");
        GUILayout.BeginArea(new Rect(panel.x + 28f, panel.y + 22f, panel.width - 56f, panel.height - 44f));
        GUILayout.Label("Permanent Upgrades", titleStyle, GUILayout.Height(48f));
        GUILayout.Label("Spend upgrade points during or between runs. Points: " + metaPoints, centeredSmallStyle, GUILayout.Height(26f));
        GUILayout.Space(10f);

        for (int i = 0; i < permanentUpgrades.Count; i++)
        {
            AdvancedPermanentUpgrade upgrade = permanentUpgrades[i];
            GUILayout.BeginHorizontal(GUILayout.Height(54f));
            GUILayout.Label(upgrade.Title + "  " + upgrade.Level + "/" + upgrade.MaxLevel + "\n" + upgrade.Description, smallStyle, GUILayout.Width(470f));

            bool canBuy = upgrade.Level < upgrade.MaxLevel && metaPoints >= upgrade.Cost;
            GUI.enabled = canBuy;
            string label = upgrade.Level >= upgrade.MaxLevel ? "MAX" : "Buy (" + upgrade.Cost + ")";
            if (GUILayout.Button(label, buttonStyle, GUILayout.Width(150f), GUILayout.Height(48f)))
            {
                TryBuyPermanentUpgrade(upgrade);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Space(5f);
        }

        GUILayout.FlexibleSpace();
        if (GUILayout.Button(upgradeReturnState == AdvancedGameState.MainMenu ? "Back to Menu" : "Back", buttonStyle, GUILayout.Height(48f)))
        {
            ClosePermanentUpgrades();
        }

        GUILayout.EndArea();
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
        if (GUILayout.Button("Permanent Upgrades", buttonStyle, GUILayout.Height(50f))) OpenPermanentUpgrades(AdvancedGameState.GameOver);
        GUILayout.Space(8f);
        if (GUILayout.Button("Main Menu", buttonStyle, GUILayout.Height(50f))) ShowMainMenu();

        GUILayout.EndArea();
    }

    private Rect CenterRect(float width, float height)
    {
        return new Rect(Screen.width * 0.5f - width * 0.5f, Screen.height * 0.5f - height * 0.5f, width, height);
    }

    private string FormatTime(float seconds)
    {
        int total = Mathf.FloorToInt(seconds);
        return total / 60 + ":" + (total % 60).ToString("00");
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
    private float shotCooldown;
    private float invulnerabilityTimer;
    private float dashTimer;
    private float dashCooldownTimer;
    private float attackAnimTimer;
    private float drawTimer;
    private float bulletScale = 1f;
    private bool isDrawing;

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
    public int PierceCount { get; set; }
    public int MultiShot { get; set; }

    public float HealthPercent { get { return MaxHealth <= 0f ? 0f : Mathf.Clamp01(Health / MaxHealth); } }
    public float DashFill { get { return DashCooldown <= 0f ? 1f : 1f - Mathf.Clamp01(dashCooldownTimer / DashCooldown); } }

    public void Initialize(AdvancedGameWorld owner, AdvancedPlayerClass heroClass)
    {
        world = owner;
        playerClass = heroClass;
        ConfigureClassStats(heroClass);
        Health = MaxHealth;
        HealthRegen = 0f;
        MagnetRange = 2.2f;
        SalvageBonus = 0f;
        MultiShot = 1;
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
                MoveSpeed = 4.6f;
                Damage = 34f;
                FireDelay = 0.58f;
                BulletSpeed = 0f;
                MaxHealth = 140f;
                DashCooldown = 2.8f;
                break;
            case AdvancedPlayerClass.Mage:
                MoveSpeed = 4.85f;
                Damage = 25f;
                FireDelay = 0.72f;
                BulletSpeed = 8.4f;
                MaxHealth = 82f;
                break;
            default:
                MoveSpeed = 5.35f;
                Damage = 19f;
                FireDelay = 0.42f;
                BulletSpeed = 15.5f;
                MaxHealth = 92f;
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

        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(aimDirection.y, aimDirection.x) * Mathf.Rad2Deg - 90f);
        barrel.localPosition = new Vector3(0f, 0.54f, -0.01f);
        barrel.localRotation = Quaternion.identity;
        barrel.localScale = WeaponScale() * (attackAnimTimer > 0f ? 1.12f : 1f);
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
            world.PerformPlayerMelee(origin, aimDirection, 1.85f + bulletScale * 0.12f, 112f, Damage);
            return;
        }

        Vector2 magicOrigin = (Vector2)transform.position + aimDirection * 0.72f;
        world.SpawnPlayerBullet(magicOrigin, aimDirection, Damage, BulletSpeed, 0, bulletScale * 1.15f, AdvancedProjectileKind.Magic, 5.7f, 1.25f);
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
        int shots = Mathf.Max(1, MultiShot);
        float totalSpread = shots == 1 ? 0f : Mathf.Min(34f, 8.5f * (shots - 1));

        for (int i = 0; i < shots; i++)
        {
            float angle = shots == 1 ? 0f : -totalSpread * 0.5f + totalSpread * i / (shots - 1);
            Vector2 direction = AdvancedGameMath.Rotate(aimDirection, angle);
            Vector2 origin = (Vector2)transform.position + direction * (0.72f + bulletScale * 0.08f);
            world.SpawnPlayerBullet(origin, direction, Damage, BulletSpeed, PierceCount, bulletScale, AdvancedProjectileKind.Arrow, 0f, 0f);
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

        int frame = Mathf.FloorToInt(Time.time * (animation == AdvancedHeroAnimation.Walk ? 8f : 10f)) % 4;
        spriteRenderer.sprite = AdvancedGameArt.HeroSprite(playerClass, animation, frame);

        if (weaponRenderer != null)
        {
            int weaponFrame = animation == AdvancedHeroAnimation.Idle ? 0 : frame;
            weaponRenderer.sprite = AdvancedGameArt.WeaponSprite(playerClass, weaponFrame);
        }
    }

    public void TakeDamage(float amount)
    {
        if (invulnerabilityTimer > 0f || world.IsGameplayPaused)
        {
            return;
        }

        Health -= amount;
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

    public void GrowBullets()
    {
        bulletScale *= 1.2f;
        Damage *= 1.1f;
        transform.localScale = Vector3.one * Mathf.Min(1.35f, 1f + (bulletScale - 1f) * 0.15f);
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
    private int xpReward;
    private bool isDead;

    public Color VisualColor { get; private set; }

    public void Initialize(AdvancedGameWorld owner, AdvancedEnemyKind enemyKind, int wave)
    {
        world = owner;
        kind = enemyKind;
        ConfigureBaseStats();

        maxHealth *= 1f + (wave - 1) * 0.14f;
        health = maxHealth;
        damage *= 1f + (wave - 1) * 0.09f;
        speed *= 1f + Mathf.Min(0.32f, (wave - 1) * 0.012f);
        xpReward = Mathf.RoundToInt(xpReward * (1f + (wave - 1) * 0.045f));
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
        bodyRenderer.sortingOrder = kind == AdvancedEnemyKind.Boss ? 9 : 6;

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
        if (kind == AdvancedEnemyKind.Ranged) UpdateRanged();
        else if (kind == AdvancedEnemyKind.Boss) UpdateBoss();
        else UpdateMelee();
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
            Vector2 nextPosition = world.ResolveMovement(currentPosition, direction.normalized * speed * Time.deltaTime, kind == AdvancedEnemyKind.Boss ? 1.25f : 0.5f);
            transform.position = nextPosition;
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

    public void TakeDamage(float amount)
    {
        if (isDead) return;

        health -= amount;
        UpdateHealthBar();
        world.CreateFloatingText(Mathf.CeilToInt(amount).ToString(), transform.position + Vector3.up * 0.55f, VisualColor);
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
            bodyRenderer.color = Color.white;
        }
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
            transform.position += (Vector3)(toPlayer.normalized * speed * Time.deltaTime);
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

            enemy.TakeDamage(damage);
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
            AdvancedGameWorld.Instance.DamageEnemiesInRadius(transform.position, explosionRadius, damage);
        }

        Destroy(gameObject);
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
    public readonly Action<AdvancedPlayerController> Apply;

    public AdvancedUpgradeOption(string title, string description, Action<AdvancedPlayerController> apply)
    {
        Title = title;
        Description = description;
        Apply = apply;
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
    private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();
    private static readonly Dictionary<string, Texture2D> PixelCache = new Dictionary<string, Texture2D>();

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

    public static Sprite PlayerProjectileSprite(AdvancedProjectileKind kind)
    {
        switch (kind)
        {
            case AdvancedProjectileKind.Arrow:
                return TwoToneSprite("projectile-arrow", new Color(0.9f, 0.78f, 0.43f), new Color(0.28f, 0.18f, 0.08f), (x, y) => Mathf.Abs(y) < 0.08f && x > -0.8f && x < 0.72f, (x, y) => x > 0.48f && Mathf.Abs(y) < 0.18f);
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

    public static Sprite XpSprite() { return DiamondSprite("xp", new Color(0.35f, 0.82f, 1f), new Color(0.8f, 1f, 1f)); }
    public static Sprite PlayerBulletSprite() { return DiamondSprite("player-bullet", Color.white, new Color(0.5f, 0.95f, 1f)); }
    public static Sprite EnemyBulletSprite() { return CircleSprite(Color.white); }
    public static Sprite RockSprite() { return BlockSprite("rock", new Color(0.22f, 0.27f, 0.32f), new Color(0.13f, 0.16f, 0.2f)); }
    public static Sprite CrystalSprite() { return DiamondSprite("crystal", new Color(0.22f, 0.8f, 1f, 0.86f), new Color(0.75f, 1f, 1f, 0.92f)); }

    public static Sprite WallSprite()
    {
        return TwoToneSprite("wall-v1", new Color(0.35f, 0.37f, 0.39f), new Color(0.16f, 0.17f, 0.19f), (x, y) => Mathf.Abs(x) < 0.9f && Mathf.Abs(y) < 0.42f, (x, y) => Mathf.Abs(y) > 0.24f || Mathf.Abs(x) > 0.72f);
    }

    public static Sprite BuildingSprite(int variant)
    {
        string key = "building-v2-" + variant;
        Sprite sprite;
        if (SpriteCache.TryGetValue(key, out sprite)) return sprite;

        Color roof = variant % 2 == 0 ? new Color(0.22f, 0.26f, 0.31f) : new Color(0.26f, 0.23f, 0.2f);
        Color trim = new Color(0.1f, 0.115f, 0.135f);
        Color window = new Color(0.78f, 0.7f, 0.48f, 0.85f);

        Texture2D texture = TransparentTexture(64);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float nx = (x + 0.5f) / 64f * 2f - 1f;
                float ny = (y + 0.5f) / 64f * 2f - 1f;
                Color pixel = new Color(0f, 0f, 0f, 0f);

                bool body = Mathf.Abs(nx) < 0.88f && Mathf.Abs(ny) < 0.78f;
                bool trimMask = Mathf.Abs(nx) > 0.72f || Mathf.Abs(ny) > 0.62f;
                bool roofStripe = Mathf.Abs(ny + 0.02f) < 0.035f || Mathf.Abs(nx + 0.02f) < 0.035f;
                bool windows = Mathf.Abs(nx) < 0.62f && Mathf.Abs(ny) < 0.48f
                    && Mathf.Repeat((nx + 1f) * 4.2f, 1f) < 0.28f
                    && Mathf.Repeat((ny + 1f) * 3.4f, 1f) < 0.24f;

                if (body) pixel = roof;
                if (body && roofStripe) pixel = new Color(roof.r * 0.72f, roof.g * 0.72f, roof.b * 0.72f, 1f);
                if (body && trimMask) pixel = trim;
                if (body && windows) pixel = window;
                texture.SetPixel(x, y, pixel);
            }
        }

        texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f);
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
