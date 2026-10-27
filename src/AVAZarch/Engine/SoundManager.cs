using AVAZarch.Audio;

namespace AVAZarch.Engine;

/// <summary>
/// Plays the game's sounds through OpenAL Soft, which mixes as many at once as the game needs. The
/// tunes and jingles play on the music channel, and the engine's roar loops on the ambience channel. In a
/// virtual machine a larger mixing buffer keeps the sound from breaking up.
/// </summary>
public sealed class SoundManager : IDisposable
{
    private readonly IAudioEngine _engine;
    private readonly string _folder;

    public SoundManager(bool silent = false)
    {
        _folder = Path.Combine(AppContext.BaseDirectory, "Sounds");
        _engine = silent
            ? new NullAudioEngine("silent")
            : OpenAlAudioEngine.CreateOrSilent(AudioBuffer.PeriodFrames(AudioBuffer.Automatic, VirtualMachine.Detect()));
        _engine.SetVolumes(0.9f, 0.8f, 0.7f);
        _engine.SetAmbienceVolume(0.5f);
    }

    public bool IsAvailable => _engine.IsAvailable;

    public bool IsMuted
    {
        get => _muted;
        set
        {
            _muted = value;
            if (value) StopAll();
        }
    }
    private bool _muted;

    /// <summary>The file a sound plays from, or null for the ones that only stop something.</summary>
    public static string? FileName(Sfx s) => s switch
    {
        Sfx.MusicStop or Sfx.ThrustOff => null,
        Sfx.ThrustOn => "Thrust.wav",
        _ => s + ".wav",
    };

    private string File(Sfx s) => Path.Combine(_folder, FileName(s)!);

    public void Play(Sfx sfx)
    {
        if (_muted) return;
        switch (sfx)
        {
            case Sfx.TitleTune: _engine.PlayMusic(File(sfx), loop: true); return;
            case Sfx.GameOver or Sfx.WaveClear or Sfx.WaveStart: _engine.PlayMusic(File(sfx), loop: false); return;
            case Sfx.MusicStop: _engine.StopMusic(); return;
            // The engine's roar is a loop under everything while the thrust is on.
            case Sfx.ThrustOn: _engine.PlayAmbience(File(sfx)); return;
            case Sfx.ThrustOff: _engine.StopAmbience(); return;
        }
        _engine.PlayEffect(File(sfx), sfx switch { Sfx.Shoot => 0.4f, Sfx.Refuel => 0.35f, Sfx.EnemyShot => 0.55f, Sfx.Spore => 0.4f, _ => 1f });
    }

    public void StopAll()
    {
        _engine.StopAmbience();
        _engine.StopMusic();
    }

    public void Dispose() => _engine.Dispose();
}
