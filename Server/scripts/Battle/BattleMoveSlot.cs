sealed class BattleMoveSlot
{
    public PokemonMoveInfo Move { get; }
    public int MaxPp { get; }
    public int Pp { get; private set; }

    public BattleMoveSlot(PokemonMoveInfo move, int? maxPp = null)
    {
        Move = move;
        MaxPp = Pp = maxPp ?? move.Pp;
    }

    public int SpendPp(int amount)
    {
        int spent = Math.Min(Pp, amount);
        Pp -= spent;
        return spent;
    }

    public void RestorePp() => Pp = MaxPp;
}
