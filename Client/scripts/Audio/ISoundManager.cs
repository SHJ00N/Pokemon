namespace Client;

enum SoundPlaybackState { Stopped, Pending, Playing }

interface ISoundManager : IDisposable
{
    void LoadSound(string id, string path);
    void PlaySound(string id);
    void PlaySoundSequence(string firstId, string nextId);
    SoundPlaybackState GetSoundState(string id);
    void StopSound(string id);
    void PlayBgm(string path, bool loop = true);
    void PlayBgmOverride(string path);
    void StopBgmOverride(int fadeInMilliseconds = 1000);
    void StopBgm();
    void StopAll();
    void SetMasterVolume(float volume);
    void SetBgmVolume(float volume);
    void SetSoundVolume(float volume);
    void Update();
}
