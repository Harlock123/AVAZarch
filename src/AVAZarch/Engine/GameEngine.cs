namespace AVAZarch.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>A wave's start: its number called, and no aliens yet.</summary>
    Ready,
    /// <summary>The aliens are about.</summary>
    Fight,
    /// <summary>The lander destroyed: the wreck burns before the next one goes up.</summary>
    Down,
    /// <summary>Every alien shot down: the bonus for the land saved, before the next wave.</summary>
    Clear,
    /// <summary>The virus has overrun the land.</summary>
    Lost,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Shoot, EnemyShot, Hit, Blast, BigBlast, Spore, BombAway, Mutate, Land, Refuel, FuelLow, ExtraLife,
    ThrustOn, ThrustOff, WaveStart, WaveClear, GameOver, TitleTune, MusicStop,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 15;
    public const double AttractSeconds = 60;
    public const double HighScoreSeconds = 9;
    public const double ReadySeconds = 3, ClearSeconds = 4.5, DownSeconds = 3, LostSeconds = 5;

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true;
    private Phase _resume = Phase.Fight;
    private readonly int _seed;
    private int _games;

    public GameEngine(HighScoreTable? table = null, int seed = 1)
    {
        HighScores = table ?? HighScoreTable.Load();
        _seed = seed;
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Ready;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public int Score => World.Score;
    public string? Message { get; private set; }
    public string? SubMessage { get; private set; }
    public double MessageTime { get; private set; }
    /// <summary>The points the last wave cleared was worth.</summary>
    public int LastBonus { get; private set; }

    /// <summary>The bonus for a wave cleared: 50 points for each per cent of the land still clean.</summary>
    public static int ClearBonus(World w) => (int)Math.Round((1 - w.Land.Infection) * 100) * 50;

    private void Play(Sfx s)
    {
        // The demonstration plays in silence.
        if (!Silent && State != GameState.Attract) Sound?.Invoke(s);
    }

    private void Say(string text, string? sub = null, double seconds = 2.5)
    {
        Message = text;
        SubMessage = sub;
        MessageTime = seconds;
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = SubMessage = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || (c.Fire || c.Thrust) && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) ShowHighScores(-1);
                break;
            case GameState.Playing:
                Simulate(c);
                break;
            case GameState.GameOver:
                Sim.Step(World, default, aliens: true);
                World.Heard.Clear();
                if (StateTime > 4)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, World.Wave);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame()
    {
        World = new World();
        Sim.Start(World, _seed * 1000 + _games++);
        SetPhase(Phase.Ready);
        Message = SubMessage = null;
    }

    public void StartGame()
    {
        NewGame();
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
        Play(Sfx.MusicStop);
        Callout();
    }

    private void StartAttract()
    {
        Play(Sfx.MusicStop);
        NewGame();
        State = GameState.Attract;
        StateTime = 0;
        Pilot.Reset();
        Callout();
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Play(Sfx.ThrustOff);
        Play(Sfx.MusicStop);
        Message = SubMessage = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        Play(Sfx.ThrustOff);
        Play(Sfx.MusicStop);
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Play(Sfx.ThrustOff);
        World.Ship.Thrusting = false;
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Callout()
    {
        Say($"WAVE {World.Wave}", World.Wave == 1 ? "SHOOT THE SEEDERS BEFORE THE VIRUS TAKES THE LAND" : "GET READY", ReadySeconds);
        Play(Sfx.WaveStart);
    }

    private void Simulate(Controls c)
    {
        double dt = Field.Step;
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Ready:
                Sim.Step(w, c);
                if (w.Ship.Dead) { _resume = Phase.Ready; SetPhase(Phase.Down); break; }
                if (PhaseTime > ReadySeconds)
                {
                    Sim.SpawnWave(w);
                    SetPhase(Phase.Fight);
                }
                break;
            case Phase.Fight:
                Sim.Step(w, c);
                if (w.Ship.Dead) { _resume = Phase.Fight; SetPhase(Phase.Down); }
                else if (w.Cleared) WaveClear();
                break;
            case Phase.Clear:
                Sim.Step(w, c);
                if (w.Ship.Dead) { _resume = Phase.Clear; SetPhase(Phase.Down); break; }
                if (PhaseTime > ClearSeconds) NextWave();
                break;
            case Phase.Down:
                Sim.Step(w, default);
                if (PhaseTime > DownSeconds)
                {
                    w.Lives--;
                    Play(Sfx.ThrustOff);
                    if (w.Lives <= 0) { GameOver(); break; }
                    Sim.Respawn(w);
                    switch (_resume)
                    {
                        case Phase.Ready: SetPhase(Phase.Ready); Callout(); break;
                        case Phase.Clear: NextWave(); break;
                        default:
                            if (w.Cleared) WaveClear();
                            else SetPhase(Phase.Fight);
                            break;
                    }
                }
                break;
            case Phase.Lost:
                Sim.Step(w, c);
                if (PhaseTime > LostSeconds) GameOver();
                break;
        }
        if (Phase is not Phase.Lost && State != GameState.GameOver && w.Land.Infection >= Field.Overrun)
        {
            SetPhase(Phase.Lost);
            Say("THE LAND IS OVERRUN", "THE VIRUS HAS WON", LostSeconds);
            Play(Sfx.MusicStop);
            Play(Sfx.GameOver);
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void WaveClear()
    {
        var w = World;
        SetPhase(Phase.Clear);
        LastBonus = ClearBonus(w);
        Say($"WAVE {w.Wave} CLEARED", $"LAND SAVED {100 - (int)Math.Round(w.Land.Infection * 100)}%   BONUS {LastBonus}", ClearSeconds);
        Sim.AddScore(w, LastBonus);
        Play(Sfx.WaveClear);
    }

    private void NextWave()
    {
        var w = World;
        Sim.Heal(w);
        w.Wave++;
        SetPhase(Phase.Ready);
        Callout();
    }

    private void GameOver()
    {
        Play(Sfx.ThrustOff);
        if (Phase != Phase.Lost) Play(Sfx.GameOver);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into a wave's fight, past its callout.</summary>
    public void Go(int wave = 1)
    {
        NewGame();
        State = GameState.Playing;
        World.Wave = wave;
        Sim.SpawnWave(World);
        SetPhase(Phase.Fight);
        Message = null;
    }
}
