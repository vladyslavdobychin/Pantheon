
namespace Pantheon.Core
{
    public sealed record GameStarted(PlayerSlot FirstPlayer, int Seed) : GameEvent;

    public sealed record TurnStarted(PlayerSlot Player, int TurnNumber) : GameEvent;

    public sealed record TurnEnded(PlayerSlot Player, int TurnNumber) : GameEvent;

    public sealed record CardDrawn(
        PlayerSlot Player,
        int CardId,
        string CardName,
        int HandCount,
        int CardsLeftInDeck
    ) : GameEvent;

    public sealed record CardBurned(
        PlayerSlot Player,
        int CardId,
        string CardName,
        int CardsLeftInDeck
    ) : GameEvent;

    public sealed record FatigueDamageTaken(
        PlayerSlot Player,
        int Amount,
        int HealthBefore,
        int HealthAfter
    ) : GameEvent;

    public sealed record GameEnded(GameResult Result) : GameEvent;
}
