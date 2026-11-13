namespace Client;

/// <summary>
/// FMOD Core 기반 오디오 관리자. 호출은 한 곳에서 순차적으로 수행한다.
/// 상대 경로는 실행 파일 폴더 기준이며, Update를 주기적으로 호출해야 한다.
/// </summary>
sealed class SoundManager : ISoundManager
{
    public const string HitWeak = "hit_weak";
    public const string HitNormal = "hit_normal";
    public const string HitSuper = "hit_super";
    public const string HealthLow = "health_low";
    public const string Faint = "faint";
    public const string HealHp = "heal_hp";
    public const string HeldItemActivate = "held_item_activate";
    public const string PokeballThrow = "pokeball_throw";
    public const string PokeballRelease = "pokeball_release";
    public const string StatFall = "stat_fall";
    public const string StatUp = "stat_up";
    public const string StatusBurned = "status_burned";
    public const string StatusConfused = "status_confused";
    public const string StatusFrozen = "status_frozen";
    public const string StatusParalyzed = "status_paralyzed";
    public const string StatusPoisoned = "status_poisoned";
    public const string StatusSleep = "status_sleep";

    private static ISoundManager? instance;

    /// <summary>Program에서 초기화한 클라이언트 공용 오디오 관리자.</summary>
    public static ISoundManager Instance => instance
        ?? throw new InvalidOperationException("SoundManager를 먼저 초기화해야 합니다.");

    /// <summary>
    /// 시작 시 한 번 호출하고 반환한 인스턴스를 using으로 관리한다.
    /// FMOD 라이브러리가 없거나 초기화에 실패하면 소리 없이 동작하는 관리자를 반환한다.
    /// </summary>
    public static ISoundManager Initialize(int maxChannels = 128)
    {
        if (instance != null)
            throw new InvalidOperationException("SoundManager가 이미 초기화되어 있습니다.");

        try
        {
            instance = new SoundManager(maxChannels);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or InvalidOperationException)
        {
            // 예: macOS에 libfmod.dylib가 없는 경우. 사운드 때문에 게임이 멈추지 않게 한다.
            Console.Error.WriteLine($"사운드를 사용할 수 없어 소리 없이 실행합니다: {ex.Message}");
            instance = new SilentSoundManager();
        }
        return instance;
    }

    private FMOD.System system;
    private FMOD.ChannelGroup masterGroup;
    private FMOD.ChannelGroup bgmGroup;
    private FMOD.ChannelGroup soundGroup;
    private readonly Dictionary<string, FMOD.Sound> sounds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FMOD.Channel> playingSounds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string FirstId, FMOD.Channel Channel)> pendingSounds = new(StringComparer.Ordinal);
    private FMOD.Sound bgm;
    private FMOD.Channel bgmChannel;
    private FMOD.Sound bgmOverride;
    private FMOD.Channel bgmOverrideChannel;
    private long? bgmFadeStartedAt;
    private int bgmFadeMilliseconds;
    private bool disposed;

    private SoundManager(int maxChannels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChannels);
        Check(FMOD.Factory.System_Create(out system));
        try
        {
            Check(system.init(maxChannels, FMOD.INITFLAGS.NORMAL, IntPtr.Zero));
            Check(system.getMasterChannelGroup(out masterGroup));
            Check(system.createChannelGroup("BGM", out bgmGroup));
            Check(bgmGroup.setVolume(0.7f));
            Check(system.createChannelGroup("Sound", out soundGroup));
            Check(masterGroup.addGroup(bgmGroup));
            Check(masterGroup.addGroup(soundGroup));

            LoadSound(HitWeak, Path.Combine("resources", "sounds", "Hit Weak Not Very Effective.mp3"));
            LoadSound(HitNormal, Path.Combine("resources", "sounds", "Hit Normal Damage.mp3"));
            LoadSound(HitSuper, Path.Combine("resources", "sounds", "Hit Super Effective.mp3"));
            LoadSound(HealthLow, Path.Combine("resources", "sounds", "In-Battle Health Low.mp3"));
            LoadSound(Faint, Path.Combine("resources", "sounds", "In-Battle Faint No Health.mp3"));
            LoadSound(HealHp, Path.Combine("resources", "sounds", "In-Battle Heal HP Restore.mp3"));
            LoadSound(HeldItemActivate, Path.Combine("resources", "sounds", "In-Battle Held Item Activate.mp3"));
            LoadSound(PokeballThrow, Path.Combine("resources", "sounds", "pokeball-throw.mp3"));
            LoadSound(PokeballRelease, Path.Combine("resources", "sounds", "In-Battle Recall Switch Pokeball.mp3"));
            LoadSound(StatFall, Path.Combine("resources", "sounds", "Stat Fall Down.mp3"));
            LoadSound(StatUp, Path.Combine("resources", "sounds", "Stat Rise Up.mp3"));
            LoadSound(StatusBurned, Path.Combine("resources", "sounds", "Status Burned.mp3"));
            LoadSound(StatusConfused, Path.Combine("resources", "sounds", "Status Confused.mp3"));
            LoadSound(StatusFrozen, Path.Combine("resources", "sounds", "Status Frozen.mp3"));
            LoadSound(StatusParalyzed, Path.Combine("resources", "sounds", "Status Paralyzed.mp3"));
            LoadSound(StatusPoisoned, Path.Combine("resources", "sounds", "Status Poisoned.mp3"));
            LoadSound(StatusSleep, Path.Combine("resources", "sounds", "Status Sleep.mp3"));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>짧은 효과음을 메모리에 로드한다. 같은 ID의 중복 등록은 허용하지 않는다.</summary>
    public void LoadSound(string id, string path)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (sounds.ContainsKey(id))
            throw new ArgumentException($"이미 등록된 효과음입니다: {id}", nameof(id));

        Check(system.createSound(ResolvePath(path), FMOD.MODE._2D | FMOD.MODE.LOOP_OFF, out var sound));
        sounds.Add(id, sound);
    }

    /// <summary>등록한 효과음을 재생한다. 같은 효과음이 재생 중이면 중복 요청을 무시한다.</summary>
    public void PlaySound(string id)
    {
        ThrowIfDisposed();
        FMOD.Sound sound = GetSound(id);

        if (playingSounds.TryGetValue(id, out var playing) && IsPlaying(id, playing))
            return;

        Check(system.playSound(sound, soundGroup, false, out var channel));
        playingSounds[id] = channel;
    }

    /// <summary>첫 효과음이 끝나면 다음 효과음을 재생한다. 후속음이 대기·재생 중이면 중복 요청을 무시한다.</summary>
    public void PlaySoundSequence(string firstId, string nextId)
    {
        ThrowIfDisposed();
        GetSound(firstId);
        GetSound(nextId);
        if (pendingSounds.ContainsKey(nextId) ||
            (playingSounds.TryGetValue(nextId, out var next) && IsPlaying(nextId, next)))
            return;

        PlaySound(firstId);
        pendingSounds.Add(nextId, (firstId, playingSounds[firstId]));
    }

    public SoundPlaybackState GetSoundState(string id)
    {
        ThrowIfDisposed();
        GetSound(id);
        if (playingSounds.TryGetValue(id, out var channel) && IsPlaying(id, channel))
            return SoundPlaybackState.Playing;
        return pendingSounds.ContainsKey(id) ? SoundPlaybackState.Pending : SoundPlaybackState.Stopped;
    }

    public void StopSound(string id)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        pendingSounds.Remove(id);
        foreach (var (nextId, pending) in pendingSounds.ToArray())
            if (pending.FirstId == id)
                pendingSounds.Remove(nextId);
        if (playingSounds.Remove(id, out var channel) && IsPlaying(id, channel))
            Check(channel.stop());
    }

    /// <summary>BGM을 스트리밍으로 재생한다. 기존 BGM은 교체한다.</summary>
    public void PlayBgm(string path, bool loop = true)
    {
        ThrowIfDisposed();
        var mode = FMOD.MODE._2D | (loop ? FMOD.MODE.LOOP_NORMAL : FMOD.MODE.LOOP_OFF);
        Check(system.createStream(ResolvePath(path), mode, out var nextBgm));
        try
        {
            StopBgm();
            Check(system.playSound(nextBgm, bgmGroup, false, out bgmChannel));
            bgm = nextBgm;
        }
        catch
        {
            nextBgm.release();
            throw;
        }
    }

    /// <summary>기본 BGM의 재생 위치는 유지하고 음소거한 뒤 다른 BGM을 즉시 반복 재생한다.</summary>
    public void PlayBgmOverride(string path)
    {
        ThrowIfDisposed();
        if (!bgm.hasHandle())
            throw new InvalidOperationException("먼저 기본 BGM을 재생해야 합니다.");
        Check(system.createStream(ResolvePath(path), FMOD.MODE._2D | FMOD.MODE.LOOP_NORMAL, out var nextBgm));
        FMOD.Channel nextChannel = default;
        try
        {
            Check(system.playSound(nextBgm, bgmGroup, true, out nextChannel));
            ReleaseBgmOverride();
            bgmFadeStartedAt = null;
            Check(bgmChannel.setVolume(0));
            Check(nextChannel.setPaused(false));
            bgmOverride = nextBgm;
            bgmOverrideChannel = nextChannel;
        }
        catch
        {
            if (nextChannel.hasHandle()) nextChannel.stop();
            nextBgm.release();
            bgmChannel.setVolume(1);
            throw;
        }
    }

    /// <summary>덮어 재생하던 곡을 즉시 끄고 기본 BGM의 볼륨을 0에서 1로 복구한다.</summary>
    public void StopBgmOverride(int fadeInMilliseconds = 1000)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(fadeInMilliseconds);
        if (!bgmOverride.hasHandle()) return;
        ReleaseBgmOverride();
        if (fadeInMilliseconds == 0)
        {
            Check(bgmChannel.setVolume(1));
            bgmFadeStartedAt = null;
        }
        else
        {
            Check(bgmChannel.setVolume(0));
            bgmFadeMilliseconds = fadeInMilliseconds;
            bgmFadeStartedAt = Environment.TickCount64;
        }
    }

    private void ReleaseBgmOverride()
    {
        if (!bgmOverride.hasHandle()) return;
        Check(bgmOverrideChannel.stop());
        bgmOverrideChannel.clearHandle();
        Check(bgmOverride.release());
        bgmOverride.clearHandle();
    }

    public void StopBgm()
    {
        ThrowIfDisposed();
        bgmFadeStartedAt = null;
        ReleaseBgmOverride();
        Check(bgmGroup.stop());
        bgmChannel.clearHandle();
        if (bgm.hasHandle())
        {
            Check(bgm.release());
            bgm.clearHandle();
        }
    }

    public void StopAll()
    {
        ThrowIfDisposed();
        StopBgm();
        Check(soundGroup.stop());
        playingSounds.Clear();
        pendingSounds.Clear();
    }

    public void SetMasterVolume(float volume) => SetVolume(masterGroup, volume);
    public void SetBgmVolume(float volume) => SetVolume(bgmGroup, volume);
    public void SetSoundVolume(float volume) => SetVolume(soundGroup, volume);

    public void Update()
    {
        ThrowIfDisposed();
        if (bgmFadeStartedAt is long startedAt)
        {
            float progress = Math.Clamp((Environment.TickCount64 - startedAt) / (float)bgmFadeMilliseconds, 0, 1);
            Check(bgmChannel.setVolume(progress));
            if (progress >= 1) bgmFadeStartedAt = null;
        }
        Check(system.update());
        foreach (string id in playingSounds.Keys.ToArray())
            if (!IsPlaying(id, playingSounds[id]))
                playingSounds.Remove(id);
        foreach (var (nextId, pending) in pendingSounds.ToArray())
        {
            if (IsPlaying(pending.FirstId, pending.Channel)) continue;
            pendingSounds.Remove(nextId);
            PlaySound(nextId);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        if (ReferenceEquals(instance, this))
            instance = null;

        // 일부 해제가 실패해도 나머지 자원의 해제를 시도한다.
        if (masterGroup.hasHandle()) masterGroup.stop();
        if (bgmOverride.hasHandle()) bgmOverride.release();
        if (bgm.hasHandle()) bgm.release();
        foreach (var sound in sounds.Values)
            sound.release();
        sounds.Clear();
        playingSounds.Clear();
        pendingSounds.Clear();
        if (bgmGroup.hasHandle()) bgmGroup.release();
        if (soundGroup.hasHandle()) soundGroup.release();
        if (system.hasHandle()) system.release();
    }

    private FMOD.Sound GetSound(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return sounds.TryGetValue(id, out var sound) ? sound
            : throw new KeyNotFoundException($"등록되지 않은 효과음입니다: {id}");
    }

    // FMOD가 끝난 채널을 다른 소리에 재사용했을 때도 이전 소리를 재생 중으로 보지 않는다.
    private bool IsPlaying(string id, FMOD.Channel channel) =>
        channel.isPlaying(out bool playing) == FMOD.RESULT.OK && playing &&
        channel.getCurrentSound(out var sound) == FMOD.RESULT.OK && sound.handle == sounds[id].handle;

    private void SetVolume(FMOD.ChannelGroup group, float volume)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(volume) || volume is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(volume), "볼륨은 0~1 사이여야 합니다.");
        Check(group.setVolume(volume));
    }

    private static string ResolvePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path, AppContext.BaseDirectory);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("오디오 파일을 찾을 수 없습니다.", fullPath);
        return fullPath;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private static void Check(FMOD.RESULT result)
    {
        if (result != FMOD.RESULT.OK)
            throw new InvalidOperationException($"FMOD 오류 ({result}): {FMOD.Error.String(result)}");
    }

    /// <summary>FMOD를 사용할 수 없을 때의 무음 관리자. 모든 재생 요청을 무시한다.</summary>
    private sealed class SilentSoundManager : ISoundManager
    {
        public void LoadSound(string id, string path) { }
        public void PlaySound(string id) { }
        public void PlaySoundSequence(string firstId, string nextId) { }
        public SoundPlaybackState GetSoundState(string id) => SoundPlaybackState.Stopped;
        public void StopSound(string id) { }
        public void PlayBgm(string path, bool loop = true) { }
        public void PlayBgmOverride(string path) { }
        public void StopBgmOverride(int fadeInMilliseconds = 1000) { }
        public void StopBgm() { }
        public void StopAll() { }
        public void SetMasterVolume(float volume) { }
        public void SetBgmVolume(float volume) { }
        public void SetSoundVolume(float volume) { }
        public void Update() { }

        public void Dispose()
        {
            if (ReferenceEquals(instance, this))
                instance = null;
        }
    }
}
