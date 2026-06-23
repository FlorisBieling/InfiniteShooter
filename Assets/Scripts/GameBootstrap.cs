using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class GameBootstrap : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartGame()
    {
        // The expanded prototype is started by AdvancedGameBootstrap.
    }
}

internal sealed class GameWorld : MonoBehaviour
{
    public static GameWorld Instance { get; private set; }

    private const float SecondsPerWave = 30f;

    private readonly List<EnemyController> enemies = new List<EnemyController>();
    private readonly List<UpgradeOption> currentUpgradeChoices = new List<UpgradeOption>();

    private Transform enemyRoot;
    private Transform projectileRoot;
    private Transform sceneryRoot;
    private CameraFollow cameraFollow;
    private PlayerController player;

    private float nextWaveTimer;
    private float spawnTimer;
    private float survivedSeconds;
    private int pendingSpawns;
    private int wave;
    private int level;
    private int xp;
    private int xpToNextLevel;
    private bool choosingUpgrade;
    private bool gameOver;

    private GUIStyle hudStyle;
    private GUIStyle titleStyle;
    private GUIStyle buttonStyle;
    private GUIStyle smallStyle;

    public PlayerController Player
    {
        get { return player; }
    }

    public bool IsGameplayPaused
    {
        get { return choosingUpgrade || gameOver; }
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
        InitializeGame();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        Time.timeScale = 1f;
    }

    private void InitializeGame()
    {
        Time.timeScale = 1f;

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Destroy(transform.GetChild(i).gameObject);
        }

        enemies.Clear();
        currentUpgradeChoices.Clear();

        wave = 0;
        level = 1;
        xp = 0;
        xpToNextLevel = 35;
        nextWaveTimer = 0f;
        spawnTimer = 0f;
        pendingSpawns = 0;
        survivedSeconds = 0f;
        choosingUpgrade = false;
        gameOver = false;

        sceneryRoot = CreateChildRoot("Scenery");
        enemyRoot = CreateChildRoot("Enemies");
        projectileRoot = CreateChildRoot("Projectiles");

        CreateArena();
        CreatePlayer();
        CreateCamera();
        StartNextWave();
    }

    private Transform CreateChildRoot(string rootName)
    {
        GameObject child = new GameObject(rootName);
        child.transform.SetParent(transform);
        return child.transform;
    }

    private void CreateArena()
    {
        Sprite square = GameArt.SquareSprite(new Color(0.11f, 0.13f, 0.15f));
        GameObject floor = new GameObject("Endless Dark Floor");
        floor.transform.SetParent(sceneryRoot);
        floor.transform.localScale = new Vector3(220f, 220f, 1f);
        SpriteRenderer floorRenderer = floor.AddComponent<SpriteRenderer>();
        floorRenderer.sprite = square;
        floorRenderer.sortingOrder = -30;

        Sprite gridSprite = GameArt.SquareSprite(new Color(0.22f, 0.25f, 0.29f, 0.55f));
        for (int i = -50; i <= 50; i += 2)
        {
            CreateGridLine("Grid Vertical " + i, gridSprite, new Vector3(i, 0f, 0f), new Vector3(0.025f, 120f, 1f));
            CreateGridLine("Grid Horizontal " + i, gridSprite, new Vector3(0f, i, 0f), new Vector3(120f, 0.025f, 1f));
        }
    }

    private void CreateGridLine(string lineName, Sprite sprite, Vector3 position, Vector3 scale)
    {
        GameObject line = new GameObject(lineName);
        line.transform.SetParent(sceneryRoot);
        line.transform.position = position;
        line.transform.localScale = scale;
        SpriteRenderer renderer = line.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = -25;
    }

    private void CreatePlayer()
    {
        GameObject playerObject = new GameObject("Player");
        playerObject.transform.SetParent(transform);
        playerObject.transform.position = Vector3.zero;

        SpriteRenderer renderer = playerObject.AddComponent<SpriteRenderer>();
        renderer.sprite = GameArt.CircleSprite(new Color(0.25f, 0.68f, 1f));
        renderer.sortingOrder = 10;

        CircleCollider2D collider = playerObject.AddComponent<CircleCollider2D>();
        collider.radius = 0.46f;
        collider.isTrigger = true;

        player = playerObject.AddComponent<PlayerController>();
        player.Initialize(this);
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
        camera.orthographicSize = 7f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.06f, 0.07f, 0.085f);

        cameraFollow = cameraObject.AddComponent<CameraFollow>();
        cameraFollow.Target = player.transform;
    }

    private void Update()
    {
        if (gameOver || choosingUpgrade)
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
            spawnTimer = Mathf.Max(0.12f, 0.45f - wave * 0.012f);
            SpawnEnemy(ChooseEnemyKind());
        }
    }

    private void StartNextWave()
    {
        wave++;
        nextWaveTimer = SecondsPerWave;
        pendingSpawns += 6 + wave * 3;
    }

    private EnemyKind ChooseEnemyKind()
    {
        float roll = UnityEngine.Random.value;

        if (wave <= 1)
        {
            return EnemyKind.Melee;
        }

        if (wave == 2)
        {
            return roll < 0.75f ? EnemyKind.Melee : EnemyKind.Ranged;
        }

        if (wave == 3)
        {
            if (roll < 0.55f) return EnemyKind.Melee;
            if (roll < 0.82f) return EnemyKind.Ranged;
            return EnemyKind.Tank;
        }

        if (roll < 0.42f) return EnemyKind.Melee;
        if (roll < 0.66f) return EnemyKind.Ranged;
        if (roll < 0.83f) return EnemyKind.Tank;
        return EnemyKind.GlassCannon;
    }

    private void SpawnEnemy(EnemyKind kind)
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

        float distance = UnityEngine.Random.Range(10.5f, 14.5f);
        Vector3 spawnPosition = player.transform.position + (Vector3)(offset * distance);

        GameObject enemyObject = new GameObject(kind + " Enemy");
        enemyObject.transform.SetParent(enemyRoot);
        enemyObject.transform.position = spawnPosition;

        EnemyController enemy = enemyObject.AddComponent<EnemyController>();
        enemy.Initialize(this, kind, wave);
        enemies.Add(enemy);
    }

    public void SpawnPlayerBullet(Vector2 origin, Vector2 direction, float damage, float speed, int pierce, float sizeMultiplier)
    {
        SpawnBullet("Player Bullet", true, origin, direction, damage, speed, pierce, new Color(0.45f, 0.95f, 1f), 0.18f * sizeMultiplier);
    }

    public void SpawnEnemyBullet(Vector2 origin, Vector2 direction, float damage, float speed)
    {
        SpawnBullet("Enemy Bullet", false, origin, direction, damage, speed, 0, new Color(1f, 0.28f, 0.55f), 0.22f);
    }

    private void SpawnBullet(string bulletName, bool fromPlayer, Vector2 origin, Vector2 direction, float damage, float speed, int pierce, Color color, float size)
    {
        if (direction.sqrMagnitude < 0.01f)
        {
            return;
        }

        GameObject bulletObject = new GameObject(bulletName);
        bulletObject.transform.SetParent(projectileRoot);
        bulletObject.transform.position = origin;

        SpriteRenderer renderer = bulletObject.AddComponent<SpriteRenderer>();
        renderer.sprite = GameArt.CircleSprite(color);
        renderer.sortingOrder = fromPlayer ? 12 : 8;

        CircleCollider2D collider = bulletObject.AddComponent<CircleCollider2D>();
        collider.radius = 0.5f;
        collider.isTrigger = true;

        Rigidbody2D body = bulletObject.AddComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        bulletObject.transform.localScale = Vector3.one * size;

        Bullet bullet = bulletObject.AddComponent<Bullet>();
        bullet.Initialize(fromPlayer, direction.normalized, damage, speed, pierce);
    }

    public void EnemyKilled(EnemyController enemy, int xpReward)
    {
        enemies.Remove(enemy);
        AddXp(xpReward);
    }

    public void AddXp(int amount)
    {
        xp += amount;
        TryLevelUp();
    }

    private void TryLevelUp()
    {
        if (choosingUpgrade || gameOver || xp < xpToNextLevel)
        {
            return;
        }

        xp -= xpToNextLevel;
        level++;
        xpToNextLevel = Mathf.RoundToInt(xpToNextLevel * 1.25f + 18f);
        choosingUpgrade = true;
        Time.timeScale = 0f;

        currentUpgradeChoices.Clear();
        List<UpgradeOption> pool = CreateUpgradePool();
        for (int i = 0; i < 3 && pool.Count > 0; i++)
        {
            int index = UnityEngine.Random.Range(0, pool.Count);
            currentUpgradeChoices.Add(pool[index]);
            pool.RemoveAt(index);
        }
    }

    private List<UpgradeOption> CreateUpgradePool()
    {
        return new List<UpgradeOption>
        {
            new UpgradeOption("Meer schade", "+25% kogelschade.", p => p.Damage *= 1.25f),
            new UpgradeOption("Sneller schieten", "Vuursnelheid omhoog.", p => p.FireDelay = Mathf.Max(0.07f, p.FireDelay * 0.82f)),
            new UpgradeOption("Sneller lopen", "+15% bewegingssnelheid.", p => p.MoveSpeed *= 1.15f),
            new UpgradeOption("Meer leven", "+25 max HP en meteen genezen.", p => p.IncreaseMaxHealth(25f)),
            new UpgradeOption("Snellere kogels", "+25% kogelsnelheid.", p => p.BulletSpeed *= 1.25f),
            new UpgradeOption("Piercing", "Kogels raken 1 extra enemy.", p => p.PierceCount++),
            new UpgradeOption("Regeneratie", "Genees langzaam tijdens het vechten.", p => p.HealthRegen += 1.4f),
            new UpgradeOption("Dubbele loop", "Schiet 1 extra kogel in een spread.", p => p.MultiShot = Mathf.Min(5, p.MultiShot + 1)),
            new UpgradeOption("Grotere kogels", "+20% kogelgrootte en +10% schade.", p => p.GrowBullets())
        };
    }

    private void ApplyUpgrade(UpgradeOption upgrade)
    {
        upgrade.Apply(player);
        choosingUpgrade = false;
        Time.timeScale = 1f;
        TryLevelUp();
    }

    public void TriggerGameOver()
    {
        if (gameOver)
        {
            return;
        }

        gameOver = true;
        choosingUpgrade = false;
        Time.timeScale = 0f;
    }

    private void OnGUI()
    {
        EnsureGuiStyles();
        DrawHud();

        if (choosingUpgrade)
        {
            DrawUpgradePanel();
        }

        if (gameOver)
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

        titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = 28;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.alignment = TextAnchor.MiddleCenter;
        titleStyle.normal.textColor = Color.white;

        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 17;
        buttonStyle.wordWrap = true;
        buttonStyle.alignment = TextAnchor.MiddleCenter;
    }

    private void DrawHud()
    {
        const float x = 18f;
        const float y = 16f;
        const float width = 330f;
        const float height = 142f;

        GUI.Box(new Rect(x, y, width, height), "");

        float healthPercent = player != null ? player.HealthPercent : 0f;
        DrawBar(new Rect(x + 18f, y + 18f, 210f, 18f), healthPercent, new Color(0.18f, 0.95f, 0.38f), new Color(0.2f, 0.05f, 0.07f));
        GUI.Label(new Rect(x + 238f, y + 14f, 90f, 24f), player != null ? Mathf.CeilToInt(player.Health) + "/" + Mathf.CeilToInt(player.MaxHealth) : "0/0", smallStyle);

        float xpPercent = xpToNextLevel > 0 ? Mathf.Clamp01((float)xp / xpToNextLevel) : 0f;
        DrawBar(new Rect(x + 18f, y + 48f, 210f, 18f), xpPercent, new Color(0.35f, 0.72f, 1f), new Color(0.05f, 0.1f, 0.2f));
        GUI.Label(new Rect(x + 238f, y + 44f, 90f, 24f), xp + "/" + xpToNextLevel + " XP", smallStyle);

        GUI.Label(new Rect(x + 18f, y + 78f, 300f, 24f), "Level " + level + "   Wave " + wave + "   Enemies " + enemies.Count, hudStyle);
        GUI.Label(new Rect(x + 18f, y + 108f, 300f, 24f), "Volgende wave over " + Mathf.CeilToInt(Mathf.Max(0f, nextWaveTimer)) + "s", smallStyle);
    }

    private void DrawBar(Rect rect, float fill, Color fillColor, Color backgroundColor)
    {
        GUI.DrawTexture(rect, GameArt.Pixel(backgroundColor));
        Rect fillRect = rect;
        fillRect.width *= Mathf.Clamp01(fill);
        GUI.DrawTexture(fillRect, GameArt.Pixel(fillColor));
    }

    private void DrawUpgradePanel()
    {
        Rect panel = new Rect(Screen.width * 0.5f - 250f, Screen.height * 0.5f - 190f, 500f, 380f);
        GUI.Box(panel, "");

        GUILayout.BeginArea(new Rect(panel.x + 24f, panel.y + 20f, panel.width - 48f, panel.height - 40f));
        GUILayout.Label("Level up! Kies een upgrade", titleStyle, GUILayout.Height(52f));
        GUILayout.Space(12f);

        for (int i = 0; i < currentUpgradeChoices.Count; i++)
        {
            UpgradeOption upgrade = currentUpgradeChoices[i];
            if (GUILayout.Button(upgrade.Title + "\n" + upgrade.Description, buttonStyle, GUILayout.Height(78f)))
            {
                ApplyUpgrade(upgrade);
            }

            GUILayout.Space(8f);
        }

        GUILayout.EndArea();
    }

    private void DrawGameOverPanel()
    {
        Rect panel = new Rect(Screen.width * 0.5f - 230f, Screen.height * 0.5f - 120f, 460f, 240f);
        GUI.Box(panel, "");

        GUILayout.BeginArea(new Rect(panel.x + 24f, panel.y + 22f, panel.width - 48f, panel.height - 44f));
        GUILayout.Label("Game over", titleStyle, GUILayout.Height(48f));
        GUILayout.Label("Je haalde wave " + wave + " en level " + level + ".", hudStyle, GUILayout.Height(32f));
        GUILayout.Label("Overleefd: " + FormatTime(survivedSeconds), smallStyle, GUILayout.Height(28f));
        GUILayout.Space(18f);

        if (GUILayout.Button("Opnieuw starten", buttonStyle, GUILayout.Height(54f)))
        {
            InitializeGame();
        }

        GUILayout.EndArea();
    }

    private string FormatTime(float seconds)
    {
        int total = Mathf.FloorToInt(seconds);
        int minutes = total / 60;
        int remainingSeconds = total % 60;
        return minutes + ":" + remainingSeconds.ToString("00");
    }
}

internal sealed class PlayerController : MonoBehaviour
{
    private GameWorld world;
    private Transform barrel;
    private Vector2 aimDirection = Vector2.right;
    private float shotCooldown;
    private float invulnerabilityTimer;
    private float bulletScale = 1f;

    public float MoveSpeed { get; set; }
    public float Damage { get; set; }
    public float FireDelay { get; set; }
    public float BulletSpeed { get; set; }
    public float MaxHealth { get; private set; }
    public float Health { get; private set; }
    public float HealthRegen { get; set; }
    public int PierceCount { get; set; }
    public int MultiShot { get; set; }

    public float HealthPercent
    {
        get { return MaxHealth <= 0f ? 0f : Mathf.Clamp01(Health / MaxHealth); }
    }

    public void Initialize(GameWorld owner)
    {
        world = owner;
        MoveSpeed = 5.2f;
        Damage = 18f;
        FireDelay = 0.28f;
        BulletSpeed = 13.5f;
        MaxHealth = 100f;
        Health = MaxHealth;
        HealthRegen = 0f;
        PierceCount = 0;
        MultiShot = 1;

        GameObject barrelObject = new GameObject("Aim Barrel");
        barrelObject.transform.SetParent(transform);
        barrelObject.transform.localPosition = new Vector3(0.48f, 0f, -0.01f);
        barrelObject.transform.localScale = new Vector3(0.85f, 0.18f, 1f);

        SpriteRenderer barrelRenderer = barrelObject.AddComponent<SpriteRenderer>();
        barrelRenderer.sprite = GameArt.SquareSprite(new Color(1f, 0.72f, 0.2f));
        barrelRenderer.sortingOrder = 11;
        barrel = barrelObject.transform;
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

        if (invulnerabilityTimer > 0f)
        {
            invulnerabilityTimer -= Time.deltaTime;
        }
    }

    private void HandleMovement()
    {
        Vector2 input = Vector2.zero;
        if (Input.GetKey(KeyCode.W)) input.y += 1f;
        if (Input.GetKey(KeyCode.S)) input.y -= 1f;
        if (Input.GetKey(KeyCode.D)) input.x += 1f;
        if (Input.GetKey(KeyCode.A)) input.x -= 1f;

        if (input.sqrMagnitude > 1f)
        {
            input.Normalize();
        }

        transform.position += (Vector3)(input * MoveSpeed * Time.deltaTime);
    }

    private void HandleAiming()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            return;
        }

        Vector3 mouseWorld = camera.ScreenToWorldPoint(Input.mousePosition);
        Vector2 direction = (Vector2)(mouseWorld - transform.position);
        if (direction.sqrMagnitude > 0.05f)
        {
            aimDirection = direction.normalized;
        }

        barrel.right = aimDirection;
        barrel.localPosition = (Vector3)(aimDirection * 0.48f) + new Vector3(0f, 0f, -0.01f);
    }

    private void HandleShooting()
    {
        shotCooldown -= Time.deltaTime;
        if (Input.GetMouseButton(0) == false || shotCooldown > 0f)
        {
            return;
        }

        shotCooldown = FireDelay;
        int shots = Mathf.Max(1, MultiShot);
        float totalSpread = shots == 1 ? 0f : Mathf.Min(36f, 9f * (shots - 1));

        for (int i = 0; i < shots; i++)
        {
            float angle = shots == 1 ? 0f : -totalSpread * 0.5f + totalSpread * i / (shots - 1);
            Vector2 direction = GameMath.Rotate(aimDirection, angle);
            Vector2 origin = (Vector2)transform.position + direction * (0.72f + bulletScale * 0.08f);
            world.SpawnPlayerBullet(origin, direction, Damage, BulletSpeed, PierceCount, bulletScale);
        }
    }

    private void Regenerate()
    {
        if (HealthRegen <= 0f || Health >= MaxHealth)
        {
            return;
        }

        Health = Mathf.Min(MaxHealth, Health + HealthRegen * Time.deltaTime);
    }

    public void TakeDamage(float amount)
    {
        if (invulnerabilityTimer > 0f || world.IsGameplayPaused)
        {
            return;
        }

        Health -= amount;
        invulnerabilityTimer = 0.08f;

        if (Health <= 0f)
        {
            Health = 0f;
            world.TriggerGameOver();
        }
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

internal sealed class EnemyController : MonoBehaviour
{
    private GameWorld world;
    private EnemyKind kind;
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
    private int xpReward;
    private bool isDead;

    public void Initialize(GameWorld owner, EnemyKind enemyKind, int wave)
    {
        world = owner;
        kind = enemyKind;

        float healthScale = 1f + (wave - 1) * 0.14f;
        float damageScale = 1f + (wave - 1) * 0.09f;
        float speedScale = 1f + Mathf.Min(0.28f, (wave - 1) * 0.012f);

        ConfigureBaseStats();

        maxHealth *= healthScale;
        health = maxHealth;
        damage *= damageScale;
        speed *= speedScale;
        xpReward = Mathf.RoundToInt(xpReward * (1f + (wave - 1) * 0.045f));

        BuildVisuals();
    }

    private void ConfigureBaseStats()
    {
        switch (kind)
        {
            case EnemyKind.Ranged:
                maxHealth = 34f;
                speed = 2.15f;
                damage = 8f;
                attackRange = 8.2f;
                preferredRange = 5.3f;
                attackDelay = 1.45f;
                xpReward = 12;
                transform.localScale = Vector3.one * 0.92f;
                break;
            case EnemyKind.Tank:
                maxHealth = 150f;
                speed = 1.25f;
                damage = 9f;
                attackRange = 0.95f;
                preferredRange = 0f;
                attackDelay = 1.1f;
                xpReward = 20;
                transform.localScale = Vector3.one * 1.45f;
                break;
            case EnemyKind.GlassCannon:
                maxHealth = 24f;
                speed = 3.65f;
                damage = 17f;
                attackRange = 0.74f;
                preferredRange = 0f;
                attackDelay = 0.48f;
                xpReward = 14;
                transform.localScale = Vector3.one * 0.78f;
                break;
            default:
                maxHealth = 48f;
                speed = 2.55f;
                damage = 11f;
                attackRange = 0.78f;
                preferredRange = 0f;
                attackDelay = 0.78f;
                xpReward = 9;
                transform.localScale = Vector3.one;
                break;
        }
    }

    private void BuildVisuals()
    {
        bodyRenderer = gameObject.AddComponent<SpriteRenderer>();
        bodyRenderer.sprite = kind == EnemyKind.Tank ? GameArt.SquareSprite(EnemyColor()) : GameArt.CircleSprite(EnemyColor());
        bodyRenderer.sortingOrder = 6;

        CircleCollider2D collider = gameObject.AddComponent<CircleCollider2D>();
        collider.radius = 0.48f;
        collider.isTrigger = true;

        Rigidbody2D body = gameObject.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;

        GameObject barBack = new GameObject("Health Bar Back");
        barBack.transform.SetParent(transform);
        barBack.transform.localPosition = new Vector3(0f, 0.7f, -0.02f);
        barBack.transform.localScale = new Vector3(0.72f, 0.08f, 1f);
        SpriteRenderer backRenderer = barBack.AddComponent<SpriteRenderer>();
        backRenderer.sprite = GameArt.SquareSprite(new Color(0.03f, 0.03f, 0.04f));
        backRenderer.sortingOrder = 14;

        GameObject fill = new GameObject("Health Bar Fill");
        fill.transform.SetParent(transform);
        fill.transform.localPosition = new Vector3(0f, 0.7f, -0.03f);
        fill.transform.localScale = new Vector3(0.68f, 0.045f, 1f);
        SpriteRenderer fillRenderer = fill.AddComponent<SpriteRenderer>();
        fillRenderer.sprite = GameArt.SquareSprite(new Color(0.18f, 1f, 0.32f));
        fillRenderer.sortingOrder = 15;
        healthFill = fill.transform;
    }

    private Color EnemyColor()
    {
        switch (kind)
        {
            case EnemyKind.Ranged:
                return new Color(0.77f, 0.35f, 1f);
            case EnemyKind.Tank:
                return new Color(0.34f, 0.86f, 0.42f);
            case EnemyKind.GlassCannon:
                return new Color(1f, 0.86f, 0.22f);
            default:
                return new Color(1f, 0.24f, 0.24f);
        }
    }

    private void Update()
    {
        if (world == null || world.Player == null || world.IsGameplayPaused)
        {
            return;
        }

        attackTimer -= Time.deltaTime;

        if (kind == EnemyKind.Ranged)
        {
            UpdateRanged();
        }
        else
        {
            UpdateMelee();
        }
    }

    private void UpdateMelee()
    {
        Vector2 toPlayer = world.Player.transform.position - transform.position;
        float distance = toPlayer.magnitude;

        if (distance > attackRange)
        {
            Move(toPlayer.normalized);
            return;
        }

        if (attackTimer <= 0f)
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

        if (distance > preferredRange + 0.6f)
        {
            Move(direction);
        }
        else if (distance < preferredRange - 1.0f)
        {
            Move(-direction);
        }

        if (distance <= attackRange && attackTimer <= 0f)
        {
            Vector2 origin = (Vector2)transform.position + direction * 0.6f;
            world.SpawnEnemyBullet(origin, direction, damage, 7.6f);
            attackTimer = attackDelay;
        }
    }

    private void Move(Vector2 direction)
    {
        if (direction.sqrMagnitude < 0.01f)
        {
            return;
        }

        transform.position += (Vector3)(direction.normalized * speed * Time.deltaTime);
    }

    public void TakeDamage(float amount)
    {
        if (isDead)
        {
            return;
        }

        health -= amount;
        UpdateHealthBar();

        if (health <= 0f)
        {
            isDead = true;
            world.EnemyKilled(this, xpReward);
            Destroy(gameObject);
        }
    }

    private void UpdateHealthBar()
    {
        if (healthFill == null)
        {
            return;
        }

        float percent = Mathf.Clamp01(health / maxHealth);
        healthFill.localScale = new Vector3(0.68f * percent, 0.045f, 1f);
        healthFill.localPosition = new Vector3(-0.34f * (1f - percent), 0.7f, -0.03f);
    }
}

internal sealed class Bullet : MonoBehaviour
{
    private bool fromPlayer;
    private float damage;
    private int pierceRemaining;
    private float lifetime = 3.4f;
    private Rigidbody2D body;

    public void Initialize(bool playerBullet, Vector2 direction, float bulletDamage, float speed, int pierce)
    {
        fromPlayer = playerBullet;
        damage = bulletDamage;
        pierceRemaining = pierce;
        body = GetComponent<Rigidbody2D>();
        body.linearVelocity = direction.normalized * speed;
    }

    private void Update()
    {
        lifetime -= Time.deltaTime;
        if (lifetime <= 0f)
        {
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (fromPlayer)
        {
            EnemyController enemy = other.GetComponent<EnemyController>();
            if (enemy == null)
            {
                return;
            }

            enemy.TakeDamage(damage);
            if (pierceRemaining <= 0)
            {
                Destroy(gameObject);
            }
            else
            {
                pierceRemaining--;
            }

            return;
        }

        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null)
        {
            return;
        }

        player.TakeDamage(damage);
        Destroy(gameObject);
    }
}

internal sealed class CameraFollow : MonoBehaviour
{
    public Transform Target { get; set; }

    private void LateUpdate()
    {
        if (Target == null)
        {
            return;
        }

        Vector3 targetPosition = Target.position + new Vector3(0f, 0f, -10f);
        transform.position = Vector3.Lerp(transform.position, targetPosition, 10f * Time.deltaTime);
    }
}

internal sealed class UpgradeOption
{
    public readonly string Title;
    public readonly string Description;
    public readonly Action<PlayerController> Apply;

    public UpgradeOption(string title, string description, Action<PlayerController> apply)
    {
        Title = title;
        Description = description;
        Apply = apply;
    }
}

internal enum EnemyKind
{
    Melee,
    Ranged,
    Tank,
    GlassCannon
}

internal static class GameMath
{
    public static Vector2 Rotate(Vector2 vector, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        return new Vector2(vector.x * cos - vector.y * sin, vector.x * sin + vector.y * cos);
    }
}

internal static class GameArt
{
    private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();
    private static readonly Dictionary<string, Texture2D> PixelCache = new Dictionary<string, Texture2D>();

    public static Sprite SquareSprite(Color color)
    {
        string key = "square-" + ColorKey(color);
        Sprite sprite;
        if (SpriteCache.TryGetValue(key, out sprite))
        {
            return sprite;
        }

        Texture2D texture = new Texture2D(8, 8);
        texture.filterMode = FilterMode.Point;
        Color[] pixels = new Color[64];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }

        texture.SetPixels(pixels);
        texture.Apply();

        sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 8f);
        SpriteCache[key] = sprite;
        return sprite;
    }

    public static Sprite CircleSprite(Color color)
    {
        string key = "circle-" + ColorKey(color);
        Sprite sprite;
        if (SpriteCache.TryGetValue(key, out sprite))
        {
            return sprite;
        }

        const int size = 64;
        Texture2D texture = new Texture2D(size, size);
        texture.filterMode = FilterMode.Bilinear;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01((1f - distance) * 14f);
                texture.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
            }
        }

        texture.Apply();

        sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        SpriteCache[key] = sprite;
        return sprite;
    }

    public static Texture2D Pixel(Color color)
    {
        string key = ColorKey(color);
        Texture2D texture;
        if (PixelCache.TryGetValue(key, out texture))
        {
            return texture;
        }

        texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        PixelCache[key] = texture;
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
