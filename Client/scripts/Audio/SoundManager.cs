namespace Client;

/// <summary>
/// FMOD Core 기반 오디오 관리자. 호출은 한 곳에서 순차적으로 수행한다.
/// 상대 경로는 실행 파일 폴더 기준이며, Update를 주기적으로 호출해야 한다.
/// </summary>
sealed class SoundManager : ISoundManager
{
    private static SoundManager? instance;

    /// <summary>Program에서 초기화한 클라이언트 공용 오디오 관리자.</summary>
    public static ISoundManager Instance => instance
        ?? throw new InvalidOperationException("SoundManager를 먼저 초기화해야 합니다.");

    /// <summary>시작 시 한 번 호출하고 반환한 인스턴스를 using으로 관리한다.</summary>
    public static ISoundManager Initialize(int maxChannels = 128)
    {
        if (instance != null)
            throw new InvalidOperationException("SoundManager가 이미 초기화되어 있습니다.");

        instance = new SoundManager(maxChannels);
        return instance;
    }

    private FMOD.System system;
    private FMOD.ChannelGroup masterGroup;
    private FMOD.ChannelGroup bgmGroup;
    private FMOD.ChannelGroup soundGroup;
    private readonly Dictionary<string, FMOD.Sound> sounds = new(StringComparer.Ordinal);
    private FMOD.Sound bgm;
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
            Check(system.createChannelGroup("Sound", out soundGroup));
            Check(masterGroup.addGroup(bgmGroup));
            Check(masterGroup.addGroup(soundGroup));
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

    /// <summary>등록한 효과음을 재생한다. 여러 효과음을 동시에 재생할 수 있다.</summary>
    public void PlaySound(string id)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!sounds.TryGetValue(id, out var sound))
            throw new KeyNotFoundException($"등록되지 않은 효과음입니다: {id}");

        Check(system.playSound(sound, soundGroup, false, out _));
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
            Check(system.playSound(nextBgm, bgmGroup, false, out _));
            bgm = nextBgm;
        }
        catch
        {
            nextBgm.release();
            throw;
        }
    }

    public void StopBgm()
    {
        ThrowIfDisposed();
        Check(bgmGroup.stop());
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
    }

    public void SetMasterVolume(float volume) => SetVolume(masterGroup, volume);
    public void SetBgmVolume(float volume) => SetVolume(bgmGroup, volume);
    public void SetSoundVolume(float volume) => SetVolume(soundGroup, volume);

    public void Update()
    {
        ThrowIfDisposed();
        Check(system.update());
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
        if (bgm.hasHandle()) bgm.release();
        foreach (var sound in sounds.Values)
            sound.release();
        sounds.Clear();
        if (bgmGroup.hasHandle()) bgmGroup.release();
        if (soundGroup.hasHandle()) soundGroup.release();
        if (system.hasHandle()) system.release();
    }

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
}
