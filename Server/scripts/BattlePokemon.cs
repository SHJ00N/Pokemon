// 전투 중인 포켓몬. 레벨 50, 개체값 31(6V), 노력치 0, 성격 보정 없음으로 실제 능력치를 계산한다.
sealed class BattlePokemon
{
    public const int Level = 50;

    public PokemonInfo Info { get; }
    public PokemonMoveInfo[] Moves { get; }
    public int MaxHp { get; }
    public int Hp { get; set; }
    public int Attack { get; }
    public int Defense { get; }
    public int SpAttack { get; }
    public int SpDefense { get; }
    public int Speed { get; }

    public BattlePokemon(PokemonInfo info, PokemonMoveInfo[] moves)
    {
        Info = info;
        Moves = moves;
        MaxHp = Hp = (2 * info.BaseHp + 31) * Level / 100 + Level + 10;
        Attack = CalculateStat(info.BaseAttack);
        Defense = CalculateStat(info.BaseDefense);
        SpAttack = CalculateStat(info.BaseSpAttack);
        SpDefense = CalculateStat(info.BaseSpDefense);
        Speed = CalculateStat(info.BaseSpeed);
    }

    private static int CalculateStat(int baseStat) => (2 * baseStat + 31) * Level / 100 + 5;
}
