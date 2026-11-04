namespace Pantheon.Core
{
    /// <summary>
    /// A request to change the game state. Carries what to do, never who is doing it —
    /// the actor is always GameState.GetCurrentPlayer().
    /// </summary>
    public abstract record Move;
}
