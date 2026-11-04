namespace Client;

interface ISoundManager : IDisposable
{
    void LoadSound(string id, string path);
    void PlaySound(string id);
    void PlayBgm(string path, bool loop = true);
    void StopBgm();
    void StopAll();
    void SetMasterVolume(float volume);
    void SetBgmVolume(float volume);
    void SetSoundVolume(float volume);
    void Update();
}
