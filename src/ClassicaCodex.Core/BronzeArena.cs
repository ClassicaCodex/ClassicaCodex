using System.Numerics;

namespace ClassicaCodex.Core;

public enum BronzeEnemyKind { Serpent, Harpy, Boar, Cyclops, Gorgon, Hydra }
public enum BronzeBattleState { Fighting, Won, Lost }
public readonly record struct BronzeInput(Vector2 Move, bool Strike = false, bool Javelin = false,
    bool Magic = false, bool Dodge = false, bool Shield = false);

public sealed class BronzeEnemy
{
    public BronzeEnemyKind Kind { get; init; }
    public Vector2 Position { get; set; }
    public Vector2 Aim { get; set; }
    public float Health { get; set; }
    public float MaxHealth { get; init; }
    public float Clock { get; set; }
    public float Telegraph { get; set; }
    public float Flash { get; set; }
    public bool Boss { get; init; }
    public float Radius => Boss ? 16 : 10;
}

public sealed class BronzeShot
{
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }
    public float Life { get; set; } = 2.5f;
    public bool Hostile { get; set; }
    public bool Magic { get; init; }
    public bool SeaBlessed { get; init; }
    public bool Reflected { get; set; }
    public float Damage { get; set; }
}

public sealed class BronzeSpark
{
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; init; }
    public float Life { get; set; }
    public int Color { get; init; }
}

public sealed class BronzePickup
{
    public Vector2 Position { get; init; }
    public bool Healing { get; init; }
}

/// <summary>Fixed-step arena simulation. No windows, images, database or wall-clock dependency.</summary>
public sealed class BronzeArena
{
    public const int Width = 480, Height = 300;
    private readonly Random _random;
    private float _spawnClock = .9f, _strikeCooldown, _throwCooldown, _magicCooldown, _dodgeCooldown;
    private int _spawned;
    private bool _bossSpawned;
    private bool _magicHeld;
    private readonly HashSet<BronzeGiftId> _gifts;
    public IReadOnlySet<BronzeGiftId> Gifts => _gifts;
    public Dictionary<BronzeEnemyKind, int> DefeatCounts { get; } = new();
    public float ConcealTime { get; private set; }
    public bool HasGift(BronzeGiftId gift) => _gifts.Contains(gift);
    public int Level { get; }
    public int Score { get; private set; }
    public Vector2 Player { get; private set; } = new(240, 180);
    public Vector2 Facing { get; private set; } = new(1, 0);
    public float MaxHealth => 100 + (Level - 1) * 15 + (HasGift(BronzeGiftId.Hephaestus) ? 35 : 0);
    public float Health { get; private set; }
    public float Mana { get; private set; } = 100;
    public float Guard { get; private set; } = 100;
    public float Invulnerable { get; private set; }
    public float DodgeTime { get; private set; }
    public float StrikeTime { get; private set; }
    public float MagicTime { get; private set; }
    public int MagicCasts { get; private set; }
    public string MagicFeedback { get; private set; } = "";
    public float MagicFeedbackTime { get; private set; }
    public int MagicCost => HasGift(BronzeGiftId.Apollo) ? 25 : 35;
    public string MagicName => Level >= 4 ? "THUNDER RING" : Level >= 2 ? "SACRED FIRE" : "LOCKED";
    public string RangedName => HasGift(BronzeGiftId.Poseidon) ? "TRIDENT" : "JAVELIN";
    public string MagicReadiness => Level < 2 ? "CHAPTER II" : Mana < MagicCost ? "LOW MANA" : _magicCooldown > 0 ? "CHARGING" : "READY";
    public float Shake { get; private set; }
    public float Time { get; private set; }
    public bool Shielding { get; private set; }
    public int Kills { get; private set; }
    public int WaveSize => 5 + Level * 2;
    public int Remaining => WaveSize + 1 - Kills;
    public BronzeBattleState State { get; private set; }
    public List<BronzeEnemy> Enemies { get; } = new();
    public List<BronzeShot> Shots { get; } = new();
    public List<BronzeSpark> Sparks { get; } = new();
    public List<BronzePickup> Pickups { get; } = new();
    public string Weapon => Level >= 3 ? "XIPHOS + JAVELIN" : "DORY + JAVELIN";
    public string Blessing => Level >= 4 ? "ZEUS: THUNDER RING" : Level >= 2 ? "ATHENA: SACRED FIRE" : "MAGIC AT CHAPTER II";

    public BronzeArena(int level, int seed, IEnumerable<BronzeGiftId>? gifts = null)
    {
        _gifts = (gifts ?? Array.Empty<BronzeGiftId>()).ToHashSet();
        Level = Math.Clamp(level, 1, 10);
        Health = MaxHealth;
        _random = new Random(seed);
    }

    public void Update(float delta, BronzeInput input)
    {
        if (State != BronzeBattleState.Fighting || !float.IsFinite(delta) || delta <= 0) return;
        // Large timer gaps never teleport enemies through the player.
        var dt = Math.Min(delta, .05f);
        Time += dt;
        Invulnerable = Down(Invulnerable, dt); DodgeTime = Down(DodgeTime, dt);
        StrikeTime = Down(StrikeTime, dt); MagicTime = Down(MagicTime, dt); Shake = Down(Shake, dt * 15);
        MagicFeedbackTime = Down(MagicFeedbackTime, dt);
        var magicPressed = input.Magic && !_magicHeld; _magicHeld = input.Magic;
        _strikeCooldown = Down(_strikeCooldown, dt); _throwCooldown = Down(_throwCooldown, dt);
        _magicCooldown = Down(_magicCooldown, dt); _dodgeCooldown = Down(_dodgeCooldown, dt);
        ConcealTime = Down(ConcealTime, dt);
        Mana = Math.Min(100, Mana + dt * (HasGift(BronzeGiftId.Apollo) ? 14 : 7));
        var move = input.Move;
        if (!float.IsFinite(move.X) || !float.IsFinite(move.Y)) move = Vector2.Zero;
        if (move.LengthSquared() > 1) move = Vector2.Normalize(move);
        if (move.LengthSquared() > .01f && DodgeTime == 0) Facing = Vector2.Normalize(move);
        Shielding = input.Shield && Guard > 2 && DodgeTime == 0;
        Guard = Math.Clamp(Guard + dt * (Shielding ? -8 : 22), 0, 100);
        var dodgeCost = HasGift(BronzeGiftId.Hermes) ? 12 : 20;
        if (input.Dodge && _dodgeCooldown == 0 && Guard >= dodgeCost)
        {
            DodgeTime = .22f; Invulnerable = HasGift(BronzeGiftId.Hades) ? .7f : .3f;
            if (HasGift(BronzeGiftId.Hades)) ConcealTime = .7f;
            _dodgeCooldown = HasGift(BronzeGiftId.Hermes) ? .5f : .85f; Guard -= dodgeCost;
            Burst(Player, 8, 1);
        }
        Player = Clamp(Player + (DodgeTime > 0 ? Facing * 260 : move * (Shielding ? 52 : 100)) * dt
            * (HasGift(BronzeGiftId.Hermes) ? 1.25f : 1));

        if (input.Strike && _strikeCooldown == 0 && DodgeTime == 0)
        {
            _strikeCooldown = Level >= 3 ? .28f : .4f; StrikeTime = .16f;
            foreach (var enemy in Enemies)
            {
                var offset = enemy.Position - Player;
                if (offset.Length() <= (Level >= 3 ? 39 : 46) + enemy.Radius
                    && Vector2.Dot(SafeDirection(offset), Facing) > (Level >= 3 ? -.05f : .45f))
                    Hit(enemy, 24 + Level * 5, Facing * 7);
            }
        }
        if (input.Javelin && _throwCooldown == 0 && DodgeTime == 0)
        {
            _throwCooldown = .55f;
            var trident = HasGift(BronzeGiftId.Poseidon);
            for (var i = trident ? -1 : 0; i <= (trident ? 1 : 0); i++)
            {
                var angle = MathF.Atan2(Facing.Y, Facing.X) + i * .18f;
                Shots.Add(new BronzeShot { Position = Player + Facing * 14,
                    Velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 240,
                    Damage = (27 + Level * 3) * (trident ? 1.2f : 1), SeaBlessed = trident });
            }
        }
        var magicCost = MagicCost;
        if (input.Magic && Level >= 2 && Mana >= magicCost && _magicCooldown == 0)
        {
            Mana -= magicCost; _magicCooldown = .8f; MagicTime = .3f; Shake = 3;
            MagicCasts++; MagicFeedback = MagicName; MagicFeedbackTime = 1;
            if (Level >= 4)
            {
                foreach (var enemy in Enemies.Where(e => Vector2.Distance(e.Position, Player) < 100))
                    Hit(enemy, 45 + Level * 4, SafeDirection(enemy.Position - Player) * 12);
                Shots.RemoveAll(s => s.Hostile && Vector2.Distance(s.Position, Player) < 100);
                Burst(Player, 32, 1);
            }
            else
                for (var i = -1; i <= 1; i++)
                {
                    var angle = MathF.Atan2(Facing.Y, Facing.X) + i * .22f;
                    Shots.Add(new BronzeShot { Position = Player, Velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 195,
                        Damage = 45, Magic = true });
                }
        }
        else if (magicPressed)
        {
            MagicFeedback = Level < 2 ? "MAGIC AWAKENS IN CHAPTER II" : Mana < magicCost ? $"NEED {magicCost} MANA" : "MAGIC IS RECHARGING";
            MagicFeedbackTime = 1.2f;
        }
        _spawnClock -= dt;
        if (_spawned < WaveSize && _spawnClock <= 0 && Enemies.Count < 4 + Level)
        {
            Spawn(false); _spawned++; _spawnClock = Math.Max(.55f, 1.6f - Level * .13f);
        }
        if (_spawned == WaveSize && Enemies.Count == 0 && !_bossSpawned)
        {
            Spawn(true); _bossSpawned = true;
        }
        foreach (var enemy in Enemies)
        {
            if (enemy.Health <= 0) continue;
            enemy.Flash = Down(enemy.Flash, dt);
            enemy.Clock -= dt;
            var direction = SafeDirection(Player - enemy.Position);
            var distance = Vector2.Distance(Player, enemy.Position);
            if (enemy.Telegraph > 0)
            {
                enemy.Telegraph -= dt;
                if (enemy.Telegraph <= 0)
                {
                    if (enemy.Kind is BronzeEnemyKind.Gorgon or BronzeEnemyKind.Hydra or BronzeEnemyKind.Harpy)
                    {
                        var count = enemy.Kind == BronzeEnemyKind.Hydra ? 5 : 1;
                        for (var i = 0; i < count; i++)
                        {
                            var angle = MathF.Atan2(enemy.Aim.Y, enemy.Aim.X) + (i - (count - 1) / 2f) * .22f;
                            Shots.Add(new BronzeShot { Position = enemy.Position,
                                Velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (75 + Level * 7),
                                Hostile = true, Damage = 12 + Level * 2 });
                        }
                    }
                    else if (enemy.Kind is BronzeEnemyKind.Boar)
                        enemy.Position = Clamp(enemy.Position + enemy.Aim * Math.Min(40, distance));
                    else if (distance < 46) DamagePlayer(16 + Level * 3, enemy.Position);
                    enemy.Clock = enemy.Boss ? 1.2f : 2;
                }
            }
            else
            {
                var ranged = enemy.Kind is BronzeEnemyKind.Gorgon or BronzeEnemyKind.Hydra or BronzeEnemyKind.Harpy;
                var speed = enemy.Kind == BronzeEnemyKind.Serpent ? 34 : enemy.Kind == BronzeEnemyKind.Harpy ? 49 : 26;
                if (ConcealTime == 0 && (!ranged || distance > 115)) enemy.Position = Clamp(enemy.Position + direction * (speed + Level * 2) * dt);
                if (ConcealTime == 0 && enemy.Clock <= 0 && (ranged || distance < 75))
                {
                    enemy.Telegraph = enemy.Boss ? .8f : .55f; enemy.Aim = direction;
                }
            }
            if (Vector2.Distance(enemy.Position, Player) < enemy.Radius + 7)
                DamagePlayer(10 + Level * 2, enemy.Position);
        }
        foreach (var shot in Shots)
        {
            shot.Position += shot.Velocity * dt; shot.Life -= dt;
            if (shot.Hostile)
            {
                if (Vector2.Distance(shot.Position, Player) < 9)
                {
                    var blocked = DamagePlayer(shot.Damage, shot.Position - SafeDirection(shot.Velocity) * 10);
                    if (blocked && HasGift(BronzeGiftId.Athena))
                    {
                        shot.Hostile = false; shot.Reflected = true; shot.Velocity *= -1.7f; shot.Damage *= 2;
                        shot.Position = Player + SafeDirection(shot.Velocity) * 15; shot.Life = 2;
                    }
                    else shot.Life = 0;
                }
            }
            else
            {
                var target = Enemies.FirstOrDefault(e => e.Health > 0 && Vector2.Distance(e.Position, shot.Position) < e.Radius + 4);
                if (target != null) { Hit(target, shot.Damage, SafeDirection(shot.Velocity) * 5); shot.Life = 0; }
            }
        }
        Shots.RemoveAll(s => s.Life <= 0 || s.Position.X < 0 || s.Position.X > Width || s.Position.Y < 36 || s.Position.Y > 275);
        foreach (var enemy in Enemies.Where(e => e.Health <= 0))
        {
            Kills++; Score += enemy.Boss ? 1000 * Level : 100 * Level;
            DefeatCounts[enemy.Kind] = DefeatCounts.GetValueOrDefault(enemy.Kind) + 1;
            Burst(enemy.Position, enemy.Boss ? 30 : 12, 0);
            if (Kills % 3 == 0 || enemy.Boss) Pickups.Add(new BronzePickup { Position = enemy.Position, Healing = Kills % 2 == 0 });
        }
        Enemies.RemoveAll(e => e.Health <= 0);
        foreach (var pickup in Pickups.Where(p => Vector2.Distance(p.Position, Player) < 17).ToArray())
        {
            if (pickup.Healing) Health = Math.Min(MaxHealth, Health + 28); else Mana = Math.Min(100, Mana + 35);
            Score += 50; Burst(pickup.Position, 8, 1); Pickups.Remove(pickup);
        }
        foreach (var spark in Sparks) { spark.Position += spark.Velocity * dt; spark.Life -= dt; }
        Sparks.RemoveAll(s => s.Life <= 0);
        if (Health <= 0) State = BronzeBattleState.Lost;
        else if (_bossSpawned && Enemies.Count == 0) State = BronzeBattleState.Won;
    }

    private void Spawn(bool boss)
    {
        var side = _random.Next(4);
        var position = side switch { 0 => new Vector2(24, _random.Next(70, 250)),
            1 => new Vector2(456, _random.Next(70, 250)), 2 => new Vector2(_random.Next(50, 430), 57),
            _ => new Vector2(_random.Next(50, 430), 261) };
        if (Vector2.Distance(position, Player) < 90) position = new Vector2(480 - position.X, 320 - position.Y);
        var kind = boss ? (Level >= 4 ? BronzeEnemyKind.Hydra : Level >= 2 ? BronzeEnemyKind.Gorgon : BronzeEnemyKind.Cyclops)
            : (BronzeEnemyKind)_random.Next(Level >= 3 ? 5 : 3);
        var health = boss ? 110 + Level * 32 : 23 + Level * 7;
        Enemies.Add(new BronzeEnemy { Kind = kind, Position = Clamp(position), Boss = boss,
            Health = health, MaxHealth = health, Clock = 1.2f });
        Burst(position, 10, 2);
    }

    private void Hit(BronzeEnemy enemy, float damage, Vector2 push)
    {
        if (enemy.Health <= 0) return;
        enemy.Health -= damage; enemy.Flash = .12f;
        enemy.Position = Clamp(enemy.Position + push); Burst(enemy.Position, 5, 0);
    }

    private bool DamagePlayer(float damage, Vector2 source)
    {
        if (Invulnerable > 0) return false;
        var blockCost = HasGift(BronzeGiftId.Athena) ? 8 : 15;
        if (Shielding && Guard >= blockCost && Vector2.Dot(SafeDirection(source - Player), Facing) > .15f)
        {
            Guard -= blockCost; Invulnerable = .15f; Burst(Player + Facing * 8, 6, 1); return true;
        }
        Health = Math.Max(0, Health - damage * Math.Max(.55f, 1 - (Level - 1) * .08f)
            * (HasGift(BronzeGiftId.Hephaestus) ? .8f : 1));
        Invulnerable = .8f; Shake = 4; Burst(Player, 10, 2);
        return false;
    }

    private void Burst(Vector2 position, int count, int color)
    {
        for (var i = 0; i < count && Sparks.Count < 250; i++)
            Sparks.Add(new BronzeSpark { Position = position,
                Velocity = new Vector2(_random.Next(-50, 51), _random.Next(-50, 51)), Life = .2f + (float)_random.NextDouble() * .4f, Color = color });
    }
    private static float Down(float n, float dt) => Math.Max(0, n - dt);
    private static Vector2 Clamp(Vector2 p) => new(Math.Clamp(p.X, 20, 460), Math.Clamp(p.Y, 55, 262));
    private static Vector2 SafeDirection(Vector2 p) => p.LengthSquared() < .0001f ? Vector2.UnitX : Vector2.Normalize(p);
}
