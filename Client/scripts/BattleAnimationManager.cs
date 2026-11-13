using Pokemon.Protocol;

namespace Client;

// 게임 상태·서버 메시지·콘솔 좌표를 모르는 연출 관리자. 상태마다 별도 인스턴스를 사용한다.
// 요청과 Update는 상태의 이벤트 루프에서 호출하고, 출력 스레드에는 CaptureFrame의 결과만 전달한다.
sealed class BattleAnimationManager
{
    public const int SwitchPauseMilliseconds = 500;
    private const int IdleCycleMilliseconds = 800;
    private readonly ISoundManager sound;
    private readonly SummonAnimation summonAnimation = new();
    private readonly WithdrawAnimation withdrawAnimation = new();
    private readonly BattleAnimation animation = new();
    private readonly StatusAnimation statusAnimation = new();
    private readonly HealingAnimation healingAnimation = new();
    private readonly FaintAnimation playerFaint = new(), opponentFaint = new();
    private readonly Queue<(Action Apply, bool StartsAnimation)> pending = new();
    private string? lastOverlaySound;
    private bool applyingStep;
    private long nextFrameAt;
    private long? idleStartedAt;
    private Action? replaceAfterWithdraw;
    private long? switchResumeAt;
    private bool switching;

    public BattleAnimationManager(ISoundManager sound)
    {
        ArgumentNullException.ThrowIfNull(sound);
        this.sound = sound;
    }

    private bool IsPlaying => switchResumeAt != null || replaceAfterWithdraw != null || withdrawAnimation.IsBusy || summonAnimation.IsBusy || animation.IsBusy || statusAnimation.IsBusy ||
        healingAnimation.IsBusy || playerFaint.IsBusy || opponentFaint.IsBusy;
    public bool IsBusy => IsPlaying || pending.Count > 0;
    public int PendingCount => pending.Count;
    public bool IsSummoning => summonAnimation.IsBusy;
    public bool IsSwitching => switching;
    public bool HoldInformation => replaceAfterWithdraw != null || animation.IsBusy || statusAnimation.IsBusy || healingAnimation.IsBusy;

    // 연출과 그 뒤에 반영할 상태 변경을 같은 순서로 처리한다. 상태 변경 내부의 요청은 함께 적용한다.
    public void ExecuteOrQueue(Action apply, bool startsAnimation = false)
    {
        ArgumentNullException.ThrowIfNull(apply);
        if (!applyingStep && IsBusy)
            pending.Enqueue((apply, startsAnimation));
        else
            ApplyStep(apply);
    }

    private void ApplyStep(Action apply)
    {
        bool previous = applyingStep;
        applyingStep = true;
        try { apply(); }
        finally { applyingStep = previous; }
    }

    public void StartSummon(bool onPlayer = true, bool onOpponent = true) => ExecuteOrQueue(() =>
    {
        summonAnimation.Start(onPlayer, onOpponent);
        nextFrameAt = 0;
    }, startsAnimation: true);

    // 퇴장 중에는 이전 화면을 유지하고, 완전히 사라진 뒤 새 포켓몬 정보로 바꾼다.
    public void PlaySwitch(bool onPlayer, bool onOpponent, Action replace,
        bool recallPlayer = true, bool recallOpponent = true)
    {
        ArgumentNullException.ThrowIfNull(replace);
        ExecuteOrQueue(() =>
        {
            if (!onPlayer && !onOpponent) { replace(); return; }
            switching = true;
            Action enter = () =>
            {
                replace();
                StartSummon(onPlayer, onOpponent);
            };
            if ((onPlayer && recallPlayer) || (onOpponent && recallOpponent))
            {
                withdrawAnimation.Start(onPlayer && recallPlayer, onOpponent && recallOpponent);
                replaceAfterWithdraw = enter;
                nextFrameAt = 0;
            }
            else
                enter();
        }, startsAnimation: true);
    }

    public void PlayAttack(TypeView type, bool fromPlayer, double effectiveness = 1) =>
        ExecuteOrQueue(() => animation.Enqueue(type, fromPlayer, effectiveness), startsAnimation: true);

    public void PlayStatus(string code, bool onPlayer) =>
        ExecuteOrQueue(() => statusAnimation.Enqueue(code, onPlayer), startsAnimation: true);

    public void PlayStat(int typeId, bool onPlayer, bool raised) =>
        ExecuteOrQueue(() => statusAnimation.EnqueueStat(typeId, onPlayer, raised), startsAnimation: true);

    public void PlayHealing(bool onPlayer) => ExecuteOrQueue(() =>
    {
        healingAnimation.Start(onPlayer);
        sound.PlaySoundSequence(SoundManager.HeldItemActivate, SoundManager.HealHp);
    }, startsAnimation: true);

    public void PlayFaint(int? playerRows, int? opponentRows)
    {
        if (playerRows == null && opponentRows == null) return;
        ExecuteOrQueue(() =>
        {
            if (playerRows is int own) playerFaint.Start(own);
            if (opponentRows is int other) opponentFaint.Start(other);
            sound.PlaySound(SoundManager.Faint);
        }, startsAnimation: true);
    }

    public void ResetFaint(bool onPlayer) => (onPlayer ? playerFaint : opponentFaint).Reset();

    public bool IsFrameDue(long now)
    {
        if (now < nextFrameAt) return false;
        nextFrameAt = now + (switchResumeAt != null || replaceAfterWithdraw != null || summonAnimation.IsBusy || playerFaint.IsBusy || opponentFaint.IsBusy ||
            healingAnimation.IsBusy ? 16 : 80);
        return true;
    }

    // true를 반환하면 호출한 상태가 연출 후의 HP·메뉴 등을 다시 그린다.
    public bool Update(long now)
    {
        idleStartedAt ??= now;
        bool wasPlaying = IsPlaying;
        bool changed = Advance(now);
        if (wasPlaying || IsBusy) idleStartedAt = now;
        return changed;
    }

    private bool Advance(long now)
    {
        bool switchPauseEnded = false;
        if (switchResumeAt is long resumeAt)
        {
            if (now < resumeAt) return false;
            switchResumeAt = null;
            switching = false;
            switchPauseEnded = true;
        }
        bool replaced = false;
        if (replaceAfterWithdraw is { } replace && !withdrawAnimation.IsBusy)
        {
            replaceAfterWithdraw = null;
            ApplyStep(replace);
            replaced = true;
        }
        bool wasWithdrawing = withdrawAnimation.IsBusy;
        withdrawAnimation.Update(now);
        if (wasWithdrawing && !withdrawAnimation.IsBusy) return true;
        bool wasSummoning = summonAnimation.IsBusy;
        if (summonAnimation.Update(now) is string summonSound)
        {
            if (summonSound == SoundManager.PokeballRelease)
                sound.StopSound(SoundManager.PokeballThrow);
            sound.StopSound(summonSound);
            sound.PlaySound(summonSound);
        }
        bool summoned = wasSummoning && !summonAnimation.IsBusy;
        // 교체된 포켓몬이 완전히 나타난 뒤에도 잠시 멈춰 후속 기술·상태 갱신을 대기시킨다.
        if (summoned && switching) switchResumeAt = now + SwitchPauseMilliseconds;
        bool wasFainting = playerFaint.IsBusy || opponentFaint.IsBusy;
        playerFaint.Update(now);
        opponentFaint.Update(now);
        // 마지막 줄이 사라진 프레임이 출력된 뒤에 다음 안내를 반영한다.
        if (wasFainting && !playerFaint.IsBusy && !opponentFaint.IsBusy) return true;
        UpdateEffects(now);
        bool changed = replaced || summoned;
        while (!IsPlaying && pending.TryPeek(out var next))
        {
            // 중간 HP·상태를 먼저 표시하고, 그 다음 프레임부터 다음 연출을 시작한다.
            if (changed && next.StartsAnimation) break;
            pending.Dequeue();
            ApplyStep(next.Apply);
            changed = true;
        }
        UpdateEffects(now);
        playerFaint.Update(now);
        opponentFaint.Update(now);
        return changed || switchPauseEnded;
    }

    private void UpdateEffects(long now)
    {
        if (healingAnimation.IsBusy)
            healingAnimation.Update(now, sound.GetSoundState(SoundManager.HealHp));
        if (statusAnimation.Update(now) is string soundId)
        {
            if (lastOverlaySound != null && lastOverlaySound != soundId)
                sound.StopSound(lastOverlaySound);
            sound.StopSound(soundId);
            sound.PlaySound(soundId);
            lastOverlaySound = soundId;
        }
        if (animation.Update(now) is double effectiveness)
            sound.PlaySound(effectiveness < 1 ? SoundManager.HitWeak
                : effectiveness > 1 ? SoundManager.HitSuper : SoundManager.HitNormal);
    }

    public BattleAnimationFrame CaptureFrame(long now, bool playerAlive = true, bool opponentAlive = true)
    {
        int idle = IsBusy || idleStartedAt == null ? 0
            : (now - idleStartedAt.Value) % IdleCycleMilliseconds < IdleCycleMilliseconds / 2 ? 0 : -1;
        return new BattleAnimationFrame(playerAlive ? idle : 0, opponentAlive ? idle : 0,
            animation.FlashPlayer, animation.FlashOpponent, animation.EffectType, animation.FromPlayer,
            animation.Progress, playerFaint.Progress, opponentFaint.Progress, statusAnimation.Overlay,
            healingAnimation.ShowImage ? healingAnimation.OnPlayer : null, summonAnimation.CurrentFrame,
            replaceAfterWithdraw != null ? withdrawAnimation.CurrentFrame : null);
    }
}
