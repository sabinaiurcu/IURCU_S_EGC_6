using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc.Teme;

/// <summary>
/// Varianta B: evită obstacolele. Drum cu trei benzi, jucătorul stă pe loc, obstacolele vin spre el.
///  - Stânga / dreapta (apăsare): schimbă banda, lin (Tween + EaseOut, 0,15 s);
///  - Space: salt cu gravitație (se pot sări obstacolele joase);
///  - obstacolele înalte se ocolesc; viteza lumii și frecvența obstacolelor cresc în timp;
///  - coliziune AABB; la coliziune jocul se oprește, titlul arată scorul și recordul, R = joc nou;
///  - bonus: marcaje pe drum care se mișcă odată cu lumea.
/// </summary>
public sealed class ScenaProprie : Scene
{
    private const string NewGame = "joc nou";

    // Drum și jucător
    private const float LaneWidth = 2.0f;
    private const float PlayerZ = 3.0f;
    private const float GroundY = 0.5f;            // înălțimea centrului cubului pe sol
    private const float Gravity = 28.0f;           // unități / s²
    private const float JumpSpeed = 9.0f;          // viteza verticală la salt
    private const float LaneChangeTime = 0.15f;    // secunde (sub 0,2 s)
    private static readonly Vector3 PlayerHalf = new(0.45f, 0.45f, 0.45f);   // hitbox ceva mai mic decât cubul

    // Obstacole și viteza lumii
    private const float SpawnZ = -70.0f;
    private const float DespawnZ = 10.0f;
    private const float BaseSpeed = 10.0f;
    private const float MaxSpeed = 28.0f;
    private const float Acceleration = 0.4f;      // unități / s², crește viteza în timp

    // Marcaje pe drum (bonus)
    private const int MarkCount = 14;
    private const float MarkSpacing = 6.0f;

    private readonly Random _rng = new();
    private readonly List<SceneObject> _obstacles = new();
    private readonly List<SceneObject> _marks = new();

    private Mesh _cube = null!;
    private SceneObject _player = null!;
    private SceneObject _shadow = null!;
    private Tween _laneTween = null!;

    private int _lane;                 // 0, 1, 2
    private float _y, _vy;             // poziția și viteza verticală
    private float _speed;              // viteza lumii
    private float _time;              // timpul supraviețuit (scorul)
    private float _best;               // recordul
    private float _spawnTimer;
    private bool _alive;

    public override string Name => "evită obstacolele";

    public override string Status => _alive
        ? $"scor {_time:F1} s | record {_best:F1} s"
        : $"GAME OVER | scor {_time:F1} s | record {_best:F1} s | R = joc nou";

    public override IEnumerable<string> ActionNames => new[]
    {
        Actions.Left, Actions.Right, Actions.Jump, NewGame,
    };

    public override IEnumerable<string> Describe()
    {
        foreach (string line in base.Describe()) yield return line;
        yield return $"Viteza lumii  : {_speed:F1}";
        yield return $"Scor / record : {_time:F1} s / {_best:F1} s";
    }

    public override void Load(Input input)
    {
        CameraPosition = new Vector3(0.0f, 4.5f, 9.0f);
        CameraTarget = new Vector3(0.0f, 0.8f, -8.0f);

        input.Bind(Actions.Left, Keys.A, Keys.Left);
        input.Bind(Actions.Right, Keys.D, Keys.Right);
        input.Bind(Actions.Jump, Keys.Space);
        input.Bind(NewGame, Keys.R);

        _cube = AddMesh(Shapes.ShadedCube());
        Mesh disk = AddMesh(Shapes.ShadedCylinder(0.5f, 0.02f, 24));

        // Drumul
        SceneObject road = AddObject("drum", _cube);
        road.Transform.Position = new Vector3(0.0f, 0.0f, -30.0f);
        road.Transform.Scale = new Vector3(3.0f * LaneWidth + 0.4f, 0.02f, 90.0f);
        road.Tint = new Color4(0.25f, 0.27f, 0.32f, 1.0f);

        // Marcajele dintre benzi (se mută odată cu lumea)
        foreach (float x in new[] { -LaneWidth / 2.0f, LaneWidth / 2.0f })
        {
            for (int i = 0; i < MarkCount; i++)
            {
                SceneObject mark = AddObject("marcaj", _cube);
                mark.Transform.Position = new Vector3(x, 0.02f, 6.0f - i * MarkSpacing);
                mark.Transform.Scale = new Vector3(0.1f, 0.02f, 2.0f);
                mark.Tint = new Color4(0.85f, 0.85f, 0.9f, 1.0f);
                _marks.Add(mark);
            }
        }

        // Jucătorul și umbra lui
        _shadow = AddObject("umbra jucătorului", disk);
        _shadow.Tint = new Color4(0.05f, 0.05f, 0.08f, 1.0f);

        _player = AddObject("jucător", _cube);

        // Ierarhie părinte-copil: o "antenă" care se rotește, copilul jucătorului. Poziția ei este
        // relativă la jucător, deci îl urmează la schimbarea benzii și la salt.
        SceneObject antenna = AddObject("antenă", _cube, _player);
        antenna.Transform.Position = new Vector3(0.0f, 0.75f, 0.0f);
        antenna.Transform.Scale = new Vector3(0.3f);
        antenna.Tint = new Color4(1.0f, 0.9f, 0.3f, 1.0f);
        antenna.Spin = new Vector3(0.0f, 180.0f, 0.0f);

        StartGame();
    }

    private static float LaneX(int lane) => (lane - 1) * LaneWidth;

    /// <summary>Pornește un joc nou: curăță obstacolele și resetează starea.</summary>
    private void StartGame()
    {
        foreach (SceneObject o in _obstacles) Objects.Remove(o);
        _obstacles.Clear();

        _lane = 1;
        _laneTween = new Tween(new Vector3(LaneX(_lane), 0.0f, 0.0f),
                               new Vector3(LaneX(_lane), 0.0f, 0.0f),
                               LaneChangeTime, Easing.EaseOut);
        _y = GroundY;
        _vy = 0.0f;
        _speed = BaseSpeed;
        _time = 0.0f;
        _spawnTimer = 1.0f;
        _alive = true;

        _player.Tint = new Color4(0.3f, 0.85f, 1.0f, 1.0f);
        UpdatePlayerTransform();
    }

    public override void Update(float dt, Input input)
    {
        base.Update(dt, input);

        if (input.WasPressed(NewGame))
        {
            StartGame();
            ConsolePanel.PrintMessage("joc nou");
        }

        if (!_alive || dt <= 0.0f) return;   // oprit (coliziune) sau în pauză

        // 1. Timpul și dificultatea cresc.
        _time += dt;
        _speed = MathF.Min(BaseSpeed + Acceleration * _time, MaxSpeed);

        // 2. Schimbarea benzii: apăsare -> Tween cu EaseOut spre banda vecină.
        if (input.WasPressed(Actions.Left) && _lane > 0)
        {
            _lane--;
            _laneTween.GoTo(new Vector3(LaneX(_lane), 0.0f, 0.0f), LaneChangeTime);
        }
        if (input.WasPressed(Actions.Right) && _lane < 2)
        {
            _lane++;
            _laneTween.GoTo(new Vector3(LaneX(_lane), 0.0f, 0.0f), LaneChangeTime);
        }
        _laneTween.Update(dt);

        // 3. Saltul cu gravitație (Euler semi-implicit: întâi viteza, apoi poziția).
        bool onGround = _y <= GroundY + 1e-4f;
        if (onGround && input.WasPressed(Actions.Jump))
        {
            _vy = JumpSpeed;
        }
        if (!onGround || _vy > 0.0f)
        {
            _vy -= Gravity * dt;
            _y += _vy * dt;
            if (_y <= GroundY)
            {
                _y = GroundY;
                _vy = 0.0f;
            }
        }
        UpdatePlayerTransform();

        // 4. Lumea se mișcă spre jucător: marcajele și obstacolele.
        foreach (SceneObject mark in _marks)
        {
            Vector3 p = mark.Transform.Position;
            p.Z += _speed * dt;
            if (p.Z > 8.0f) p.Z -= MarkCount * MarkSpacing;   // reapare în fundal
            mark.Transform.Position = p;
        }

        for (int i = _obstacles.Count - 1; i >= 0; i--)
        {
            SceneObject o = _obstacles[i];
            Vector3 p = o.Transform.Position;
            p.Z += _speed * dt;
            o.Transform.Position = p;

            if (p.Z > DespawnZ)
            {
                Objects.Remove(o);
                _obstacles.RemoveAt(i);
            }
        }

        // 5. Obstacole noi, din ce în ce mai dese.
        _spawnTimer -= dt;
        if (_spawnTimer <= 0.0f)
        {
            SpawnObstacle();
            _spawnTimer = MathF.Max(0.45f, 1.3f - 0.02f * _time);
        }

        // 6. Coliziunea AABB: cutiile se suprapun dacă se suprapun pe toate cele trei axe.
        Vector3 playerCenter = _player.Transform.Position;
        foreach (SceneObject o in _obstacles)
        {
            if (Overlap(playerCenter, PlayerHalf, o.Transform.Position, o.Transform.Scale * 0.5f))
            {
                GameOver();
                break;
            }
        }
    }

    private void UpdatePlayerTransform()
    {
        float x = _laneTween.Value.X;
        _player.Transform.Position = new Vector3(x, _y, PlayerZ);

        float height = _y - GroundY;
        float shadowSize = 1.1f / (1.0f + 0.25f * height);
        _shadow.Transform.Position = new Vector3(x, 0.03f, PlayerZ);
        _shadow.Transform.Scale = new Vector3(shadowSize, 1.0f, shadowSize);
    }

    private void SpawnObstacle()
    {
        int lane = _rng.Next(3);
        bool high = _rng.NextDouble() < 0.4;      // înalt = se ocolește, jos = se sare
        float h = high ? 2.5f : 0.6f;

        SceneObject o = AddObject(high ? "obstacol înalt" : "obstacol jos", _cube);
        o.Transform.Scale = new Vector3(1.6f, h, 1.0f);
        o.Transform.Position = new Vector3(LaneX(lane), h / 2.0f, SpawnZ);
        o.Tint = high ? new Color4(0.9f, 0.25f, 0.25f, 1.0f)     // roșu: ocolește
                      : new Color4(1.0f, 0.8f, 0.2f, 1.0f);      // galben: sari
        _obstacles.Add(o);
    }

    private void GameOver()
    {
        _alive = false;
        _player.Tint = new Color4(1.0f, 0.2f, 0.2f, 1.0f);
        if (_time > _best) _best = _time;
        ConsolePanel.PrintMessage($"coliziune! scor {_time:F1} s, record {_best:F1} s (R = joc nou)");
    }

    /// <summary>AABB: se suprapun pe X, pe Y ȘI pe Z (a = centru, ha = semi-dimensiuni).</summary>
    private static bool Overlap(Vector3 ca, Vector3 ha, Vector3 cb, Vector3 hb) =>
        MathF.Abs(ca.X - cb.X) < ha.X + hb.X &&
        MathF.Abs(ca.Y - cb.Y) < ha.Y + hb.Y &&
        MathF.Abs(ca.Z - cb.Z) < ha.Z + hb.Z;
}